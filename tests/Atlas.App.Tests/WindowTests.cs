using Atlas.App.Composition;
using Atlas.App.Controls;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Resources;
using Atlas.Core.Settings;
using Atlas.Core.Workspaces;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.App.Tests;

/// <summary>Runs the real views on the real services and database, without showing anything on screen.</summary>
public sealed class WindowTests
{
    [AvaloniaFact]
    public async Task MainWindow_ShowsTheCountReadFromTheRealDatabase()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études d'été", null));

        Assert.Equal(Strings.AppName, app.Window.Title);
        Assert.Equal("1 espace de travail", app.Find<TextBlock>("WorkspaceCount").Text);
    }

    [AvaloniaFact]
    public async Task EveryPage_HasAView()
    {
        await using var app = await RunningApp.StartAsync();
        Assert.NotEmpty(app.Window.GetVisualDescendants().OfType<HomePageView>());

        app.Click("NavWorkspaces");
        await app.SettleAsync();
        Assert.NotEmpty(app.Window.GetVisualDescendants().OfType<WorkspacesPageView>());

        app.Click("NavSettings");
        await app.SettleAsync();
        Assert.NotEmpty(app.Window.GetVisualDescendants().OfType<SettingsPageView>());

        app.Click("NavHome");
        await app.SettleAsync();
        Assert.NotEmpty(app.Window.GetVisualDescendants().OfType<HomePageView>());
    }

    [AvaloniaFact]
    public async Task Navigation_MarksTheSelectedSection()
    {
        await using var app = await RunningApp.StartAsync();
        Assert.Contains("selected", app.Find<Button>("NavHome").Classes);

        app.Click("NavSettings");
        await app.SettleAsync();

        Assert.Contains("selected", app.Find<Button>("NavSettings").Classes);
        Assert.DoesNotContain("selected", app.Find<Button>("NavHome").Classes);
    }

    [AvaloniaFact]
    public async Task QuickCreate_WithTheEnterKeyCreatesAndOpensTheWorkspace()
    {
        await using var app = await RunningApp.StartAsync();
        var field = app.Find<TextBox>("NewWorkspaceName");
        field.Focus();
        field.Text = "Données d'été";

        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();

        Assert.Equal("Données d'été", app.Find<SelectableTextBlock>("WorkspaceName").Text);
        Assert.Equal(1, await app.Workspaces.CountAsync());
    }

    [AvaloniaFact]
    public async Task QuickCreate_ShowsWhyANameIsRefused()
    {
        await using var app = await RunningApp.StartAsync();

        app.Click("QuickCreate");
        await app.SettleAsync();

        var error = app.Find<TextBlock>("NewWorkspaceError");
        Assert.True(error.IsVisible);
        Assert.Equal(Strings.NameRequired, error.Text);
    }

    [AvaloniaFact]
    public async Task Card_OpensTheWorkspaceAndShowsItsResourcesByCategory()
    {
        await using var app = await RunningApp.StartAsync(async services =>
        {
            var repository = services.GetRequiredService<IWorkspaceRepository>();
            var workspace = Workspace.Create("Études d'été", "Mémoire", TimeProvider.System.GetUtcNow());
            workspace.AddResource(ResourceKind.Folder, "Sources", @"C:\Données d'été", TimeProvider.System.GetUtcNow());
            await repository.AddAsync(workspace);
        });

        app.Click("NavWorkspaces");
        await app.SettleAsync();
        var cards = app.Find<ItemsControl>("Cards");
        var card = cards.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal("Ouvrir Études d'été", AutomationName(card));

        card.Command!.Execute(null);
        await app.SettleAsync();

        Assert.Equal("Études d'été", app.Find<SelectableTextBlock>("WorkspaceName").Text);
        Assert.False(app.Find<TextBlock>("NoResources").IsVisible);
        var texts = app.Find<ItemsControl>("ResourceGroups").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Dossiers", texts);
        Assert.Contains("Sources", texts);
        Assert.Contains(@"C:\Données d'été", texts);
    }

    [AvaloniaFact]
    public async Task Notes_TypedInTheViewReachTheDatabase()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études", null));
        var id = (await app.Workspaces.ListAsync()).Single().Id;
        await app.ViewModel.OpenWorkspaceAsync(id);
        await app.SettleAsync();

        app.Find<TextBox>("Notes").Text = "Écrit dans la vraie vue";
        Assert.True(app.ViewModel.HasUnsavedChanges);
        await app.ViewModel.OpenHomeAsync();

        Assert.Equal("Écrit dans la vraie vue", (await app.Workspaces.GetAsync(id)).Notes);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_SavesPendingNotesFirst()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études", null));
        var id = (await app.Workspaces.ListAsync()).Single().Id;
        await app.ViewModel.OpenWorkspaceAsync(id);
        await app.SettleAsync();
        app.Find<TextBox>("Notes").Text = "Écrit juste avant de fermer";
        var closed = false;
        app.Window.Closed += (_, _) => closed = true;

        app.Window.Close();
        await app.WaitUntilAsync(() => closed);

        Assert.Equal("Écrit juste avant de fermer", (await app.Workspaces.GetAsync(id)).Notes);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindow_WithNothingPendingClosesAtOnce()
    {
        await using var app = await RunningApp.StartAsync();
        var closed = false;
        app.Window.Closed += (_, _) => closed = true;

        app.Window.Close();

        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task Settings_ShowTheThemeTheDataFolderTheVersionAndTheLicenses()
    {
        await using var app = await RunningApp.StartAsync();

        app.Click("NavSettings");
        await app.SettleAsync();

        Assert.True(app.Find<RadioButton>("ThemeSystem").IsChecked);
        Assert.Equal(app.Folder.Paths.DataDirectory, app.Find<SelectableTextBlock>("DataFolder").Text);
        Assert.StartsWith("Version ", app.Find<TextBlock>("Version").Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Settings_ChoosingAThemeChangesTheApplicationAndIsStored()
    {
        await using var app = await RunningApp.StartAsync();
        var application = Application.Current!;
        try
        {
            app.Click("NavSettings");
            await app.SettleAsync();

            app.Find<RadioButton>("ThemeDark").IsChecked = true;
            await app.SettleAsync();

            Assert.Equal("Dark", application.ActualThemeVariant.Key.ToString());
            Assert.False(app.Find<RadioButton>("ThemeSystem").IsChecked);
            Assert.Equal(ThemePreference.Dark, await app.Services.GetRequiredService<SettingsService>().GetThemeAsync());
        }
        finally
        {
            application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task Confirmation_StartsOnTheSafeAnswerAndCancelsWithEscape()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync(new ConfirmationRequest("Supprimer ?", "Message", "Supprimer", "Annuler"));
        var dialog = await app.WaitForDialogAsync<ConfirmationWindow>();

        Assert.Equal("Message", dialog.FindControl<TextBlock>("ConfirmationMessage")?.Text);
        Assert.True(dialog.FindControl<Button>("CancelButton")!.IsFocused);

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(await answer);
    }

    [AvaloniaFact]
    public async Task Confirmation_AnswersYesOnlyOnTheConfirmButton()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync(new ConfirmationRequest("Supprimer ?", "Message", "Supprimer", "Annuler"));
        var dialog = await app.WaitForDialogAsync<ConfirmationWindow>();
        dialog.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await answer);
    }

    [AvaloniaFact]
    public async Task Confirmation_ClosedWithoutAnswerMeansNo()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync(new ConfirmationRequest("Supprimer ?", "Message", "Supprimer", "Annuler"));
        var dialog = await app.WaitForDialogAsync<ConfirmationWindow>();
        dialog.Close();

        Assert.False(await answer);
    }

    [AvaloniaFact]
    public async Task Editor_StartsOnTheNameSavesWithEnterAndClosesItself()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.EditWorkspaceAsync(CreateRequest(app));
        var dialog = await app.WaitForDialogAsync<WorkspaceEditorWindow>();
        var name = dialog.FindControl<TextBox>("NameBox")!;
        Assert.True(name.IsFocused);
        name.Text = "Études d'été";

        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var saved = await answer;

        Assert.Equal("Études d'été", saved?.Name);
        Assert.False(dialog.IsVisible);
        Assert.Equal(1, await app.Workspaces.CountAsync());
    }

    [AvaloniaFact]
    public async Task Editor_StaysOpenWithAMessageWhenTheNameIsRefused()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.EditWorkspaceAsync(CreateRequest(app));
        var dialog = await app.WaitForDialogAsync<WorkspaceEditorWindow>();
        dialog.FindControl<Button>("ConfirmButton")!.Command!.Execute(null);
        await app.SettleAsync();

        Assert.True(dialog.IsVisible);
        Assert.False(answer.IsCompleted);
        var error = dialog.FindControl<TextBlock>("NameError")!;
        Assert.True(error.IsVisible);
        Assert.Equal(Strings.NameRequired, error.Text);

        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(await answer);
        Assert.Equal(0, await app.Workspaces.CountAsync());
    }

    [AvaloniaFact]
    public async Task Editor_HidesTheDescriptionWhenItOnlyAsksForAName()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.EditWorkspaceAsync(CreateRequest(app) with { ShowDescription = false });
        var dialog = await app.WaitForDialogAsync<WorkspaceEditorWindow>();

        Assert.False(dialog.FindControl<TextBox>("DescriptionBox")!.IsEffectivelyVisible);

        dialog.Close();
        Assert.Null(await answer);
    }

    [AvaloniaTheory]
    [InlineData(280, 1)]
    [InlineData(571, 1)]
    [InlineData(572, 2)]
    [InlineData(864, 3)]
    [InlineData(100, 1)]
    public void ResponsiveGrid_FitsAsManyColumnsAsTheWidthAllows(double width, int expected) =>
        Assert.Equal(expected, new ResponsiveGrid().ColumnsFor(width));

    [AvaloniaFact]
    public void ResponsiveGrid_GivesItemsEqualWidthsAndSharedRowHeights()
    {
        var grid = new ResponsiveGrid { Width = 600 };
        grid.Children.Add(new Border { MinHeight = 100 });
        grid.Children.Add(new Border { MinHeight = 50 });
        grid.Children.Add(new Border { MinHeight = 30 });
        var window = new Window { Content = grid, Width = 600, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(294, grid.Children[0].Bounds.Width);
        Assert.Equal(294, grid.Children[1].Bounds.Width);
        Assert.Equal(100, grid.Children[1].Bounds.Height);
        Assert.Equal(306, grid.Children[1].Bounds.X);
        Assert.Equal(112, grid.Children[2].Bounds.Y);
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

    private static WorkspaceEditorRequest CreateRequest(RunningApp app) =>
        new(
            Strings.NewWorkspaceDialogTitle,
            Strings.Create,
            string.Empty,
            string.Empty,
            true,
            (name, description) => app.Workspaces.CreateAsync(name, description));

    private static string? AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    /// <summary>The application as it runs for real, in a temporary data folder.</summary>
    private sealed class RunningApp : IAsyncDisposable
    {
        private RunningApp(TemporaryDataFolder folder, ServiceProvider services, MainWindowViewModel viewModel, MainWindow window)
        {
            Folder = folder;
            Services = services;
            ViewModel = viewModel;
            Window = window;
        }

        public TemporaryDataFolder Folder { get; }

        public ServiceProvider Services { get; }

        public MainWindowViewModel ViewModel { get; }

        public MainWindow Window { get; }

        public WorkspaceService Workspaces => Services.GetRequiredService<WorkspaceService>();

        public static async Task<RunningApp> StartAsync(Func<ServiceProvider, Task>? seed = null)
        {
            var folder = new TemporaryDataFolder();
            var services = AppStartup.Run(folder.Paths).Services ?? throw new InvalidOperationException("Atlas did not start.");

            if (seed is not null)
                await seed(services);

            var viewModel = services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = viewModel };
            services.GetRequiredService<MainWindowHost>().Window = window;
            window.Show();

            var app = new RunningApp(folder, services, viewModel, window);
            await viewModel.InitializeAsync();
            await app.SettleAsync();
            return app;
        }

        public T Find<T>(string name)
            where T : Control =>
            Window.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name)
            ?? throw new InvalidOperationException($"{typeof(T).Name} '{name}' is not on screen.");

        public void Click(string buttonName) => Find<Button>(buttonName).Command!.Execute(null);

        /// <summary>Lets database work finish and the interface catch up.</summary>
        public async Task SettleAsync()
        {
            for (var i = 0; i < 5; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Dispatcher.UIThread.RunJobs();
        }

        public async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The expected state was not reached in time.");
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
        }

        public async Task<T> WaitForDialogAsync<T>()
            where T : Window
        {
            T? dialog = null;
            await WaitUntilAsync(() =>
            {
                dialog = Window.OwnedWindows.OfType<T>().FirstOrDefault();
                return dialog is not null;
            });
            Dispatcher.UIThread.RunJobs();
            return dialog!;
        }

        public async ValueTask DisposeAsync()
        {
            if (Window.IsVisible)
                Window.Close();

            await SettleAsync();
            await Services.DisposeAsync();
            Folder.Dispose();
        }
    }
}
