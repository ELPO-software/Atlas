using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Core.Settings;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.App.Tests;

/// <summary>Hands out copies, as a database would, and can fail on demand.</summary>
internal sealed class FakeWorkspaceRepository : IWorkspaceRepository
{
    private readonly Dictionary<Guid, Workspace> _workspaces = [];

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    /// <summary>While set, writes wait for this task, which keeps an operation in progress.</summary>
    public Task? WriteGate { get; set; }

    public int WriteCount { get; private set; }

    /// <summary>The history written with the changes, shared with <see cref="FakeHistoryRepository"/>.</summary>
    public List<HistoryEntry> History { get; } = [];

    public Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfSet(ReadFailure);
        return Task.FromResult<IReadOnlyList<WorkspaceSummary>>(
            _workspaces.Values
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => new WorkspaceSummary(w.Id, w.Name, w.Description, w.Resources.Count, w.UpdatedAt, w.LastUsedAt))
                .ToList());
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfSet(ReadFailure);
        return Task.FromResult(_workspaces.Count);
    }

    public Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(ReadFailure);
        return Task.FromResult(_workspaces.TryGetValue(id, out var workspace) ? Copy(workspace) : null);
    }

    public async Task AddAsync(Workspace workspace, HistoryEntry? record = null, CancellationToken cancellationToken = default)
    {
        await PassThroughGateAsync();
        WriteCount++;
        _workspaces.Add(workspace.Id, Copy(workspace));
        Record(record);
    }

    public async Task<bool> UpdateAsync(Workspace workspace, HistoryEntry? record = null, CancellationToken cancellationToken = default)
    {
        await PassThroughGateAsync();
        if (!_workspaces.ContainsKey(workspace.Id))
            return false;

        WriteCount++;
        _workspaces[workspace.Id] = Copy(workspace);
        Record(record);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, HistoryEntry? record = null, CancellationToken cancellationToken = default)
    {
        await PassThroughGateAsync();
        WriteCount++;
        var removed = _workspaces.Remove(id);
        if (removed)
            Record(record);

        return removed;
    }

    private void Record(HistoryEntry? record)
    {
        if (record is not null)
            History.Add(record);
    }

    /// <summary>Stores a workspace directly, without counting as a write made by the code under test.</summary>
    public void Seed(Workspace workspace) => _workspaces.Add(workspace.Id, Copy(workspace));

    public Workspace? Stored(Guid id) => _workspaces.GetValueOrDefault(id);

    private async Task PassThroughGateAsync()
    {
        if (WriteGate is { } gate)
            await gate;

        ThrowIfSet(WriteFailure);
    }

    private static void ThrowIfSet(Exception? failure)
    {
        if (failure is not null)
            throw failure;
    }

    private static Workspace Copy(Workspace w) =>
        Workspace.Restore(w.Id, w.Name, w.Description, w.Notes, w.CreatedAt, w.UpdatedAt, w.LastUsedAt, w.Resources);
}

internal sealed class FakeVersionRepository(FakeWorkspaceRepository workspaces) : IVersionRepository
{
    public List<WorkspaceVersion> Versions { get; } = [];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<VersionSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<VersionSummary>>(Versions
            .Where(v => v.WorkspaceId == workspaceId)
            .OrderByDescending(v => v.SavedAt)
            .Select(v => new VersionSummary(v.Id, v.WorkspaceId, v.Name, v.SavedAt, v.IsAutomatic, v.ResourceCount))
            .ToList());
    }

    public Task<int> CountAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult(Versions.Count(v => v.WorkspaceId == workspaceId));
    }

    public Task<WorkspaceVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Versions.FirstOrDefault(v => v.Id == versionId));

    public Task AddAsync(WorkspaceVersion version, HistoryEntry record, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        Versions.Add(version);
        workspaces.History.Add(record);
        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
            throw Failure;
    }
}

internal sealed class FakeHistoryRepository(FakeWorkspaceRepository workspaces) : IHistoryRepository
{
    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken = default) =>
        Failure is null
            ? Task.FromResult<IReadOnlyList<HistoryEntry>>(workspaces.History.OrderByDescending(e => e.OccurredAt).Take(limit).ToList())
            : Task.FromException<IReadOnlyList<HistoryEntry>>(Failure);

    public Task RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
            return Task.FromException(Failure);

        workspaces.History.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public Exception? Failure { get; set; }

    public int WriteCount { get; private set; }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Failure is null ? Task.FromResult(_values.GetValueOrDefault(key)) : Task.FromException<string?>(Failure);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
            return Task.FromException(Failure);

        WriteCount++;
        _values[key] = value;
        return Task.CompletedTask;
    }
}

internal sealed class FakeDialogService(AtlasDataPaths paths) : IDialogService
{
    public bool ConfirmAnswer { get; set; }

    public List<ConfirmationRequest> Confirmations { get; } = [];

    /// <summary>Plays the part of the user in the form. Null means the user cancels.</summary>
    public Action<WorkspaceEditorViewModel>? FillEditor { get; set; }

    public List<WorkspaceEditorRequest> Editors { get; } = [];

    /// <summary>The last form shown, to inspect what it displays after the user acted.</summary>
    public WorkspaceEditorViewModel? LastEditor { get; private set; }

    /// <summary>Answers each confirmation differently when set; otherwise every answer is <see cref="ConfirmAnswer"/>.</summary>
    public Func<ConfirmationRequest, bool>? Decide { get; set; }

    /// <summary>Plays the part of the user in the resource form. Null means the user cancels.</summary>
    public Action<ResourceEditorViewModel>? FillResourceEditor { get; set; }

    public List<ResourceEditorRequest> ResourceEditors { get; } = [];

    public ResourceEditorViewModel? LastResourceEditor { get; private set; }

    public FakeFilePicker Picker { get; } = new();

    public FakeApplicationCatalog Applications { get; } = new();

    public Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        Confirmations.Add(request);
        return Task.FromResult(Decide?.Invoke(request) ?? ConfirmAnswer);
    }

    /// <summary>The answer to the import preview.</summary>
    public bool ImportAnswer { get; set; }

    public List<ImportPreviewViewModel> ImportPreviews { get; } = [];

    public Task<bool> ConfirmImportAsync(ImportPreviewViewModel preview)
    {
        ImportPreviews.Add(preview);
        return Task.FromResult(ImportAnswer);
    }

    public async Task<Workspace?> EditResourceAsync(ResourceEditorRequest request)
    {
        ResourceEditors.Add(request);
        LastResourceEditor = new ResourceEditorViewModel(
            request, Picker, Applications, paths, NullLogger<ResourceEditorViewModel>.Instance);

        if (FillResourceEditor is null)
            return null;

        FillResourceEditor(LastResourceEditor);
        await LastResourceEditor.SubmitCommand.ExecuteAsync(null);
        return LastResourceEditor.Result;
    }

    public async Task<Workspace?> EditWorkspaceAsync(WorkspaceEditorRequest request)
    {
        Editors.Add(request);
        LastEditor = new WorkspaceEditorViewModel(request, paths, NullLogger<WorkspaceEditorViewModel>.Instance);

        if (FillEditor is null)
            return null;

        FillEditor(LastEditor);
        await LastEditor.SubmitCommand.ExecuteAsync(null);
        return LastEditor.Result;
    }
}

internal sealed class FakeFilePicker : IFilePicker
{
    /// <summary>What the user picks; null means they cancel.</summary>
    public string? NextPath { get; set; }

    public List<string> Requests { get; } = [];

    public Task<string?> PickProgramAsync() => Pick("program");

    public Task<string?> PickFileAsync() => Pick("file");

    public Task<string?> PickFolderAsync() => Pick("folder");

    public Task<string?> PickImportFileAsync() => Pick("import");

    public Task<string?> PickExportFileAsync(string suggestedName)
    {
        SuggestedNames.Add(suggestedName);
        return Pick("export");
    }

    public List<string> SuggestedNames { get; } = [];

    private Task<string?> Pick(string what)
    {
        Requests.Add(what);
        return Task.FromResult(NextPath);
    }
}

internal sealed class FakeApplicationCatalog : IApplicationCatalog
{
    public List<InstalledApplication> Installed { get; } = [];

    public Exception? Failure { get; set; }

    public int Reads { get; private set; }

    public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Failure is null
            ? Task.FromResult<IReadOnlyList<InstalledApplication>>(Installed.ToList())
            : Task.FromException<IReadOnlyList<InstalledApplication>>(Failure);
    }
}

/// <summary>Records what would have been opened; nothing is ever opened for real.</summary>
internal sealed class FakeResourceOpener : IResourceOpener
{
    private readonly object _gate = new();
    private readonly List<string> _opened = [];

    public Dictionary<string, OpenResult> Answers { get; } = [];

    /// <summary>While set, every opening waits for it, which keeps a restoration in progress.</summary>
    public Task? Hold { get; set; }

    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_gate)
                return _opened.ToList();
        }
    }

    public async Task<OpenResult> OpenAsync(ResourceKind kind, string target)
    {
        lock (_gate)
            _opened.Add(target);

        if (Hold is { } hold)
            await hold;

        return Answers.GetValueOrDefault(target) ?? (kind == ResourceKind.Application ? OpenResult.Launched() : OpenResult.Opened());
    }
}

internal sealed class FakeThemeController : IThemeController
{
    public List<ThemePreference> Applied { get; } = [];

    public void Apply(ThemePreference preference) => Applied.Add(preference);
}

internal sealed class FakeFolderOpener : IFolderOpener
{
    public bool Succeeds { get; set; } = true;

    public List<string> Opened { get; } = [];

    public Task<bool> OpenAsync(string path)
    {
        Opened.Add(path);
        return Task.FromResult(Succeeds);
    }
}

internal sealed class RecordingNavigator : INavigator
{
    public List<string> Visits { get; } = [];

    public Task OpenHomeAsync() => Record("home");

    public Task OpenWorkspacesAsync() => Record("workspaces");

    public Task OpenWorkspaceAsync(Guid workspaceId) => Record(workspaceId.ToString());

    public Task OpenSettingsAsync() => Record("settings");

    private Task Record(string visit)
    {
        Visits.Add(visit);
        return Task.CompletedTask;
    }
}

/// <summary>Everything a page needs, with a fixed clock and no database, window or disk.</summary>
internal sealed class TestServices
{
    public const string LogsMarker = "journaux-de-test";

    public TestServices()
    {
        Paths = new AtlasDataPaths(Path.Combine(Path.GetTempPath(), LogsMarker));
        Dialogs = new FakeDialogService(Paths);
        Workspaces = new WorkspaceService(Repository, Clock);
        Versions = new FakeVersionRepository(Repository);
        History = new FakeHistoryRepository(Repository);
        Pages = new PageServices(
            Workspaces,
            new VersionService(Workspaces, Versions, Clock),
            History,
            new SettingsService(SettingsStore),
            new RestorationService(Opener, Clock),
            Dialogs,
            Files,
            Theme,
            Folders,
            Paths,
            Clock,
            NullLoggerFactory.Instance);
    }

    public FakeWorkspaceRepository Repository { get; } = new();

    public FakeResourceOpener Opener { get; } = new();

    public FakeVersionRepository Versions { get; }

    public FakeHistoryRepository History { get; }

    /// <summary>The pickers the pages use, as opposed to those of the resource form.</summary>
    public FakeFilePicker Files { get; } = new();

    public FakeSettingsStore SettingsStore { get; } = new();

    public FakeDialogService Dialogs { get; }

    public FakeThemeController Theme { get; } = new();

    public FakeFolderOpener Folders { get; } = new();

    public RecordingNavigator Navigator { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public AtlasDataPaths Paths { get; }

    public WorkspaceService Workspaces { get; }

    public PageServices Pages { get; }

    public Workspace SeedWorkspace(string name = "Études d'été", string? description = null)
    {
        var workspace = Workspace.Create(name, description, Clock.GetUtcNow());
        Repository.Seed(workspace);
        return workspace;
    }
}
