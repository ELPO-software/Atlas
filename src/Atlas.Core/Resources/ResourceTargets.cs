using System.Collections.Frozen;

namespace Atlas.Core.Resources;

/// <summary>Reads what a resource target designates, the way Windows will read it.</summary>
public static class ResourceTargets
{
    /// <summary>Opens an application through the list of applications installed on this computer.</summary>
    public const string InstalledApplicationPrefix = @"shell:AppsFolder\";

    private const string ExecutableExtension = "exe";

    /// <summary>
    /// Types that run code when opened. Windows' own list of dangerous types is checked as well when a file is
    /// opened; this one also covers script types that list leaves out, such as Python.
    /// </summary>
    private static readonly FrozenSet<string> CodeExtensions = FrozenSet.ToFrozenSet(
        [
            "ade", "adp", "ahk", "app", "application", "appinstaller", "appref-ms", "appx", "appxbundle", "asp", "au3",
            "bas", "bash", "bat", "cer", "chm", "cmd", "com", "cpl", "crt", "csh", "der", "diagcab", "diagcfg",
            "diagpkg", "exe", "fxp", "gadget", "grp", "hlp", "hpj", "hta", "inf", "ins", "isp", "its", "jar", "jnlp",
            "js", "jse", "job", "ksh", "library-ms", "lnk", "mad", "maf", "mag", "mam", "maq", "mar", "mas", "mat",
            "mau", "mav", "maw", "mcf", "mda", "mde", "mdt", "mdw", "mdz", "msc", "msh", "msh1", "msh1xml", "msh2",
            "msh2xml", "mshxml", "msi", "msix", "msixbundle", "msp", "mst", "ops", "pcd", "pif", "pl", "plg", "prf",
            "prg", "ps1", "ps1xml", "ps2", "ps2xml", "psc1", "psc2", "psd1", "psm1", "pst", "py", "pyc", "pyo", "pyw",
            "pyz", "pyzw", "rb", "reg", "scf", "scr", "sct", "search-ms", "searchconnector-ms", "settingcontent-ms",
            "sh", "shb", "shs", "tmp", "url", "vb", "vbe", "vbp", "vbs", "vsmacros", "vsw", "website", "ws", "wsc",
            "wsf", "wsh", "xbap", "xll", "xnk",
        ],
        StringComparer.Ordinal);

    private static readonly char[] CharactersWindowsRefuses = ['"', '*', '?', '<', '>', '|'];

    public static string ForInstalledApplication(string applicationId) => InstalledApplicationPrefix + applicationId;

    public static bool TryGetInstalledApplicationId(string target, out string applicationId)
    {
        if (target.StartsWith(InstalledApplicationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            applicationId = target[InstalledApplicationPrefix.Length..];
            return true;
        }

        applicationId = string.Empty;
        return false;
    }

    /// <summary>
    /// True for a complete path that names one location: no device path, no wildcard, and no alternate data stream
    /// such as "notes.txt:hidden.exe", which Windows would open as something else.
    /// </summary>
    public static bool IsUsablePath(string target)
    {
        if (target.IndexOfAny(CharactersWindowsRefuses) >= 0)
            return false;

        if (target.StartsWith(@"\\?\", StringComparison.Ordinal) || target.StartsWith(@"\\.\", StringComparison.Ordinal))
            return false;

        var hasDrive = target.Length >= 2 && target[1] == ':';
        if (target.IndexOf(':', hasDrive ? 2 : 0) >= 0)
            return false;

        return Path.IsPathFullyQualified(target);
    }

    public static bool IsNetworkPath(string target) =>
        target.StartsWith(@"\\", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// The extension Windows will act on, in lower case. Windows ignores dots and spaces at the end of a name,
    /// so "rapport.bat." is a batch file.
    /// </summary>
    public static string ExtensionOf(string path)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..].TrimEnd('.', ' ');
        var dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
    }

    public static bool RunsCode(string path) => CodeExtensions.Contains(ExtensionOf(path));

    public static bool IsProgram(string path) => ExtensionOf(path) == ExecutableExtension;

    public static bool TryGetWebAddress(string target, out Uri address)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(parsed.Host))
        {
            address = parsed;
            return true;
        }

        address = null!;
        return false;
    }

    /// <summary>
    /// An installed application is named by an identifier. Identifiers that are paths must name a program, so that a
    /// help file or a document listed among the applications is not taken for one; web addresses are refused.
    /// </summary>
    public static bool IsInstalledApplicationId(string applicationId)
    {
        if (applicationId.Length == 0 || applicationId.Contains("://", StringComparison.Ordinal) || applicationId.Contains('"'))
            return false;

        var isPath = applicationId.Contains('\\') || applicationId.Contains('/');
        return !isPath || IsProgram(applicationId);
    }
}
