namespace Atlas.Infrastructure.Storage;

/// <summary>Where Atlas keeps its data on this computer. Nothing is ever stored anywhere else.</summary>
public sealed record AtlasDataPaths(string DataDirectory)
{
    public string DatabasePath => Path.Combine(DataDirectory, "atlas.db");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>The per-user local data folder: %LOCALAPPDATA%\ELPO\Atlas on Windows.</summary>
    public static AtlasDataPaths ForCurrentUser()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return string.IsNullOrEmpty(root)
            ? throw new DirectoryNotFoundException("This system does not provide a local application data folder.")
            : new AtlasDataPaths(Path.Combine(root, "ELPO", "Atlas"));
    }
}
