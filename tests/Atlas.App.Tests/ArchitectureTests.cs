using System.Reflection;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Persistence;
using Atlas.Platform;

namespace Atlas.App.Tests;

/// <summary>The dependency rules from docs/architecture.md. Breaking one fails the test run.</summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Core = typeof(Workspace).Assembly;
    private static readonly Assembly Infrastructure = typeof(SqliteDatabase).Assembly;
    private static readonly Assembly PlatformLayer = typeof(SingleInstanceGuard).Assembly;

    [Fact]
    public void Core_DependsOnNoOtherAtlasProjectOrThirdPartyLibrary()
    {
        var references = ReferenceNames(Core);

        Assert.DoesNotContain(references, name => name.StartsWith("Atlas.", StringComparison.Ordinal));
        Assert.All(references, name => Assert.True(IsBaseLibrary(name), $"Atlas.Core must not reference {name}."));
    }

    [Fact]
    public void Infrastructure_ReferencesOnlyCoreAmongAtlasProjects() =>
        Assert.Equal(["Atlas.Core"], AtlasReferences(Infrastructure));

    [Fact]
    public void Platform_ReferencesAtMostCoreAmongAtlasProjects() =>
        Assert.Empty(AtlasReferences(PlatformLayer).Except(["Atlas.Core"]));

    [Theory]
    [InlineData("Avalonia")]
    [InlineData("CommunityToolkit.Mvvm")]
    public void OnlyTheApplicationKnowsTheInterfaceLibraries(string prefix)
    {
        foreach (var assembly in new[] { Core, Infrastructure, PlatformLayer })
            Assert.DoesNotContain(ReferenceNames(assembly), name => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Platform_DoesNotKnowTheDatabase() =>
        Assert.DoesNotContain(ReferenceNames(PlatformLayer), name => name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("Avalonia.BuildServices.dll")]
    [InlineData("Microsoft.ApplicationInsights.dll")]
    [InlineData("Microsoft.Testing.Extensions.Telemetry.dll")]
    public void NoTelemetryLibraryReachesTheOutput(string fileName) =>
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, fileName)), $"{fileName} must stay excluded.");

    private static List<string> ReferenceNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    private static List<string> AtlasReferences(Assembly assembly) =>
        ReferenceNames(assembly).Where(name => name.StartsWith("Atlas.", StringComparison.Ordinal)).ToList();

    private static bool IsBaseLibrary(string name) =>
        name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal);
}
