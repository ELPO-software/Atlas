using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

/// <summary>Shows a long text to read, such as a license.</summary>
public sealed partial class TextDocumentWindow : Window
{
    public TextDocumentWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public TextDocumentWindow(string title, string text)
        : this()
    {
        Title = title;
        DocumentTitle.Text = title;
        DocumentText.Text = text;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CloseButton.Focus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
