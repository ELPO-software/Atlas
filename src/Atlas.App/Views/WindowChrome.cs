using Atlas.Platform;
using Avalonia.Controls;
using Avalonia.Media;

namespace Atlas.App.Views;

/// <summary>Keeps the title bar of a window in the color of its content, through theme changes too.</summary>
internal static class WindowChrome
{
    private const string BackgroundResource = "AtlasBackgroundBrush";
    private const string WindowsHandleKind = "HWND";

    public static void MatchTitleBarToTheme(Window window)
    {
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: WindowsHandleKind } handle)
            return;

        if (window.TryFindResource(BackgroundResource, window.ActualThemeVariant, out var resource)
            && resource is ISolidColorBrush { Color: var color })
        {
            TitleBarColor.TrySet(handle.Handle, color.R, color.G, color.B);
        }
    }
}
