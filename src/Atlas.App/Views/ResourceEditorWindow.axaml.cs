using Atlas.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

public sealed partial class ResourceEditorWindow : Window
{
    public ResourceEditorWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public ResourceEditorWindow(ResourceEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.Completed += (_, _) => Close(viewModel.Result);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TargetBox.Focus();
        TargetBox.SelectAll();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
