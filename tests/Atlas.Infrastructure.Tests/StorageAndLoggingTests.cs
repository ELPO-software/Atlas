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

    [Fact]
    public void AtlasDataPaths_ForCurrentUser_IsInTheLocalApplicationDataFolder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ELPO", "Atlas");

        Assert.Equal(expected, AtlasDataPaths.ForCurrentUser().DataDirectory);
    }

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
