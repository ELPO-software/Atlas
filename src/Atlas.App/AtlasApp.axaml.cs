using Atlas.App.Composition;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Settings;
using Atlas.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.App;

public sealed partial class AtlasApp : Application
{
    private ServiceProvider? _services;

    /// <summary>Set by <see cref="Program"/>, which owns it for the whole life of the process.</summary>
    internal static SingleInstanceGuard? InstanceGuard { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => ReleaseResources();
            desktop.MainWindow = CreateStartupWindow();

            // Another start of Atlas brings this window forward, whichever it is.
            if (InstanceGuard is { IsFirstInstance: true } guard)
                guard.ListenForActivation(() => Dispatcher.UIThread.Post(() => WindowActivation.BringToFront(desktop.MainWindow)));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window CreateStartupWindow()
    {
        // Only reached when the running Atlas could not be asked to come forward.
        if (InstanceGuard is { IsFirstInstance: false })
            return new StartupNoticeWindow(Strings.AlreadyRunningTitle, Strings.AlreadyRunningMessage);

        var outcome = AppStartup.RunForCurrentUser();
        if (outcome.Services is null)
            return new StartupNoticeWindow(Strings.StartupFailedTitle, outcome.FailureMessage!);

        _services = outcome.Services;
        var logger = _services.GetRequiredService<ILoggerFactory>().CreateLogger("Atlas");
        ApplySavedTheme(_services, logger);

        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = viewModel };
        _services.GetRequiredService<MainWindowHost>().Window = window;
        WatchForUnexpectedErrors(viewModel, logger);

        _ = viewModel.InitializeAsync();
        return window;
    }

    /// <summary>Runs before the window exists, so that it opens in the right theme instead of changing after a moment.</summary>
    private static void ApplySavedTheme(ServiceProvider services, ILogger logger)
    {
        try
        {
            // Waiting on the interface thread for work that resumes on it would freeze the application,
            // so the read runs entirely on the thread pool.
            var settings = services.GetRequiredService<SettingsService>();
            var theme = Task.Run(() => settings.GetThemeAsync()).GetAwaiter().GetResult();
            services.GetRequiredService<IThemeController>().Apply(theme);
        }
        catch (Exception ex)
        {
            // The window still opens, following the system theme. The settings page reports the problem.
            logger.LogError(ex, "The saved theme could not be read at startup");
        }
    }

    private static void WatchForUnexpectedErrors(MainWindowViewModel viewModel, ILogger logger)
    {
        // Pages handle the failures they expect. This is for the others: writes are transactional, so the
        // stored data stays consistent and the user can keep working after being told.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "An unexpected error reached the interface");
            viewModel.ShowUnexpectedError();
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "A background operation failed unobserved");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Atlas is stopping after an unexpected error");
    }

    private void ReleaseResources() => _services?.Dispose();
}
