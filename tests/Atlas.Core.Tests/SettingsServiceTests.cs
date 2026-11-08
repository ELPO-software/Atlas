using Atlas.Core.Settings;

namespace Atlas.Core.Tests;

public sealed class SettingsServiceTests
{
    private readonly InMemorySettingsStore _store = new();
    private readonly SettingsService _service;

    public SettingsServiceTests() => _service = new SettingsService(_store);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetThemeAsync_FollowsTheSystemWhenNothingWasSaved() =>
        Assert.Equal(ThemePreference.System, await _service.GetThemeAsync(Token));

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public async Task SetThemeAsync_IsReadBack(ThemePreference theme)
    {
        await _service.SetThemeAsync(theme, Token);

        Assert.Equal(theme, await _service.GetThemeAsync(Token));
    }

    [Fact]
    public async Task GetThemeAsync_FollowsTheSystemForAValueItDoesNotKnow()
    {
        await _store.SetAsync("theme", "contraste-élevé", Token);

        Assert.Equal(ThemePreference.System, await _service.GetThemeAsync(Token));
    }

    [Fact]
    public async Task SetThemeAsync_RefusesAValueOutsideTheList() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SetThemeAsync((ThemePreference)99, Token));

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
