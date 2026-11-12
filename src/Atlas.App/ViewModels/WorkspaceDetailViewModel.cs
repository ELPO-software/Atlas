using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Core.Validation;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class WorkspaceDetailViewModel(
    Guid workspaceId,
    PageServices services,
    INavigator navigator) : PageViewModel
{
    /// <summary>Notes are saved after this pause in typing.</summary>
    internal static readonly TimeSpan NotesSaveDelay = TimeSpan.FromSeconds(1);

    /// <summary>From this number of programs, the user confirms before they start.</summary>
    internal const int ApplicationsNeedingConfirmation = 2;

    private readonly ILogger _logger = services.Loggers.CreateLogger<WorkspaceDetailViewModel>();
    private readonly SemaphoreSlim _notesSaveGate = new(1, 1);

    private CancellationTokenSource? _notesDelay;
    private CancellationTokenSource? _restoreStop;
    private string _savedNotes = string.Empty;
    private bool _isLoadingNotes;
    private bool _isDeleted;
    private bool _isPreparingRestore;
    private int _resourceCount;
    private int _versionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(IsLoading))]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    public partial string? LoadError { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CreatedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ModifiedText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResources), nameof(CanRestore))]
    public partial IReadOnlyList<ResourceGroupViewModel> ResourceGroups { get; private set; } = [];

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NotesStatus { get; private set; }

    [ObservableProperty]
    public partial string? NotesError { get; private set; }

    [ObservableProperty]
    public partial string? ActionError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(CanChange))]
    public partial bool IsRestoring { get; private set; }

    [ObservableProperty]
    public partial string RestoreProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int RestoreCompleted { get; private set; }

    [ObservableProperty]
    public partial int RestoreTotal { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    public partial RestoreReportViewModel? Report { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoVersions))]
    public partial IReadOnlyList<VersionItemViewModel> Versions { get; private set; } = [];

    [ObservableProperty]
    public partial string? VersionsError { get; private set; }

    /// <summary>A confirmation of what an action did, such as where a file was exported.</summary>
    [ObservableProperty]
    public partial string? ActionInfo { get; private set; }

    public bool HasNoVersions => Versions.Count == 0;

    public bool HasNoResources => ResourceGroups.Count == 0;

    public bool CanRestore => IsLoaded && !HasNoResources && !IsRestoring;

    /// <summary>True while the workspace is being read, for the progress line.</summary>
    public bool IsLoading => !IsLoaded && LoadError is null;

    /// <summary>The workspace cannot change while it is being restored.</summary>
    public bool CanChange => !IsRestoring;

    public bool HasReport => Report is not null;

    public override bool HasUnsavedChanges =>
        IsRestoring || (!_isDeleted && !string.Equals(_savedNotes, Notes, StringComparison.Ordinal));

    /// <summary>The automatic save in progress, if any. Lets tests wait for it.</summary>
    internal Task PendingNotesSave { get; private set; } = Task.CompletedTask;

    /// <summary>The restoration in progress, if any.</summary>
    internal Task PendingRestore { get; private set; } = Task.CompletedTask;

    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspace = await services.Workspaces.GetAsync(workspaceId);

            ShowDetails(workspace);
            ShowResources(workspace);

            _savedNotes = workspace.Notes;
            ShowNotes(workspace);

            IsLoaded = true;
        }
        catch (WorkspaceNotFoundException)
        {
            LoadError = Strings.WorkspaceNotFound;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace {WorkspaceId} could not be read", workspaceId);
            LoadError = UiText.Format(Strings.WorkspaceReadFailed, services.Paths.LogsDirectory);
            return;
        }

        if (IsLoaded)
            await LoadVersionsAsync();
    }

    public override async Task<bool> TryLeaveAsync()
    {
        if (!IsLoaded || _isDeleted)
            return true;

        if (IsRestoring)
        {
            var stop = await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
                Strings.StopRestoreTitle, Strings.StopRestoreMessage, Strings.StopAndLeave, Strings.Stay));
            if (!stop)
                return false;

            _restoreStop?.Cancel();
            await PendingRestore;
        }

        CancelNotesDelay();
        if (await SaveNotesAsync())
            return true;

        return await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            Strings.LeaveWithoutSavingTitle,
            Strings.LeaveWithoutSavingMessage,
            Strings.LeaveWithoutSaving,
            Strings.Stay));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task BackAsync() => navigator.OpenWorkspacesAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RestoreAsync()
    {
        // A second click while the first one is still preparing must not start a second restoration.
        if (!CanRestore || _isPreparingRestore)
            return;

        _isPreparingRestore = true;
        try
        {
            ActionError = null;
            RestorePlan plan;
            try
            {
                plan = RestorePlan.For(await services.Workspaces.GetAsync(workspaceId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The workspace {WorkspaceId} could not be read before restoring it", workspaceId);
                ActionError = UiText.Format(Strings.RestoreFailed, services.Paths.LogsDirectory);
                return;
            }

            var applications = plan.ApplicationsToLaunch;
            if (applications.Count >= ApplicationsNeedingConfirmation && !await ConfirmLaunchAsync(applications))
                return;

            PendingRestore = RunRestorationAsync(plan, applications.Count > 0);
        }
        finally
        {
            _isPreparingRestore = false;
        }

        await PendingRestore;
    }

    [RelayCommand]
    private void StopRestore() => _restoreStop?.Cancel();

    [RelayCommand]
    private void CloseReport() => Report = null;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddResourceAsync()
    {
        ActionError = null;
        var updated = await services.Dialogs.EditResourceAsync(new ResourceEditorRequest(
            Strings.AddResourceDialogTitle,
            Strings.Add,
            FixedKind: null,
            DisplayName: string.Empty,
            Target: string.Empty,
            (kind, name, target) => services.Workspaces.AddResourceAsync(workspaceId, kind, name, target)));

        if (updated is not null)
            ShowChange(updated);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task EditAsync()
    {
        ActionError = null;
        var updated = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.EditWorkspaceDialogTitle,
            Strings.Save,
            Name,
            Description,
            ShowDescription: true,
            (name, description) => services.Workspaces.UpdateDetailsAsync(workspaceId, name, description)));

        if (updated is not null)
            ShowDetails(updated);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DuplicateAsync()
    {
        ActionError = null;
        var copy = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.DuplicateWorkspaceDialogTitle,
            Strings.Duplicate,
            SuggestCopyName(Name),
            Description,
            ShowDescription: false,
            (name, _) => DuplicateWithLatestNotesAsync(name)));

        if (copy is not null)
            await navigator.OpenWorkspaceAsync(copy.Id);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DeleteAsync()
    {
        ActionError = null;
        var message = _versionCount == 0
            ? UiText.Format(Strings.DeleteWorkspaceMessage, Name, UiText.ResourceCount(_resourceCount))
            : UiText.Format(Strings.DeleteWorkspaceMessageWithVersions, Name, UiText.ResourceCount(_resourceCount), VersionCount(_versionCount));
        var confirmed = await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            Strings.DeleteWorkspaceTitle, message, Strings.Delete, Strings.Cancel));

        if (!confirmed)
            return;

        CancelNotesDelay();
        await PendingNotesSave;
        try
        {
            await services.Workspaces.DeleteAsync(workspaceId);
            _isDeleted = true;
        }
        catch (WorkspaceNotFoundException)
        {
            _isDeleted = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace {WorkspaceId} could not be deleted", workspaceId);
            ActionError = UiText.Format(Strings.DeleteFailed, services.Paths.LogsDirectory);

            // Notes typed before this attempt still have to reach the database.
            if (HasUnsavedChanges)
                ScheduleNotesSave();

            return;
        }

        await navigator.OpenWorkspacesAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetrySaveNotesAsync() => SaveNotesAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SaveVersionAsync()
    {
        ActionError = null;
        var saved = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.SaveVersionDialogTitle,
            Strings.Save,
            UiText.Format(Strings.DefaultVersionName, DateText.Full(services.Time.GetUtcNow(), services.Time)),
            string.Empty,
            ShowDescription: false,
            async (name, _) =>
            {
                // A version saved right after typing must contain what was just typed.
                CancelNotesDelay();
                await SaveNotesAsync();
                await services.Versions.SaveAsync(workspaceId, name);
                return await services.Workspaces.GetAsync(workspaceId);
            }));

        if (saved is not null)
            await LoadVersionsAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ExportAsync()
    {
        ActionError = null;
        ActionInfo = null;
        try
        {
            CancelNotesDelay();
            await SaveNotesAsync();
            var workspace = await services.Workspaces.GetAsync(workspaceId);

            if (ExportPrivacy.RevealedUserName(
                    workspace.Resources.Select(r => r.Target),
                    Environment.UserName,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) is { } userName
                && !await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
                    Strings.ExportUserNameTitle,
                    UiText.Format(Strings.ExportUserNameMessage, userName),
                    Strings.ExportAnyway,
                    Strings.Cancel) { IsDestructive = false }))
            {
                return;
            }

            if (await services.Files.PickExportFileAsync(ExportPrivacy.FileNameFor(workspace.Name)) is not { } path)
                return;

            var now = services.Time.GetUtcNow();
            await WorkspaceFiles.WriteAsync(path, WorkspaceDocument.Write(workspace, now));
            ActionInfo = UiText.Format(Strings.ExportDone, path);
            await RecordAsync(new HistoryEntry(now, HistoryAction.WorkspaceExported, workspace.Id, workspace.Name, Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace {WorkspaceId} could not be exported", workspaceId);
            ActionError = UiText.Format(Strings.ExportFailed, services.Paths.LogsDirectory);
        }
    }

    private async Task RestoreVersionAsync(VersionSummary version)
    {
        ActionError = null;
        var confirmed = await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            UiText.Format(Strings.RestoreVersionTitle, version.Name),
            Strings.RestoreVersionMessage,
            Strings.RestoreVersion,
            Strings.Cancel) { IsDestructive = false });

        if (!confirmed)
            return;

        try
        {
            // The current state, saved first as a version, must include what was just typed.
            CancelNotesDelay();
            await SaveNotesAsync();
            var workspace = await services.Versions.RestoreAsync(version.Id, UiText.Format(Strings.SafetyVersionName, version.Name));

            ShowDetails(workspace);
            ShowResources(workspace);
            ShowNotes(workspace);
            NotesStatus = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The version {VersionId} could not be restored", version.Id);
            ActionError = UiText.Format(Strings.VersionRestoreFailed, services.Paths.LogsDirectory);
        }

        await LoadVersionsAsync();
    }

    private async Task ApproveResourceAsync(Resource resource)
    {
        ActionError = null;
        var confirmed = await services.Dialogs.ConfirmAsync(
            new ConfirmationRequest(
                UiText.Format(Strings.ApproveResourceTitle, resource.DisplayName),
                Strings.ApproveResourceMessage,
                Strings.Approve,
                Strings.Cancel)
            {
                Details = [new ConfirmationDetail(resource.DisplayName, resource.Target)],
                IsDestructive = false,
            });

        if (!confirmed)
            return;

        try
        {
            ShowChange(await services.Workspaces.ApproveResourceAsync(workspaceId, resource.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The resource {ResourceId} could not be approved", resource.Id);
            ActionError = UiText.Format(Strings.SaveFailed, services.Paths.LogsDirectory);
        }
    }

    private async Task LoadVersionsAsync()
    {
        VersionsError = null;
        try
        {
            var versions = await services.Versions.ListAsync(workspaceId);
            Versions = versions.Select(v => new VersionItemViewModel(v, services.Time, RestoreVersionAsync)).ToList();
            _versionCount = versions.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The versions of workspace {WorkspaceId} could not be read", workspaceId);
            VersionsError = UiText.Format(Strings.VersionsReadFailed, services.Paths.LogsDirectory);
        }
    }

    /// <summary>A failure to record history never undoes or hides what was done.</summary>
    private async Task RecordAsync(HistoryEntry entry)
    {
        try
        {
            await services.History.RecordAsync(entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The history line for workspace {WorkspaceId} could not be written", workspaceId);
        }
    }

    partial void OnNotesChanged(string value)
    {
        if (!_isLoadingNotes)
            ScheduleNotesSave();
    }

    private async Task EditResourceAsync(Resource resource)
    {
        ActionError = null;
        var updated = await services.Dialogs.EditResourceAsync(new ResourceEditorRequest(
            Strings.EditResourceDialogTitle,
            Strings.Save,
            resource.Kind,
            resource.DisplayName,
            resource.Target,
            (_, name, target) => services.Workspaces.UpdateResourceAsync(workspaceId, resource.Id, name, target)));

        if (updated is not null)
            ShowChange(updated);
    }

    private async Task RemoveResourceAsync(Resource resource)
    {
        ActionError = null;
        var confirmed = await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            Strings.RemoveResourceTitle,
            UiText.Format(Strings.RemoveResourceMessage, resource.DisplayName),
            Strings.Remove,
            Strings.Cancel));

        if (!confirmed)
            return;

        try
        {
            ShowChange(await services.Workspaces.RemoveResourceAsync(workspaceId, resource.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The resource {ResourceId} could not be removed", resource.Id);
            ActionError = UiText.Format(Strings.SaveFailed, services.Paths.LogsDirectory);
        }
    }

    private async Task<bool> ConfirmLaunchAsync(IReadOnlyList<Resource> applications) =>
        await services.Dialogs.ConfirmAsync(
            new ConfirmationRequest(
                UiText.Format(Strings.LaunchApplicationsTitle, applications.Count),
                Strings.LaunchApplicationsMessage,
                Strings.LaunchAndOpen,
                Strings.Cancel)
            {
                Details = applications.Select(a => new ConfirmationDetail(a.DisplayName, RestoreText.Target(a))).ToList(),
                IsDestructive = false,
            });

    private async Task RunRestorationAsync(RestorePlan plan, bool startsApplications)
    {
        Report = null;
        IsRestoring = true;
        RestoreCompleted = 0;
        RestoreTotal = plan.Steps.Count;
        _restoreStop = new CancellationTokenSource();

        var progress = new Progress<RestoreProgress>(p =>
        {
            RestoreCompleted = p.Completed;
            RestoreProgressText = p.Current is { } current
                ? UiText.Format(Strings.RestoringProgress, p.Completed + 1, p.Total, current.DisplayName)
                : startsApplications ? Strings.CheckingApplications : RestoreProgressText;
        });

        try
        {
            var report = await services.Restoration.RunAsync(plan, progress, _restoreStop.Token);
            Report = RestoreReportViewModel.From(report);
            await RecordUseAsync(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The restoration of workspace {WorkspaceId} stopped unexpectedly", workspaceId);
            ActionError = UiText.Format(Strings.RestoreFailed, services.Paths.LogsDirectory);
        }
        finally
        {
            IsRestoring = false;
            _restoreStop.Dispose();
            _restoreStop = null;
        }
    }

    /// <summary>Puts the workspace among the recent ones. Failing to record it does not change what was restored.</summary>
    private async Task RecordUseAsync(RestoreReport report)
    {
        try
        {
            await services.Workspaces.MarkUsedAsync(
                workspaceId,
                new RestoreCounts(
                    report.Count(RestoreStatus.Restored),
                    report.Count(RestoreStatus.Missing),
                    report.Count(RestoreStatus.Failed),
                    report.Count(RestoreStatus.Skipped)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The restoration of workspace {WorkspaceId} could not be recorded", workspaceId);
        }
    }

    private void ScheduleNotesSave()
    {
        NotesStatus = Strings.NotesSaving;
        NotesError = null;

        CancelNotesDelay();
        _notesDelay = new CancellationTokenSource();
        PendingNotesSave = SaveNotesAfterDelayAsync(_notesDelay.Token);
    }

    private void CancelNotesDelay()
    {
        _notesDelay?.Cancel();
        _notesDelay?.Dispose();
        _notesDelay = null;
    }

    private async Task SaveNotesAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NotesSaveDelay, services.Time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SaveNotesAsync();
    }

    /// <summary>
    /// Stores the notes as they are now. Saves run one at a time, and text typed during a save is stored
    /// right after it, so what ends up in the database is always the latest text.
    /// </summary>
    /// <returns>True if nothing remains unsaved.</returns>
    private async Task<bool> SaveNotesAsync()
    {
        await _notesSaveGate.WaitAsync();
        try
        {
            while (!string.Equals(_savedNotes, Notes, StringComparison.Ordinal))
            {
                var text = Notes;
                NotesStatus = Strings.NotesSaving;
                NotesError = null;

                var saved = await services.Workspaces.UpdateNotesAsync(workspaceId, text);

                _savedNotes = text;
                ModifiedText = DateText.Modified(saved.UpdatedAt, services.Time);
            }

            // Also reached when the text went back to what was already stored: there is nothing left to save.
            if (NotesStatus is not null)
                NotesStatus = Strings.NotesSaved;

            return true;
        }
        catch (AtlasValidationException ex)
        {
            NotesStatus = null;
            NotesError = ValidationText.Describe(ex.Errors[0]);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The notes of workspace {WorkspaceId} could not be saved", workspaceId);
            NotesStatus = null;
            NotesError = UiText.Format(Strings.NotesSaveFailed, services.Paths.LogsDirectory);
            return false;
        }
        finally
        {
            _notesSaveGate.Release();
        }
    }

    /// <summary>A copy made right after typing must contain what was just typed.</summary>
    private async Task<Workspace> DuplicateWithLatestNotesAsync(string? name)
    {
        CancelNotesDelay();
        await SaveNotesAsync();
        return await services.Workspaces.DuplicateAsync(workspaceId, name);
    }

    private void ShowChange(Workspace workspace)
    {
        ShowResources(workspace);
        ModifiedText = DateText.Modified(workspace.UpdatedAt, services.Time);
    }

    private void ShowDetails(Workspace workspace)
    {
        Name = workspace.Name;
        Description = workspace.Description;
        CreatedText = DateText.Created(workspace.CreatedAt, services.Time);
        ModifiedText = DateText.Modified(workspace.UpdatedAt, services.Time);
    }

    private void ShowResources(Workspace workspace)
    {
        ResourceGroups = ResourceGroupViewModel.From(workspace.Resources, EditResourceAsync, RemoveResourceAsync, ApproveResourceAsync);
        _resourceCount = workspace.Resources.Count;
    }

    /// <summary>Shows stored notes without counting them as typed.</summary>
    private void ShowNotes(Workspace workspace)
    {
        _savedNotes = workspace.Notes;
        _isLoadingNotes = true;
        Notes = workspace.Notes;
        _isLoadingNotes = false;
    }

    private static string VersionCount(int count) =>
        UiText.Format(count == 1 ? Strings.VersionCountOne : Strings.VersionCountMany, count);

    private static string SuggestCopyName(string name)
    {
        var suggestion = UiText.Format(Strings.CopyNameFormat, name);
        var excess = suggestion.Length - WorkspaceRules.NameMaxLength;

        return excess <= 0
            ? suggestion
            : UiText.Format(Strings.CopyNameFormat, name[..^excess].TrimEnd());
    }
}
