using System.Globalization;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class ResourceEditorViewModel(
    ResourceEditorRequest request,
    IFilePicker picker,
    IApplicationCatalog applications,
    AtlasDataPaths paths,
    ILogger<ResourceEditorViewModel> logger) : ViewModelBase
{
    private IReadOnlyList<InstalledApplication>? _installedApplications;
    private string? _suggestedName;
    private bool _isSubmitting;

    /// <summary>Raised once the values are saved, when the form can close.</summary>
    public event EventHandler? Completed;

    public string Title => request.Title;

    public string ConfirmLabel => request.ConfirmLabel;

    public bool CanChooseKind => request.FixedKind is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsApplication), nameof(IsFile), nameof(IsFolder), nameof(IsLink),
        nameof(TargetLabel), nameof(TargetPlaceholder), nameof(CanBrowse))]
    public partial ResourceKind Kind { get; private set; } = request.FixedKind ?? ResourceKind.Application;

    public bool IsApplication
    {
        get => Kind == ResourceKind.Application;
        set => ChooseKind(value, ResourceKind.Application);
    }

    public bool IsFile
    {
        get => Kind == ResourceKind.File;
        set => ChooseKind(value, ResourceKind.File);
    }

    public bool IsFolder
    {
        get => Kind == ResourceKind.Folder;
        set => ChooseKind(value, ResourceKind.Folder);
    }

    public bool IsLink
    {
        get => Kind == ResourceKind.Url;
        set => ChooseKind(value, ResourceKind.Url);
    }

    public string TargetLabel => KindText(Strings.TargetLabelApplication, Strings.TargetLabelFile, Strings.TargetLabelFolder, Strings.TargetLabelLink);

    public string TargetPlaceholder =>
        KindText(Strings.TargetPlaceholderApplication, Strings.TargetPlaceholderFile, Strings.TargetPlaceholderFolder, Strings.TargetPlaceholderLink);

    public bool CanBrowse => !IsLink;

    [ObservableProperty]
    public partial string Target { get; set; } = request.Target;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = request.DisplayName;

    [ObservableProperty]
    public partial string? TargetError { get; private set; }

    [ObservableProperty]
    public partial string? DisplayNameError { get; private set; }

    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatchingApplication))]
    public partial bool IsChoosingApplication { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatchingApplication))]
    public partial bool IsLoadingApplications { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatchingApplication))]
    public partial string? ApplicationsError { get; private set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoMatchingApplication))]
    public partial IReadOnlyList<InstalledApplication> MatchingApplications { get; private set; } = [];

    [ObservableProperty]
    public partial InstalledApplication? SelectedApplication { get; set; }

    public bool HasNoMatchingApplication =>
        IsChoosingApplication && !IsLoadingApplications && ApplicationsError is null && MatchingApplications.Count == 0;

    /// <summary>The workspace with the saved resource, or null while nothing was saved.</summary>
    public Workspace? Result { get; private set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BrowseAsync()
    {
        var path = Kind == ResourceKind.Application ? await picker.PickProgramAsync()
            : Kind == ResourceKind.Folder ? await picker.PickFolderAsync()
            : await picker.PickFileAsync();

        if (path is not null)
            Target = path;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ChooseInstalledApplicationAsync()
    {
        IsChoosingApplication = true;
        if (_installedApplications is not null || IsLoadingApplications)
            return;

        IsLoadingApplications = true;
        ApplicationsError = null;
        try
        {
            _installedApplications = await applications.ListAsync();
            ShowMatchingApplications();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The installed applications could not be listed");
            ApplicationsError = UiText.Format(Strings.ApplicationsLoadFailed, paths.LogsDirectory);
        }
        finally
        {
            IsLoadingApplications = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SubmitAsync()
    {
        // Pressing Enter twice must not save twice.
        if (_isSubmitting)
            return;

        _isSubmitting = true;
        TargetError = null;
        DisplayNameError = null;
        SaveError = null;
        try
        {
            Result = await request.Submit(Kind, DisplayName, Target);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (AtlasValidationException ex)
        {
            TargetError = ValidationText.FirstFor(ex, ValidationField.ResourceTarget);
            DisplayNameError = ValidationText.FirstFor(ex, ValidationField.ResourceDisplayName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The resource could not be saved");
            SaveError = UiText.Format(Strings.SaveFailed, paths.LogsDirectory);
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    partial void OnKindChanged(ResourceKind value)
    {
        TargetError = null;
        IsChoosingApplication = false;
    }

    partial void OnTargetChanged(string value)
    {
        if (SuggestName(Kind, value) is { } name)
            Suggest(name);
    }

    partial void OnSearchTextChanged(string value) => ShowMatchingApplications();

    partial void OnSelectedApplicationChanged(InstalledApplication? value)
    {
        if (value is null)
            return;

        Target = value.Target;
        Suggest(value.Name);
        IsChoosingApplication = false;
    }

    /// <summary>Fills in the name for the user, unless they typed one themselves.</summary>
    private void Suggest(string name)
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName == _suggestedName)
        {
            DisplayName = name;
            _suggestedName = name;
        }
    }

    private void ShowMatchingApplications()
    {
        var all = _installedApplications ?? [];
        var search = SearchText.Trim();
        MatchingApplications = search.Length == 0
            ? all
            : all.Where(a => UiText.Culture.CompareInfo.IndexOf(a.Name, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();
    }

    private void ChooseKind(bool isChosen, ResourceKind kind)
    {
        if (isChosen && CanChooseKind)
            Kind = kind;
    }

    private string KindText(string application, string file, string folder, string link)
    {
        if (Kind == ResourceKind.Application)
            return application;
        if (Kind == ResourceKind.File)
            return file;

        return Kind == ResourceKind.Folder ? folder : link;
    }

    private static string? SuggestName(ResourceKind kind, string target)
    {
        var value = target.Trim();
        if (kind == ResourceKind.Url)
            return ResourceTargets.TryGetWebAddress(value, out var address) ? address.Host : null;

        if (ResourceTargets.TryGetInstalledApplicationId(value, out _))
            return null;

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(value));
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return kind == ResourceKind.Application ? Path.GetFileNameWithoutExtension(name) : name;
    }
}
