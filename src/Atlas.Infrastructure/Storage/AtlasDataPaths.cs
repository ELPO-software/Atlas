namespace Atlas.Infrastructure.Storage;

/// <summary>Where Atlas keeps its data on this computer. Nothing is ever stored anywhere else.</summary>
public sealed record AtlasDataPaths(string DataDirectory)
{
    /// <summary>
    /// Names another data folder, as a full path. It keeps tests and trials away from the data of the person
    /// who uses this computer; nothing in Atlas sets it.
    /// </summary>
    public const string DirectoryVariable = "ATLAS_DATA_DIR";

    public string DatabasePath => Path.Combine(DataDirectory, "atlas.db");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>True when the folder comes from <see cref="DirectoryVariable"/> rather than the default location.</summary>
    public bool IsCustom { get; init; }

    /// <summary>The per-user local data folder: %LOCALAPPDATA%\ELPO\Atlas on Windows, unless <see cref="DirectoryVariable"/> names another.</summary>
    public static AtlasDataPaths ForCurrentUser() =>
        Resolve(Environment.GetEnvironmentVariable(DirectoryVariable));

    /// <param name="customDirectory">The value of <see cref="DirectoryVariable"/>, if any.</param>
    public static AtlasDataPaths Resolve(string? customDirectory)
    {
        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            // A relative path would depend on where Atlas was started from, and could land anywhere.
            return Path.IsPathFullyQualified(customDirectory)
                ? new AtlasDataPaths(Path.GetFullPath(customDirectory)) { IsCustom = true }
                : throw new DirectoryNotFoundException($"{DirectoryVariable} must be a full path.");
        }

        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return string.IsNullOrEmpty(root)
            ? throw new DirectoryNotFoundException("This system does not provide a local application data folder.")
            : new AtlasDataPaths(Path.Combine(root, "ELPO", "Atlas"));
    }
}
