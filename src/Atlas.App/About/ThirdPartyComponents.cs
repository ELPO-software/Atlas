using Atlas.App.Resources;

namespace Atlas.App.About;

/// <param name="PackagePrefixes">The NuGet packages this entry covers. ThirdPartyComponentsTests fails if a package has no entry.</param>
/// <param name="LicenseFiles">The files of the licenses folder that hold its full license text, built into Atlas.</param>
public sealed record ThirdPartyComponent(string Name, string License, IReadOnlyList<string> PackagePrefixes, IReadOnlyList<string> LicenseFiles);

/// <summary>The components shipped with Atlas, with the licenses declared by their packages.</summary>
public static class ThirdPartyComponents
{
    private const string ResourcePrefix = "Atlas.Licenses.";

    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new(".NET", "MIT", [], ["dotnet.txt", "dotnet-third-party-notices.txt"]),
        new("ANGLE", "BSD-3-Clause", ["Avalonia.Angle."], ["ANGLE.txt"]),
        new("Avalonia", "MIT", ["Avalonia"], ["Avalonia.txt"]),
        new("CommunityToolkit.Mvvm", "MIT", ["CommunityToolkit.Mvvm"], ["CommunityToolkit.Mvvm.txt", "CommunityToolkit.Mvvm-third-party-notices.txt"]),
        new("HarfBuzzSharp", "MIT", ["HarfBuzzSharp"], ["SkiaSharp.txt", "SkiaSharp-third-party-notices.txt"]),
        new("Inter", "OFL-1.1", ["Avalonia.Fonts.Inter"], ["Inter.txt"]),
        new("MicroCom.Runtime", "MIT", ["MicroCom.Runtime"], ["MicroCom.Runtime.txt"]),
        new("Microsoft.Data.Sqlite", "MIT", ["Microsoft.Data.Sqlite"], ["dotnet.txt"]),
        new("Microsoft.Extensions", "MIT", ["Microsoft.Extensions."], ["dotnet.txt", "dotnet-third-party-notices.txt"]),
        new("Serilog", "Apache-2.0", ["Serilog"], ["Serilog.txt"]),
        new("SkiaSharp", "MIT", ["SkiaSharp"], ["SkiaSharp.txt", "SkiaSharp-third-party-notices.txt"]),
        new("SQLite", Strings.PublicDomain, [], ["SQLite.txt"]),
        new("SQLitePCLRaw", "Apache-2.0", ["SQLitePCLRaw."], ["SQLitePCLRaw.txt"]),
        new("Tmds.DBus.Protocol", "MIT", ["Tmds.DBus.Protocol"], ["Tmds.DBus.Protocol.txt"]),
    ];

    /// <summary>
    /// The full license text of a component, read from Atlas itself, so that it travels with every copy, including
    /// the single executable.
    /// </summary>
    public static string ReadLicense(ThirdPartyComponent component) =>
        string.Join(
            Environment.NewLine + Environment.NewLine + new string('-', 72) + Environment.NewLine + Environment.NewLine,
            component.LicenseFiles.Select(ReadFile));

    private static string ReadFile(string fileName)
    {
        using var stream = typeof(ThirdPartyComponents).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"The license file {fileName} is missing from Atlas.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
