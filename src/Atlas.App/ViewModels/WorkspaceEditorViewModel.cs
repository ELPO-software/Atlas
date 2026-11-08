using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class WorkspaceEditorViewModel(
    WorkspaceEditorRequest request,
    AtlasDataPaths paths,
    ILogger<WorkspaceEditorViewModel> logger) : ViewModelBase
{
    private bool _isSubmitting;

    /// <summary>Raised once the values are saved, when the form can close.</summary>
    public event EventHandler? Completed;

    public string Title => request.Title;

    public string ConfirmLabel => request.ConfirmLabel;

    public bool ShowDescription => request.ShowDescription;

    [ObservableProperty]
    public partial string Name { get; set; } = request.Name;

    [ObservableProperty]
    public partial string Description { get; set; } = request.Description;

    [ObservableProperty]
    public partial string? NameError { get; private set; }

    [ObservableProperty]
    public partial string? DescriptionError { get; private set; }

    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    /// <summary>The saved workspace, or null while nothing was saved.</summary>
    public Workspace? Result { get; private set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SubmitAsync()
    {
        // Pressing Enter twice must not save twice.
        if (_isSubmitting)
            return;

        _isSubmitting = true;
        NameError = null;
        DescriptionError = null;
        SaveError = null;
        try
        {
            Result = await request.Submit(Name, Description);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (AtlasValidationException ex)
        {
            NameError = FirstMessageFor(ex, ValidationField.WorkspaceName);
            DescriptionError = FirstMessageFor(ex, ValidationField.WorkspaceDescription);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The workspace could not be saved");
            SaveError = UiText.Format(Strings.SaveFailed, paths.LogsDirectory);
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    private static string? FirstMessageFor(AtlasValidationException exception, ValidationField field) =>
        exception.Errors.FirstOrDefault(e => e.Field == field) is { } error ? ValidationText.Describe(error) : null;
}
