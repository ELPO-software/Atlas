using Atlas.App.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

/// <summary>Asks before a destructive action. Focus starts on the safe answer, so pressing Enter by reflex loses nothing.</summary>
public sealed partial class ConfirmationWindow : Window
{
    public ConfirmationWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public ConfirmationWindow(ConfirmationRequest request)
        : this() => DataContext = request;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CancelButton.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
