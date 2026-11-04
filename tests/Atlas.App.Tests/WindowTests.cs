using Atlas.App.Composition;
using Atlas.App.Resources;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Workspaces;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.App.Tests;

public sealed class WindowTests
{
    [AvaloniaFact]
    public async Task MainWindow_ShowsTheCountReadFromTheRealDatabase()
    {
        using var folder = new TemporaryDataFolder();
        using var services = AppStartup.Run(folder.Paths).Services;
        Assert.NotNull(services);
        await services.GetRequiredService<WorkspaceService>().CreateAsync("Études d'été", null);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        await viewModel.LoadAsync();

        Assert.Equal("1 espace de travail", window.FindControl<TextBlock>("WorkspaceCount")?.Text);
        Assert.Equal(Strings.AppName, window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void StartupNoticeWindow_ShowsItsMessageAndClosesOnRequest()
    {
        var window = new StartupNoticeWindow("Titre", "Message à lire");
        window.Show();

        Assert.Equal("Titre", window.FindControl<TextBlock>("NoticeTitle")?.Text);
        Assert.Equal("Message à lire", window.FindControl<SelectableTextBlock>("NoticeMessage")?.Text);

        window.FindControl<Button>("CloseButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(window.IsVisible);
    }
}
