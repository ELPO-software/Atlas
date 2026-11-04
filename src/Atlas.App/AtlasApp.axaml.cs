using Atlas.App.Composition;
using Atlas.App.Resources;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.App;

public sealed partial class AtlasApp : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => ReleaseResources();
            desktop.MainWindow = CreateStartupWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window CreateStartupWindow()
    {
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsFirstInstance)
            return new StartupNoticeWindow(Strings.AlreadyRunningTitle, Strings.AlreadyRunningMessage);

        var outcome = AppStartup.RunForCurrentUser();
        if (outcome.Services is null)
            return new StartupNoticeWindow(Strings.StartupFailedTitle, outcome.FailureMessage!);

        _services = outcome.Services;
        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        _ = viewModel.LoadAsync();
        return new MainWindow { DataContext = viewModel };
    }

    private void ReleaseResources()
    {
        _services?.Dispose();
        _instanceGuard?.Dispose();
    }
}
