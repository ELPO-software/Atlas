using Atlas.App.Services;
using Atlas.Core.History;
using Atlas.Core.Restoration;
using Atlas.Core.Settings;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

/// <summary>What pages need from the rest of the application.</summary>
public sealed record PageServices(
    WorkspaceService Workspaces,
    VersionService Versions,
    IHistoryRepository History,
    SettingsService Settings,
    RestorationService Restoration,
    IDialogService Dialogs,
    IFilePicker Files,
    IThemeController Theme,
    IFolderOpener Folders,
    SuggestionCenter Suggestions,
    AtlasDataPaths Paths,
    TimeProvider Time,
    ILoggerFactory Loggers);

public abstract class PageViewModel : ViewModelBase
{
    /// <summary>True while the page holds changes that are not stored yet, or work that has not finished.</summary>
    public virtual bool HasUnsavedChanges => false;

    public abstract Task LoadAsync();

    /// <summary>Finishes the work of the page before it is replaced or the window closes.</summary>
    /// <returns>False to keep the user on this page.</returns>
    public virtual Task<bool> TryLeaveAsync() => Task.FromResult(true);
}
