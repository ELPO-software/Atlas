namespace Atlas.Core.Settings;

public sealed class SettingsService(ISettingsStore store)
{
    private const string ThemeKey = "theme";
    private const string LightValue = "light";
    private const string DarkValue = "dark";
    private const string SystemValue = "system";
    private const string DetectionKey = "detection";
    private const string DetectionNotificationsKey = "detection-notifications";
    private const string OnValue = "on";
    private const string OffValue = "off";

    /// <summary>A value Atlas does not recognize, such as one from a newer version, means following the system.</summary>
    public async Task<ThemePreference> GetThemeAsync(CancellationToken cancellationToken = default) =>
        await store.GetAsync(ThemeKey, cancellationToken) switch
        {
            LightValue => ThemePreference.Light,
            DarkValue => ThemePreference.Dark,
            _ => ThemePreference.System,
        };

    /// <summary>
    /// Whether Atlas may observe the session to propose environments. Null until the user decides: Atlas asks before
    /// it observes anything.
    /// </summary>
    public async Task<bool?> GetDetectionEnabledAsync(CancellationToken cancellationToken = default) =>
        await store.GetAsync(DetectionKey, cancellationToken) switch
        {
            OnValue => true,
            OffValue => false,
            _ => null,
        };

    public Task SetDetectionEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        store.SetAsync(DetectionKey, enabled ? OnValue : OffValue, cancellationToken);

    /// <summary>Whether proposals may appear as Windows notifications. They always appear in Atlas itself.</summary>
    public async Task<bool> GetDetectionNotificationsAsync(CancellationToken cancellationToken = default) =>
        await store.GetAsync(DetectionNotificationsKey, cancellationToken) != OffValue;

    public Task SetDetectionNotificationsAsync(bool enabled, CancellationToken cancellationToken = default) =>
        store.SetAsync(DetectionNotificationsKey, enabled ? OnValue : OffValue, cancellationToken);

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
