namespace Atlas.Core.Settings;

/// <summary>Stores preferences as named text values.</summary>
public interface ISettingsStore
{
    /// <returns>The stored value, or null if the setting was never saved.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}
