using Atlas.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

/// <summary>Shows a file's content before anything is created. Focus starts on cancelling.</summary>
public sealed partial class ImportPreviewWindow : Window
{
    public ImportPreviewWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public ImportPreviewWindow(ImportPreviewViewModel preview)
        : this() => DataContext = preview;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CancelButton.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnImportClick(object? sender, RoutedEventArgs e) => Close(true);
}
