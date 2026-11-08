using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Resources;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Services;

internal sealed class WindowDialogService(
    MainWindowHost host,
    AtlasDataPaths paths,
    IApplicationCatalog applications,
    ILogger<WorkspaceEditorViewModel> workspaceEditorLogger,
    ILogger<ResourceEditorViewModel> resourceEditorLogger) : IDialogService
{
    private Window Owner => host.Window ?? throw new InvalidOperationException("A dialog was requested before the main window exists.");

    public Task<bool> ConfirmAsync(ConfirmationRequest request) =>
        new ConfirmationWindow(request).ShowDialog<bool>(Owner);

    public Task<Workspace?> EditWorkspaceAsync(WorkspaceEditorRequest request) =>
        new WorkspaceEditorWindow(new WorkspaceEditorViewModel(request, paths, workspaceEditorLogger)).ShowDialog<Workspace?>(Owner);

    public Task ShowTextAsync(string title, string text) =>
        new TextDocumentWindow(title, text).ShowDialog(Owner);

    public Task<bool> ConfirmImportAsync(ImportPreviewViewModel preview) =>
        new ImportPreviewWindow(preview).ShowDialog<bool>(Owner);

    public Task<Workspace?> EditResourceAsync(ResourceEditorRequest request)
    {
        // The pickers open over the form, which exists only once the view model is built.
        ResourceEditorWindow? window = null;
        var viewModel = new ResourceEditorViewModel(
            request, new StorageFilePicker(() => window), applications, paths, resourceEditorLogger);
        window = new ResourceEditorWindow(viewModel);
        return window.ShowDialog<Workspace?>(Owner);
    }
}
