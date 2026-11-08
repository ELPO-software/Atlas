using Atlas.App.Resources;

namespace Atlas.App.About;

/// <param name="PackagePrefixes">The NuGet packages this entry covers. ThirdPartyComponentsTests fails if a package has no entry.</param>
public sealed record ThirdPartyComponent(string Name, string License, IReadOnlyList<string> PackagePrefixes);

/// <summary>The components shipped with Atlas, with the licenses declared by their packages.</summary>
public static class ThirdPartyComponents
{
    public static IReadOnlyList<ThirdPartyComponent> All { get; } =
    [
        new(".NET", "MIT", []),
        new("ANGLE", "BSD-3-Clause", ["Avalonia.Angle."]),
        new("Avalonia", "MIT", ["Avalonia"]),
        new("CommunityToolkit.Mvvm", "MIT", ["CommunityToolkit.Mvvm"]),
        new("HarfBuzzSharp", "MIT", ["HarfBuzzSharp"]),
        new("Inter", "OFL-1.1", ["Avalonia.Fonts.Inter"]),
        new("MicroCom.Runtime", "MIT", ["MicroCom.Runtime"]),
        new("Microsoft.Data.Sqlite", "MIT", ["Microsoft.Data.Sqlite"]),
        new("Microsoft.Extensions", "MIT", ["Microsoft.Extensions."]),
        new("Serilog", "Apache-2.0", ["Serilog"]),
        new("SkiaSharp", "MIT", ["SkiaSharp"]),
        new("SQLite", Strings.PublicDomain, []),
        new("SQLitePCLRaw", "Apache-2.0", ["SQLitePCLRaw."]),
        new("Tmds.DBus.Protocol", "MIT", ["Tmds.DBus.Protocol"]),
    ];
}
