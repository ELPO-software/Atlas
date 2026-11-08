using Atlas.Core.Settings;
using Avalonia;
using Avalonia.Styling;

namespace Atlas.App.Services;

public interface IThemeController
{
    void Apply(ThemePreference preference);
}

internal sealed class ApplicationThemeController : IThemeController
{
    public void Apply(ThemePreference preference)
    {
        if (Application.Current is { } application)
        {
            // The default variant follows the system, including when the user changes it while Atlas is open.
            application.RequestedThemeVariant = preference switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
