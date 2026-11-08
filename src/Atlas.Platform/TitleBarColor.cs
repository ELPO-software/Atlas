using System.Runtime.InteropServices;

namespace Atlas.Platform;

/// <summary>Tints the title bar drawn by the system, which keeps its own buttons and window snapping.</summary>
public static partial class TitleBarColor
{
    private const int CaptionColorAttribute = 35;
    private const int FirstBuildWithCaptionColor = 22000;

    /// <returns>False if this system cannot tint title bars, as on Windows 10 and other platforms. The title bar then keeps its default color.</returns>
    public static bool TrySet(nint windowHandle, byte red, byte green, byte blue)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstBuildWithCaptionColor))
            return false;

        var colorRef = red | (green << 8) | (blue << 16);
        return DwmSetWindowAttribute(windowHandle, CaptionColorAttribute, ref colorRef, sizeof(int)) == 0;
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
