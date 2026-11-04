namespace Atlas.Infrastructure.Tests;

/// <summary>A unique folder whose path deliberately contains spaces and accents, removed after the test.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Données d'été Atlas {Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string DatabasePath => System.IO.Path.Combine(Path, "atlas.db");

    public void Dispose()
    {
        // Failing here would mean a test left a file open, which is a bug the test must reveal.
        Directory.Delete(Path, recursive: true);
    }
}
