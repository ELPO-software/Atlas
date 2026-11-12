using System.Runtime.Versioning;
using Atlas.Core.Detection;
using Microsoft.Win32;

namespace Atlas.Platform.Windows;

/// <summary>
/// Tells whether this copy of Atlas is the one the installer put in place. Only that copy has a Start menu entry with
/// the identity of Atlas, so only that copy may show Windows notifications: Windows remembers the identity of an
/// application that once notified, even after it is uninstalled, and a portable copy must not borrow it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstalledCopy
{
    /// <summary>The key the installer writes, from its AppId (build/installer/Atlas.iss).</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D1538AA5-DB1C-4AEB-97A7-68AC1603F0A9}_is1";

    public static bool IsThisCopy() =>
        IsSameFolder(InstallLocation(RegistryHive.LocalMachine), AppContext.BaseDirectory)
        || IsSameFolder(InstallLocation(RegistryHive.CurrentUser), AppContext.BaseDirectory);

    /// <summary>Compares two folders as Windows does, whatever their case or final separator.</summary>
    public static bool IsSameFolder(string? installLocation, string folder) =>
        !string.IsNullOrWhiteSpace(installLocation) && PathText.Key(installLocation) == PathText.Key(folder);

    private static string? InstallLocation(RegistryHive hive)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }
}
