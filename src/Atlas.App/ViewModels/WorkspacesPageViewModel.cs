using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Exchange;
using Atlas.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class WorkspacesPageViewModel(PageServices services, INavigator navigator) : PageViewModel
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<WorkspacesPageViewModel>();

    [ObservableProperty]
    public partial string WorkspaceCountText { get; private set; } = Strings.Loading;

    [ObservableProperty]
    public partial IReadOnlyList<WorkspaceCardViewModel> Cards { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    [ObservableProperty]
    public partial string? ImportError { get; private set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspaces = await services.Workspaces.ListAsync();

            WorkspaceCountText = UiText.WorkspaceCount(workspaces.Count);
            IsEmpty = workspaces.Count == 0;
            Cards = workspaces.Select(w => new WorkspaceCardViewModel(w, services.Time, navigator)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspaces could not be read");
            WorkspaceCountText = string.Empty;
            LoadError = UiText.Format(Strings.WorkspacesReadFailed, services.Paths.LogsDirectory);
        }
    }

    /// <summary>
    /// Reads the chosen file and shows what it holds. Nothing is created until the user confirms, and the import
    /// always creates a new workspace.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ImportAsync()
    {
        ImportError = null;
        if (await services.Files.PickImportFileAsync() is not { } path)
            return;

        var fileName = Path.GetFileName(path);
        var file = await WorkspaceFiles.ReadForImportAsync(path);
        ImportPreviewViewModel preview;
        if (file.Text is null)
        {
            preview = new ImportPreviewViewModel(fileName, null, new DocumentError(file.Problem ?? DocumentProblem.Unreadable));
        }
        else
        {
            var read = WorkspaceDocument.Read(file.Text);
            preview = read.Content is null
                ? new ImportPreviewViewModel(fileName, null, read.Error)
                : new ImportPreviewViewModel(fileName, ImportPlan.From(read.Content), null);
        }

        if (!await services.Dialogs.ConfirmImportAsync(preview) || preview.Plan is not { CanImport: true } plan)
            return;

        try
        {
            var workspace = await services.Workspaces.ImportAsync(plan, fileName);
            await navigator.OpenWorkspaceAsync(workspace.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The file {FileName} could not be imported", fileName);
            ImportError = UiText.Format(Strings.ImportFailed, services.Paths.LogsDirectory);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CreateAsync()
    {
        var created = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.NewWorkspaceDialogTitle,
            Strings.Create,
            Name: string.Empty,
            Description: string.Empty,
            ShowDescription: true,
            (name, description) => services.Workspaces.CreateAsync(name, description)));

        if (created is not null)
            await navigator.OpenWorkspaceAsync(created.Id);
    }
}
