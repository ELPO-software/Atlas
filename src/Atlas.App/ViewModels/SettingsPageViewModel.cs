using System.Reflection;
using Atlas.App.About;
using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class SettingsPageViewModel(PageServices services) : PageViewModel
{
    private const string LicenseResourceName = "Atlas.License.txt";

    private readonly ILogger _logger = services.Loggers.CreateLogger<SettingsPageViewModel>();
    private bool _isShowingStoredTheme;

    [ObservableProperty]
    public partial bool IsSystemTheme { get; set; }

    [ObservableProperty]
    public partial bool IsLightTheme { get; set; }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; }

    [ObservableProperty]
    public partial string? ThemeError { get; private set; }

    [ObservableProperty]
    public partial string? OpenFolderError { get; private set; }

    public string DataFolder => services.Paths.DataDirectory;

    public string VersionText { get; } = UiText.Format(Strings.VersionFormat, ReadVersion());

    public string LicenseText { get; } = ReadLicense();

    public IReadOnlyList<ThirdPartyComponent> Components => ThirdPartyComponents.All;

    /// <summary>The theme change being stored, if any. Lets tests wait for it.</summary>
    internal Task PendingThemeSave { get; private set; } = Task.CompletedTask;

    public override async Task LoadAsync()
    {
        ThemeError = null;
        try
        {
            ShowTheme(await services.Settings.GetThemeAsync());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The theme setting could not be read");
            ThemeError = UiText.Format(Strings.ThemeReadFailed, services.Paths.LogsDirectory);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowLicenseAsync(ThirdPartyComponent component) =>
        services.Dialogs.ShowTextAsync(
            UiText.Format(Strings.LicenseWindowTitle, component.Name),
            ThirdPartyComponents.ReadLicense(component));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenDataFolderAsync()
    {
        OpenFolderError = await services.Folders.OpenAsync(DataFolder) ? null : Strings.OpenFolderFailed;
    }

    partial void OnIsSystemThemeChanged(bool value) => OnThemeChosen(value, ThemePreference.System);

    partial void OnIsLightThemeChanged(bool value) => OnThemeChosen(value, ThemePreference.Light);

    partial void OnIsDarkThemeChanged(bool value) => OnThemeChosen(value, ThemePreference.Dark);

    private void OnThemeChosen(bool isChosen, ThemePreference theme)
    {
        if (isChosen && !_isShowingStoredTheme)
        {
            services.Theme.Apply(theme);
            PendingThemeSave = SaveThemeAsync(theme);
        }
    }

    private async Task SaveThemeAsync(ThemePreference theme)
    {
        ThemeError = null;
        try
        {
            await services.Settings.SetThemeAsync(theme);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The theme setting could not be saved");
            ThemeError = UiText.Format(Strings.ThemeSaveFailed, services.Paths.LogsDirectory);
        }
    }

    private void ShowTheme(ThemePreference theme)
    {
        _isShowingStoredTheme = true;
        IsSystemTheme = theme == ThemePreference.System;
        IsLightTheme = theme == ThemePreference.Light;
        IsDarkTheme = theme == ThemePreference.Dark;
        _isShowingStoredTheme = false;
    }

    private static string ReadVersion()
    {
        var version = typeof(SettingsPageViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        // The build appends the commit after a plus sign, which means nothing to the reader.
        var commitSeparator = version.IndexOf('+', StringComparison.Ordinal);
        return commitSeparator >= 0 ? version[..commitSeparator] : version;
    }

    private static string ReadLicense()
    {
        using var stream = typeof(SettingsPageViewModel).Assembly.GetManifestResourceStream(LicenseResourceName)
            ?? throw new InvalidOperationException("The license text is missing from the application.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
