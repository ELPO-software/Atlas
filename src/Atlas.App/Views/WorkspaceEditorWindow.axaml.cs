using Atlas.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

public sealed partial class WorkspaceEditorWindow : Window
{
    public WorkspaceEditorWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public WorkspaceEditorWindow(WorkspaceEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.Completed += (_, _) => Close(viewModel.Result);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
