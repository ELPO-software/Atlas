using System.Runtime.InteropServices;

namespace Atlas.Platform;

/// <summary>Whether the user lets applications animate, as set in Windows (Accessibility, Visual effects).</summary>
public static partial class SystemMotion
{
    private const int GetClientAreaAnimation = 0x1042;

    /// <returns>True unless the user turned animations off. Other systems are assumed to allow them.</returns>
    public static bool AnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        return !SystemParametersInfo(GetClientAreaAnimation, 0, out var enabled, 0) || enabled;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(int action, int parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, int update);
}
