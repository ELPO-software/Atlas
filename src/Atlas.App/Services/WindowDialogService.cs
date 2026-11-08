using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Services;

internal sealed class WindowDialogService(
    MainWindowHost host,
    AtlasDataPaths paths,
    ILogger<WorkspaceEditorViewModel> editorLogger) : IDialogService
{
    private Window Owner => host.Window ?? throw new InvalidOperationException("A dialog was requested before the main window exists.");

    public Task<bool> ConfirmAsync(ConfirmationRequest request) =>
        new ConfirmationWindow(request).ShowDialog<bool>(Owner);

    public Task<Workspace?> EditWorkspaceAsync(WorkspaceEditorRequest request) =>
        new WorkspaceEditorWindow(new WorkspaceEditorViewModel(request, paths, editorLogger)).ShowDialog<Workspace?>(Owner);
}
