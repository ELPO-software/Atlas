using System.Text.Json;
using Atlas.App.About;

namespace Atlas.App.Tests;

public sealed class ThirdPartyComponentsTests
{
    [Fact]
    public void EveryPackageShippedWithTheApplicationIsListed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Atlas.deps.json");
        Assert.True(File.Exists(path), $"{path} is needed to check the list of components.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var packages = document.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "package")
            .Select(library => library.Name.Split('/')[0])
            .ToList();

        Assert.NotEmpty(packages);
        var unlisted = packages.Where(package => !IsListed(package)).ToList();
        Assert.True(unlisted.Count == 0, $"Add these packages to ThirdPartyComponents: {string.Join(", ", unlisted)}");
    }

    [Fact]
    public void EveryComponentCarriesItsFullLicenseText() =>
        Assert.All(ThirdPartyComponents.All, component =>
        {
            Assert.NotEmpty(component.LicenseFiles);
            Assert.True(ThirdPartyComponents.ReadLicense(component).Length > 100, $"The license of {component.Name} looks empty.");
        });

    [Theory]
    [InlineData("Avalonia", "AvaloniaUI")]
    [InlineData("ANGLE", "Redistribution and use in source and binary forms")]
    [InlineData("Inter", "SIL OPEN FONT LICENSE")]
    [InlineData("Inno Setup", "Jordan Russell")]
    [InlineData("Serilog", "Apache License")]
    [InlineData("SQLitePCLRaw", "Apache License")]
    [InlineData("SQLite", "May you do good and not evil")]
    [InlineData(".NET", ".NET Foundation and Contributors")]
    [InlineData("SkiaSharp", "Microsoft Corporation")]
    [InlineData("Tmds.DBus.Protocol", "Alp Toker")]
    public void LicenseTexts_AreTheRightOnes(string name, string expected) =>
        Assert.Contains(expected, ThirdPartyComponents.ReadLicense(ThirdPartyComponents.All.Single(c => c.Name == name)), StringComparison.Ordinal);

    [Fact]
    public void LicenseTexts_OfSeveralFilesAreKeptApart()
    {
        var text = ThirdPartyComponents.ReadLicense(ThirdPartyComponents.All.Single(c => c.Name == "CommunityToolkit.Mvvm"));

        Assert.Contains(new string('-', 72), text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEntryNamesItsLicense() =>
        Assert.All(ThirdPartyComponents.All, component => Assert.False(string.IsNullOrWhiteSpace(component.License)));

    [Theory]
    [InlineData("ANGLE", "BSD-3-Clause")]
    [InlineData("Inter", "OFL-1.1")]
    [InlineData("Serilog", "Apache-2.0")]
    [InlineData("SQLitePCLRaw", "Apache-2.0")]
    public void ComponentsOutsideMitAreNamedWithTheirOwnLicense(string name, string license) =>
        Assert.Equal(license, ThirdPartyComponents.All.Single(c => c.Name == name).License);

    private static bool IsListed(string package) =>
        ThirdPartyComponents.All.Any(component =>
            component.PackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.Ordinal)));
}
