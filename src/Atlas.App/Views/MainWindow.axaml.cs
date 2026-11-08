using Atlas.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Atlas.App.Views;

public sealed partial class MainWindow : Window
{
    private bool _isFinishingWork;
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.MatchTitleBarToTheme(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_canClose || DataContext is not MainWindowViewModel { HasUnsavedChanges: true } viewModel)
            return;

        // Unsaved notes must be stored first, or the user must choose to lose them.
        e.Cancel = true;
        if (!_isFinishingWork)
            _ = FinishWorkThenCloseAsync(viewModel);
    }

    private async Task FinishWorkThenCloseAsync(MainWindowViewModel viewModel)
    {
        _isFinishingWork = true;
        try
        {
            _canClose = await viewModel.TryCloseAsync();
        }
        finally
        {
            _isFinishingWork = false;
        }

        // Posted so that the window never closes from inside the close request it just cancelled.
        if (_canClose)
            Dispatcher.UIThread.Post(Close);
    }
}
