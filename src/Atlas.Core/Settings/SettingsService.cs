namespace Atlas.Core.Settings;

public sealed class SettingsService(ISettingsStore store)
{
    private const string ThemeKey = "theme";
    private const string LightValue = "light";
    private const string DarkValue = "dark";
    private const string SystemValue = "system";

    /// <summary>A value Atlas does not recognize, such as one from a newer version, means following the system.</summary>
    public async Task<ThemePreference> GetThemeAsync(CancellationToken cancellationToken = default) =>
        await store.GetAsync(ThemeKey, cancellationToken) switch
        {
            LightValue => ThemePreference.Light,
            DarkValue => ThemePreference.Dark,
            _ => ThemePreference.System,
        };

    public Task SetThemeAsync(ThemePreference theme, CancellationToken cancellationToken = default) =>
        store.SetAsync(
            ThemeKey,
            theme switch
            {
                ThemePreference.Light => LightValue,
                ThemePreference.Dark => DarkValue,
                ThemePreference.System => SystemValue,
                _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown theme."),
            },
            cancellationToken);
}
