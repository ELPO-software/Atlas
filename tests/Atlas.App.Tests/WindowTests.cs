using Atlas.App.Composition;
using Atlas.App.Controls;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.App.Views;
using Atlas.Core.Detection;
using Atlas.Core.Notifications;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
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
    public async Task Suggestions_AskFirstThenShowWhatAtlasFound()
    {
        await using var app = await RunningApp.StartAsync();
        const string Root = @"C:\Users\Léa\source\Atlas";
        app.Observer.Next = new WorkObservation(
            [new ObservedApplication("Visual Studio Code", @"C:\Programmes\Code.exe", [new ObservedItem(Root, true)])],
            [],
            [new ObservedProject(Root, "Atlas", null, null)]);

        app.Click("NavSuggestions");
        await app.SettleAsync();
        Assert.True(app.Find<Border>("DetectionInvite").IsVisible);
        Assert.Equal(0, app.Observer.Observations);

        app.Click("EnableDetection");
        await app.WaitUntilAsync(() => app.ViewModel.Suggestions.Count == 1);
        await app.SettleAsync();

        Assert.False(app.Find<Border>("DetectionInvite").IsVisible);
        var texts = app.Find<ItemsControl>("SuggestionCards").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Atlas", texts);
        Assert.Contains(Strings.SuggestionDetected, texts);
        Assert.Contains("Visual Studio Code", texts);
        Assert.True(app.Find<Border>("SuggestionBadge").IsVisible);
        Assert.Empty(app.Notifications.Shown);
    }

    [AvaloniaFact]
    public async Task Settings_ShowTheDetectionSwitches()
    {
        await using var app = await RunningApp.StartAsync();

        app.Click("NavSettings");
        await app.SettleAsync();

        Assert.False(app.Find<ToggleSwitch>("DetectionToggle").IsChecked);
        Assert.True(app.Find<ToggleSwitch>("NotificationsToggle").IsChecked);
        Assert.False(app.Find<TextBlock>("NotificationsUnavailable").IsVisible);
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

    [AvaloniaFact]
    public async Task Restore_FromTheViewOpensTheResourcesAndShowsTheReport()
    {
        await using var app = await RunningApp.StartAsync(async services =>
        {
            var workspaces = services.GetRequiredService<WorkspaceService>();
            var workspace = await workspaces.CreateAsync("Études d'été", null);
            await workspaces.AddResourceAsync(workspace.Id, ResourceKind.Url, "Bibliothèque", "https://example.org");
            await workspaces.AddResourceAsync(workspace.Id, ResourceKind.File, "Absent", @"C:\Données d'été\absent.txt");
        });
        app.Opener.Answers[@"C:\Données d'été\absent.txt"] = OpenResult.NotFound();
        await app.ViewModel.OpenWorkspaceAsync((await app.Workspaces.ListAsync()).Single().Id);
        await app.SettleAsync();
        var restore = app.Find<Button>("RestoreWorkspace");
        Assert.True(restore.IsEffectivelyEnabled);

        restore.Command!.Execute(null);
        await app.WaitUntilAsync(() => app.Find<Border>("RestoreReport").IsVisible);

        Assert.Equal("1 restaurée, 1 introuvable", app.Find<TextBlock>("ReportSummary").Text);
        var texts = app.Find<ItemsControl>("ReportLines").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Introuvable", texts);
        Assert.Contains("Ouverte", texts);
        Assert.Equal([@"C:\Données d'été\absent.txt", "https://example.org"], app.Opener.Opened);
    }

    [AvaloniaFact]
    public async Task Restore_IsDisabledInTheViewWithoutResources()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Vide", null));
        await app.ViewModel.OpenWorkspaceAsync((await app.Workspaces.ListAsync()).Single().Id);
        await app.SettleAsync();

        Assert.False(app.Find<Button>("RestoreWorkspace").IsEffectivelyEnabled);
        Assert.True(app.Find<TextBlock>("NoResources").IsVisible);
    }

    [AvaloniaFact]
    public async Task ResourceEditor_AddsALinkTypedInTheForm()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études", null));
        var id = (await app.Workspaces.ListAsync()).Single().Id;
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.EditResourceAsync(new ResourceEditorRequest(
            Strings.AddResourceDialogTitle,
            Strings.Add,
            FixedKind: null,
            string.Empty,
            string.Empty,
            (kind, name, target) => app.Workspaces.AddResourceAsync(id, kind, name, target)));
        var dialog = await app.WaitForDialogAsync<ResourceEditorWindow>();
        Assert.True(dialog.FindControl<RadioButton>("KindApplication")!.IsEffectivelyVisible);
        Assert.True(dialog.FindControl<TextBox>("TargetBox")!.IsFocused);

        dialog.FindControl<RadioButton>("KindLink")!.IsChecked = true;
        dialog.FindControl<TextBox>("TargetBox")!.Text = "https://example.org/été";
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var saved = await answer;

        var resource = Assert.Single(saved!.Resources);
        Assert.Equal((ResourceKind.Url, "example.org"), (resource.Kind, resource.DisplayName));
        Assert.Single((await app.Workspaces.GetAsync(id)).Resources);
    }

    [AvaloniaFact]
    public async Task ResourceEditor_ShowsWhyAFileThatRunsCodeIsRefused()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études", null));
        var id = (await app.Workspaces.ListAsync()).Single().Id;
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.EditResourceAsync(new ResourceEditorRequest(
            Strings.EditResourceDialogTitle,
            Strings.Save,
            ResourceKind.File,
            "Script",
            string.Empty,
            (kind, name, target) => app.Workspaces.AddResourceAsync(id, kind, name, target)));
        var dialog = await app.WaitForDialogAsync<ResourceEditorWindow>();
        Assert.False(dialog.FindControl<RadioButton>("KindApplication")!.IsEffectivelyVisible);
        Assert.False(dialog.FindControl<Button>("ChooseInstalledApplication")!.IsEffectivelyVisible);

        dialog.FindControl<TextBox>("TargetBox")!.Text = @"C:\Outils\lancer.bat";
        dialog.FindControl<Button>("ConfirmButton")!.Command!.Execute(null);
        await app.SettleAsync();

        var error = dialog.FindControl<TextBlock>("TargetError")!;
        Assert.True(error.IsVisible);
        Assert.Equal(Strings.TargetExecutableFile, error.Text);
        dialog.Close();
        Assert.Null(await answer);
        Assert.Empty((await app.Workspaces.GetAsync(id)).Resources);
    }

    [AvaloniaFact]
    public async Task Confirmation_ListsWhatItIsAboutAndUsesTheMainActionStyleWhenNothingIsDestroyed()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync(new ConfirmationRequest("Lancer 2 applications ?", "Message", "Lancer et ouvrir", "Annuler")
        {
            Details = [new ConfirmationDetail("Éditeur", @"C:\Programmes\Éditeur.exe"), new ConfirmationDetail("Calculatrice", "Application installée")],
            IsDestructive = false,
        });
        var dialog = await app.WaitForDialogAsync<ConfirmationWindow>();

        var texts = dialog.FindControl<ItemsControl>("ConfirmationDetails")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(["Éditeur", @"C:\Programmes\Éditeur.exe", "Calculatrice", "Application installée"], texts);
        var confirm = dialog.FindControl<Button>("ConfirmButton")!;
        Assert.Contains("primary", confirm.Classes);
        Assert.DoesNotContain("danger", confirm.Classes);
        Assert.True(dialog.FindControl<Button>("CancelButton")!.IsFocused);

        dialog.Close();
        Assert.False(await answer);
    }

    [AvaloniaFact]
    public async Task History_ShowsWhatWasDoneIncludingDeletedWorkspaces()
    {
        await using var app = await RunningApp.StartAsync(async services =>
        {
            var workspaces = services.GetRequiredService<WorkspaceService>();
            var workspace = await workspaces.CreateAsync("Études d'été", null);
            await workspaces.DeleteAsync(workspace.Id);
        });

        app.Click("NavHistory");
        await app.SettleAsync();

        Assert.Contains("selected", app.Find<Button>("NavHistory").Classes);
        var texts = app.Find<ItemsControl>("HistoryItems").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Espace «\u00A0Études d'été\u00A0» supprimé", texts);
        Assert.Contains("Espace «\u00A0Études d'été\u00A0» créé", texts);
    }

    [AvaloniaFact]
    public async Task Versions_SavedFromTheViewAppearInIt()
    {
        await using var app = await RunningApp.StartAsync(services =>
            services.GetRequiredService<WorkspaceService>().CreateAsync("Études", null));
        var id = (await app.Workspaces.ListAsync()).Single().Id;
        await app.ViewModel.OpenWorkspaceAsync(id);
        await app.SettleAsync();
        Assert.True(app.Find<TextBlock>("NoVersions").IsVisible);

        app.Click("SaveVersion");
        var dialog = await app.WaitForDialogAsync<WorkspaceEditorWindow>();
        dialog.FindControl<TextBox>("NameBox")!.Text = "Avant les révisions";
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.WaitUntilAsync(() => !app.Find<TextBlock>("NoVersions").IsVisible);

        var texts = app.Find<ItemsControl>("Versions").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Avant les révisions", texts);
    }

    [AvaloniaFact]
    public async Task ImportPreview_ShowsAProblemWithoutAnImportButton()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmImportAsync(new ImportPreviewViewModel(
            "partagé.json", null, new Atlas.Core.Exchange.DocumentError(Atlas.Core.Exchange.DocumentProblem.NotAnAtlasFile)));
        var dialog = await app.WaitForDialogAsync<ImportPreviewWindow>();

        Assert.Equal(Strings.FileProblemNotAnAtlasFile, dialog.FindControl<SelectableTextBlock>("Problem")!.Text);
        Assert.False(dialog.FindControl<Button>("ImportButton")!.IsEffectivelyVisible);
        Assert.True(dialog.FindControl<Button>("CancelButton")!.IsFocused);

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(await answer);
    }

    [AvaloniaFact]
    public async Task ImportPreview_ListsTheResourcesAndImportsOnRequest()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();
        var plan = Atlas.Core.Exchange.ImportPlan.From(new Atlas.Core.Exchange.DocumentContent(
            "Espace partagé",
            string.Empty,
            string.Empty,
            [new Atlas.Core.Exchange.DocumentResource("application", "Outil", @"C:\Outils\outil.exe", false)]));

        var answer = dialogs.ConfirmImportAsync(new ImportPreviewViewModel("partagé.json", plan, null));
        var dialog = await app.WaitForDialogAsync<ImportPreviewWindow>();

        var texts = dialog.FindControl<ItemsControl>("Items")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Outil", texts);
        Assert.Contains(Strings.ImportNoteNeedsApproval, texts);
        dialog.FindControl<Button>("ImportButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await answer);
    }

    /// <summary>The whole life of a workspace, through the real services and database. Nothing opens for real.</summary>
    [AvaloniaFact]
    public async Task EndToEnd_CreateRestoreSaveExportImportAndDelete()
    {
        await using var app = await RunningApp.StartAsync();
        var history = app.Services.GetRequiredService<Atlas.Core.History.IHistoryRepository>();
        var exportPath = Path.Combine(app.Folder.Paths.DataDirectory, "Études d'été.json");

        // Create from the home page.
        var field = app.Find<TextBox>("NewWorkspaceName");
        field.Focus();
        field.Text = "Études d'été";
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        var page = Assert.IsType<WorkspaceDetailViewModel>(app.ViewModel.CurrentPage);
        var id = (await app.Workspaces.ListAsync()).Single().Id;

        // Add resources and notes.
        await app.Workspaces.AddResourceAsync(id, ResourceKind.Application, "Éditeur", @"C:\Programmes\Éditeur d'été\Éditeur.exe");
        await app.Workspaces.AddResourceAsync(id, ResourceKind.Url, "Bibliothèque", "https://example.org");
        await app.ViewModel.OpenWorkspaceAsync(id);
        await app.SettleAsync();
        page = Assert.IsType<WorkspaceDetailViewModel>(app.ViewModel.CurrentPage);
        page.Notes = "Relire le chapitre 3";
        await app.WaitUntilAsync(() => !page.HasUnsavedChanges);

        // Restore: one program, so no confirmation is needed.
        await page.RestoreCommand.ExecuteAsync(null);
        Assert.Equal([@"C:\Programmes\Éditeur d'été\Éditeur.exe", "https://example.org"], app.Opener.Opened);
        Assert.Equal("2 restaurées", page.Report?.Summary);

        // Save a version, then export the workspace.
        await app.Services.GetRequiredService<Atlas.Core.Versions.VersionService>().SaveAsync(id, "Avant les révisions");
        await File.WriteAllTextAsync(exportPath, Atlas.Core.Exchange.WorkspaceDocument.Write(await app.Workspaces.GetAsync(id), DateTimeOffset.UtcNow));

        // Import it back: a new workspace, with the program waiting for approval.
        var read = Atlas.Core.Exchange.WorkspaceDocument.Read(await File.ReadAllTextAsync(exportPath));
        var imported = await app.Workspaces.ImportAsync(Atlas.Core.Exchange.ImportPlan.From(read.Content!), "Études d'été.json");
        Assert.NotEqual(id, imported.Id);
        Assert.True(imported.Resources.Single(r => r.Kind == ResourceKind.Application).RequiresReview);
        Assert.Equal("Relire le chapitre 3", imported.Notes);

        // Delete the original: its history stays.
        await app.Workspaces.DeleteAsync(id);
        Assert.Single(await app.Workspaces.ListAsync());

        var actions = (await history.ListAsync(50)).Select(e => e.Action).ToList();
        Assert.Contains(Atlas.Core.History.HistoryAction.WorkspaceCreated, actions);
        Assert.Contains(Atlas.Core.History.HistoryAction.WorkspaceRestored, actions);
        Assert.Contains(Atlas.Core.History.HistoryAction.VersionSaved, actions);
        Assert.Contains(Atlas.Core.History.HistoryAction.WorkspaceImported, actions);
        Assert.Equal(Atlas.Core.History.HistoryAction.WorkspaceDeleted, actions[0]);
    }

    [AvaloniaFact]
    public async Task TextDocument_ShowsALicenseAndClosesWithEscape()
    {
        await using var app = await RunningApp.StartAsync();
        var dialogs = app.Services.GetRequiredService<IDialogService>();

        var shown = dialogs.ShowTextAsync("Licence de Serilog", "Apache License\nVersion 2.0, January 2004");
        var window = await app.WaitForDialogAsync<TextDocumentWindow>();

        Assert.Equal("Licence de Serilog", window.Title);
        Assert.Equal("Apache License\nVersion 2.0, January 2004", window.FindControl<SelectableTextBlock>("DocumentText")!.Text);
        Assert.True(window.FindControl<Button>("CloseButton")!.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await shown;
        Assert.False(window.IsVisible);
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

        public FakeResourceOpener Opener { get; private init; } = new();

        public FakeApplicationCatalog Catalog { get; private init; } = new();

        public FakeWorkObserver Observer { get; private init; } = new();

        public FakeDesktopNotifications Notifications { get; private init; } = new();

        public static async Task<RunningApp> StartAsync(Func<ServiceProvider, Task>? seed = null)
        {
            var folder = new TemporaryDataFolder();
            var opener = new FakeResourceOpener();
            var catalog = new FakeApplicationCatalog();
            var observer = new FakeWorkObserver();
            var notifications = new FakeDesktopNotifications();

            // Nothing a test does may open a file, a folder, a link or a program on this computer, read its windows,
            // or show a Windows notification.
            var services = AppStartup.Run(folder.Paths, replace =>
            {
                replace.AddSingleton<IResourceOpener>(opener);
                replace.AddSingleton<IApplicationCatalog>(catalog);
                replace.AddSingleton<IWorkObserver>(observer);
                replace.AddSingleton<IDesktopNotifications>(notifications);
            }).Services ?? throw new InvalidOperationException("Atlas did not start.");

            if (seed is not null)
                await seed(services);

            var viewModel = services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = viewModel };
            services.GetRequiredService<MainWindowHost>().Window = window;
            window.Show();

            var app = new RunningApp(folder, services, viewModel, window) { Opener = opener, Catalog = catalog, Observer = observer, Notifications = notifications };
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
