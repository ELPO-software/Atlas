using Atlas.App.ViewModels;
using Atlas.Core.Settings;

namespace Atlas.App.Tests;

public sealed class SettingsPageViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly TestServices _services = new();
    private readonly SettingsPageViewModel _page;

    public SettingsPageViewModelTests() => _page = new SettingsPageViewModel(_services.Pages);

    [Fact]
    public async Task LoadAsync_SelectsTheSystemThemeByDefault()
    {
        await _page.LoadAsync();

        Assert.True(_page.IsSystemTheme);
        Assert.False(_page.IsLightTheme);
        Assert.False(_page.IsDarkTheme);
    }

    [Fact]
    public async Task LoadAsync_SelectsTheStoredThemeWithoutApplyingOrSavingAnything()
    {
        await _services.Pages.Settings.SetThemeAsync(ThemePreference.Dark, Token);
        var writesBefore = _services.SettingsStore.WriteCount;

        await _page.LoadAsync();

        Assert.True(_page.IsDarkTheme);
        Assert.Empty(_services.Theme.Applied);
        Assert.Equal(writesBefore, _services.SettingsStore.WriteCount);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheThemeCannotBeRead()
    {
        _services.SettingsStore.Failure = new IOException("Disque indisponible");

        await _page.LoadAsync();

        Assert.Contains(TestServices.LogsMarker, _page.ThemeError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.System)]
    public async Task ChoosingATheme_AppliesAndStoresIt(ThemePreference theme)
    {
        await _services.Pages.Settings.SetThemeAsync(theme == ThemePreference.System ? ThemePreference.Dark : ThemePreference.System, Token);
        await _page.LoadAsync();

        switch (theme)
        {
            case ThemePreference.Light:
                _page.IsLightTheme = true;
                break;
            case ThemePreference.Dark:
                _page.IsDarkTheme = true;
                break;
            default:
                _page.IsSystemTheme = true;
                break;
        }

        await _page.PendingThemeSave;

        Assert.Equal([theme], _services.Theme.Applied);
        Assert.Equal(theme, await _services.Pages.Settings.GetThemeAsync(Token));
    }

    [Fact]
    public async Task ChoosingATheme_StaysAppliedWhenItCannotBeSavedAndSaysSo()
    {
        await _page.LoadAsync();
        _services.SettingsStore.Failure = new IOException("Disque plein");

        _page.IsDarkTheme = true;
        await _page.PendingThemeSave;

        Assert.Equal([ThemePreference.Dark], _services.Theme.Applied);
        Assert.Contains("prochain démarrage", _page.ThemeError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChoosingATheme_ClearsThePreviousError()
    {
        await _page.LoadAsync();
        _services.SettingsStore.Failure = new IOException("Disque plein");
        _page.IsDarkTheme = true;
        await _page.PendingThemeSave;
        _services.SettingsStore.Failure = null;

        _page.IsLightTheme = true;
        await _page.PendingThemeSave;

        Assert.Null(_page.ThemeError);
    }

    [Fact]
    public void UncheckingATheme_DoesNothing()
    {
        _page.IsDarkTheme = true;
        _services.Theme.Applied.Clear();

        _page.IsDarkTheme = false;

        Assert.Empty(_services.Theme.Applied);
    }

    [Fact]
    public async Task OpenDataFolder_OpensTheRealDataLocation()
    {
        await _page.OpenDataFolderCommand.ExecuteAsync(null);

        Assert.Equal([_services.Paths.DataDirectory], _services.Folders.Opened);
        Assert.Null(_page.OpenFolderError);
    }

    [Fact]
    public async Task OpenDataFolder_SaysSoWhenTheSystemCannotOpenIt()
    {
        _services.Folders.Succeeds = false;

        await _page.OpenDataFolderCommand.ExecuteAsync(null);

        Assert.Equal("Impossible d'ouvrir ce dossier. Son emplacement est indiqué ci-dessus.", _page.OpenFolderError);
    }

    [Fact]
    public async Task OpenDataFolder_ClearsTheErrorWhenItWorksAgain()
    {
        _services.Folders.Succeeds = false;
        await _page.OpenDataFolderCommand.ExecuteAsync(null);
        _services.Folders.Succeeds = true;

        await _page.OpenDataFolderCommand.ExecuteAsync(null);

        Assert.Null(_page.OpenFolderError);
    }

    [Fact]
    public void DataFolder_ShowsTheActualLocation() =>
        Assert.Equal(_services.Paths.DataDirectory, _page.DataFolder);

    [Fact]
    public void Version_IsTheProductVersionWithoutTheBuildSuffix()
    {
        Assert.StartsWith("Version 0.1.0-alpha.1", _page.VersionText, StringComparison.Ordinal);
        Assert.DoesNotContain("+", _page.VersionText, StringComparison.Ordinal);
    }

    [Fact]
    public void License_IsTheFullTextOfTheRepositoryLicense()
    {
        Assert.StartsWith("MIT License", _page.LicenseText, StringComparison.Ordinal);
        Assert.Contains("ELPO software", _page.LicenseText, StringComparison.Ordinal);
    }
}
