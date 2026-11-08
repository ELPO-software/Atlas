using Atlas.App.Services;
using Atlas.Core.Settings;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

namespace Atlas.App.Tests;

/// <summary>Reads the colors from the real theme, so that a palette change cannot silently break readability.</summary>
public sealed class ThemeTests
{
    private const double ReadableText = 4.5;
    private const double RecognizableControl = 3.0;

    private static readonly ThemeVariant[] Themes = [ThemeVariant.Dark, ThemeVariant.Light];

    [AvaloniaTheory]
    [InlineData("AtlasTextBrush", "AtlasBackgroundBrush")]
    [InlineData("AtlasTextBrush", "AtlasSidebarBrush")]
    [InlineData("AtlasTextBrush", "AtlasSurfaceBrush")]
    [InlineData("AtlasTextBrush", "AtlasSurfaceHoverBrush")]
    [InlineData("AtlasTextBrush", "AtlasSurfacePressedBrush")]
    [InlineData("AtlasTextSecondaryBrush", "AtlasBackgroundBrush")]
    [InlineData("AtlasTextSecondaryBrush", "AtlasSidebarBrush")]
    [InlineData("AtlasTextSecondaryBrush", "AtlasSurfaceBrush")]
    [InlineData("AtlasTextSecondaryBrush", "AtlasSurfaceHoverBrush")]
    [InlineData("AtlasErrorTextBrush", "AtlasBackgroundBrush")]
    [InlineData("AtlasErrorTextBrush", "AtlasErrorSurfaceBrush")]
    [InlineData("AtlasErrorTextBrush", "ButtonBackground")]
    [InlineData("AtlasErrorTextBrush", "ButtonBackgroundPointerOver")]
    [InlineData("AtlasErrorTextBrush", "ButtonBackgroundPressed")]
    [InlineData("AtlasOnColorBrush", "AtlasAccentBrush")]
    [InlineData("AtlasOnColorBrush", "AtlasAccentHoverBrush")]
    [InlineData("AtlasOnColorBrush", "AtlasAccentPressedBrush")]
    [InlineData("AtlasOnColorBrush", "AtlasDangerBrush")]
    [InlineData("AtlasOnColorBrush", "AtlasDangerHoverBrush")]
    [InlineData("AtlasOnColorBrush", "AtlasDangerPressedBrush")]
    public void TextStaysReadable(string text, string background)
    {
        foreach (var theme in Themes)
            AssertContrast(text, background, theme, ReadableText);
    }

    [AvaloniaTheory]
    [InlineData("TextControlBorderBrush", "AtlasBackgroundBrush")]
    [InlineData("TextControlBorderBrushPointerOver", "AtlasBackgroundBrush")]
    [InlineData("TextControlBorderBrushFocused", "AtlasBackgroundBrush")]
    [InlineData("AtlasAccentBrush", "AtlasSurfaceHoverBrush")]
    public void ControlsStayRecognizable(string foreground, string background)
    {
        foreach (var theme in Themes)
            AssertContrast(foreground, background, theme, RecognizableControl);
    }

    [AvaloniaFact]
    public void Palette_UsesTheColorsOfTheBrief()
    {
        Assert.Equal(Color.Parse("#090B10"), ColorOf("AtlasBackgroundBrush", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#FFFFFF"), ColorOf("AtlasTextBrush", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#858B98"), ColorOf("AtlasTextSecondaryBrush", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#FFFFFF"), ColorOf("AtlasBackgroundBrush", ThemeVariant.Light));
        Assert.Equal(Color.Parse("#090B10"), ColorOf("AtlasTextBrush", ThemeVariant.Light));
        Assert.Equal(Color.Parse("#5F6573"), ColorOf("AtlasTextSecondaryBrush", ThemeVariant.Light));
        Assert.Equal(Color.Parse("#246BFD"), ColorOf("AtlasAccentBrush", ThemeVariant.Dark));
        Assert.Equal(Color.Parse("#246BFD"), ColorOf("AtlasAccentBrush", ThemeVariant.Light));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light, "Light")]
    [InlineData(ThemePreference.Dark, "Dark")]
    [InlineData(ThemePreference.System, "Default")]
    public void ThemeController_ChangesTheWholeApplication(ThemePreference preference, string expected)
    {
        var application = Application.Current!;
        try
        {
            new ApplicationThemeController().Apply(preference);

            Assert.Equal(expected, application.RequestedThemeVariant?.Key?.ToString());
        }
        finally
        {
            application.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    private static void AssertContrast(string foreground, string background, ThemeVariant theme, double minimum)
    {
        var ratio = Contrast(ColorOf(foreground, theme), ColorOf(background, theme));

        Assert.True(ratio >= minimum, $"{foreground} on {background} in {theme}: {ratio:F2}:1, expected at least {minimum}:1.");
    }

    private static Color ColorOf(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var resource), $"{key} is missing from {theme}.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
    }

    private static double Contrast(Color first, Color second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second));
        if (lighter < darker)
            (lighter, darker) = (darker, lighter);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var scaled = value / 255d;
        return scaled <= 0.04045 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}
