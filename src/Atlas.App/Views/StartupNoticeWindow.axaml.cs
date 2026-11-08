using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Atlas.App.Views;

/// <summary>Tells the user why Atlas cannot continue. It runs before any service exists, so it has no view model.</summary>
public sealed partial class StartupNoticeWindow : Window
{
    public StartupNoticeWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    public StartupNoticeWindow(string title, string message)
        : this()
    {
        NoticeTitle.Text = title;
        NoticeMessage.Text = message;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
