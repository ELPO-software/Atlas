using Atlas.Platform;
using Avalonia.Controls;

namespace Atlas.App.Views;

/// <summary>Brings an Atlas window back in front, when Atlas is started again or a notification is chosen.</summary>
internal static class WindowActivation
{
    public static void BringToFront(Window? window)
    {
        if (window is null)
            return;

        if (!window.IsVisible)
            window.Show();

        if (WindowForeground.TryBringToFront(window.TryGetPlatformHandle()?.Handle ?? nint.Zero))
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }
}
