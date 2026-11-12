using Atlas.Infrastructure.Logging;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Tests;

public sealed class StorageAndLoggingTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void AtlasDataPaths_PlacesEveryFileInsideTheDataFolder()
    {
        var paths = new AtlasDataPaths(_folder.Path);

        Assert.Equal(Path.Combine(_folder.Path, "atlas.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(_folder.Path, "logs"), paths.LogsDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AtlasDataPaths_ByDefault_IsInTheLocalApplicationDataFolder(string? customDirectory)
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ELPO", "Atlas");

        var paths = AtlasDataPaths.Resolve(customDirectory);

        Assert.Equal(expected, paths.DataDirectory);
        Assert.False(paths.IsCustom);
    }

    [Fact]
    public void AtlasDataPaths_UsesTheFolderNamedByTheVariable()
    {
        var custom = Path.Combine(_folder.Path, "données d'essai");

        var paths = AtlasDataPaths.Resolve(custom);

        Assert.Equal(custom, paths.DataDirectory);
        Assert.True(paths.IsCustom);
        Assert.Equal(Path.Combine(custom, "atlas.db"), paths.DatabasePath);
    }

    [Theory]
    [InlineData("donnees")]
    [InlineData(@"..\donnees")]
    [InlineData(@"\donnees")]
    [InlineData("C:donnees")]
    public void AtlasDataPaths_RefusesAFolderThatIsNotAFullPath(string custom) =>
        Assert.Throws<DirectoryNotFoundException>(() => AtlasDataPaths.Resolve(custom));

    [Fact]
    public void FileLogging_WritesToAFolderWithSpacesAndAccents()
    {
        var logs = Path.Combine(_folder.Path, "journaux d'été");

        using (var factory = FileLogging.CreateLoggerFactory(logs))
        {
            factory.CreateLogger("Test").LogInformation("Démarrage de {Nom}", "Atlas");
        }

        var file = Assert.Single(Directory.GetFiles(logs, "atlas-*.log"));
        var content = File.ReadAllText(file);
        Assert.Contains("Démarrage de Atlas", content, StringComparison.Ordinal);
    }

    [Fact]
    public void FileLogging_IgnoresDetailedDiagnostics()
    {
        var logs = Path.Combine(_folder.Path, "journaux");

        using (var factory = FileLogging.CreateLoggerFactory(logs))
        {
            factory.CreateLogger("Test").LogDebug("Détail");
        }

        var file = Directory.GetFiles(logs, "atlas-*.log").SingleOrDefault();
        var content = file is null ? string.Empty : File.ReadAllText(file);
        Assert.DoesNotContain("Détail", content, StringComparison.Ordinal);
    }
}
