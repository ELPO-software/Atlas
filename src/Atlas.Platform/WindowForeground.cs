using System.Runtime.InteropServices;

namespace Atlas.Platform;

/// <summary>Brings a window of Atlas back in front of the user.</summary>
public static partial class WindowForeground
{
    private const int Restore = 9;

    /// <summary>Restores the window if it is minimized, in the size it had before, and moves it to the front.</summary>
    /// <returns>False on systems where Atlas cannot do it this way; the caller then uses the interface library instead.</returns>
    public static bool TryBringToFront(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == nint.Zero)
            return false;

        if (IsIconic(windowHandle))
            ShowWindow(windowHandle, Restore);

        // Windows may refuse, if the user is busy in another application. The taskbar button then flashes instead.
        SetForegroundWindow(windowHandle);
        return true;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
