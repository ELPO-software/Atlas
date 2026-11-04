using Atlas.Infrastructure.Storage;

namespace Atlas.App.Tests;

/// <summary>A unique data folder whose path contains spaces and accents, removed after the test.</summary>
internal sealed class TemporaryDataFolder : IDisposable
{
    public TemporaryDataFolder()
    {
        Paths = new AtlasDataPaths(Path.Combine(Path.GetTempPath(), $"Données d'été Atlas {Guid.NewGuid():N}"));
        Directory.CreateDirectory(Paths.DataDirectory);
    }

    public AtlasDataPaths Paths { get; }

    public void Dispose() => Directory.Delete(Paths.DataDirectory, recursive: true);
}
