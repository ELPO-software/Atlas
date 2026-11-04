using System.Globalization;
using Atlas.App.Resources;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class MainWindowViewModel(
    WorkspaceService workspaces,
    AtlasDataPaths paths,
    ILogger<MainWindowViewModel> logger) : ViewModelBase
{
    [ObservableProperty]
    public partial string WorkspaceCountText { get; private set; } = Strings.Loading;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await workspaces.CountAsync(cancellationToken);
            var format = count <= 1 ? Strings.WorkspaceCountOne : Strings.WorkspaceCountMany;
            WorkspaceCountText = string.Format(CultureInfo.CurrentCulture, format, count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The workspace count could not be read");
            WorkspaceCountText = string.Format(CultureInfo.CurrentCulture, Strings.WorkspaceCountFailed, paths.LogsDirectory);
        }
    }
}
