using Atlas.Core.Detection;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows.Detection;

/// <summary>
/// Finds the project a file or folder belongs to: the Git repository that holds it, or else the nearest folder with a
/// solution or a package manifest. It only reads names and one Git configuration file, never the content of the project.
/// </summary>
public sealed class ProjectLocator
{
    private const int MaxDepth = 12;

    private static readonly string[] Manifests =
    [
        "package.json", "pyproject.toml", "Cargo.toml", "go.mod", "pom.xml", "build.gradle", "build.gradle.kts",
        "composer.json", "Gemfile", "CMakeLists.txt", "Directory.Build.props",
    ];

    private static readonly string[] EntryFilePatterns = ["*.slnx", "*.sln", "*.code-workspace"];

    private readonly HashSet<string> _neverProjects;

    /// <param name="neverProjects">Folders that hold projects but are not projects themselves, such as the user's home folder.</param>
    public ProjectLocator(IEnumerable<string> neverProjects) =>
        _neverProjects = neverProjects.Where(p => !string.IsNullOrWhiteSpace(p)).Select(PathText.Key).ToHashSet(StringComparer.Ordinal);

    public static ProjectLocator ForCurrentUser() =>
        new(new[]
        {
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
        }.Select(Environment.GetFolderPath).Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));

    /// <returns>The project that holds <paramref name="path"/>, or null if it belongs to none.</returns>
    public ObservedProject? Find(string path)
    {
        if (!ResourceTargets.IsUsablePath(path) || ResourceTargets.IsNetworkPath(path))
            return null;

        DirectoryInfo? folder;
        try
        {
            folder = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        string? repository = null;
        string? manifestFolder = null;

        for (var depth = 0; folder is not null && depth < MaxDepth; depth++, folder = folder.Parent)
        {
            if (folder.Parent is null || _neverProjects.Contains(PathText.Key(folder.FullName)))
                break;

            if (HasGitRepository(folder.FullName))
            {
                repository = folder.FullName;
                break;
            }

            if (manifestFolder is null && HasManifest(folder.FullName))
                manifestFolder = folder.FullName;
        }

        var root = repository ?? manifestFolder;
        if (root is null)
            return null;

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        return new ObservedProject(root, name, FindEntryFile(root, name), repository is null ? null : FindRepositoryAddress(root));
    }

    /// <summary>The solution or workspace file that opens the project, when there is exactly one, or one named after the folder.</summary>
    internal static string? FindEntryFile(string root, string name)
    {
        foreach (var pattern in EntryFilePatterns)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            // "*.sln" also matches ".slnx" files on Windows; each pattern keeps its own extension only.
            files = files.Where(f => string.Equals(Path.GetExtension(f), pattern[1..], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 1)
                return files[0];

            var named = files.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
                return named;
        }

        return null;
    }

    /// <summary>The web address of the "origin" remote, read from the Git configuration of the repository.</summary>
    internal static string? FindRepositoryAddress(string root)
    {
        var gitPath = Path.Combine(root, ".git");
        try
        {
            var gitFolder = gitPath;

            // A worktree or submodule has a .git file that points to the real Git folder.
            if (File.Exists(gitPath))
            {
                var pointer = File.ReadLines(gitPath).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
                if (pointer is null)
                    return null;

                gitFolder = Path.GetFullPath(pointer["gitdir:".Length..].Trim(), root);
                var commonDirectory = Path.Combine(gitFolder, "commondir");
                if (File.Exists(commonDirectory))
                    gitFolder = Path.GetFullPath(File.ReadAllText(commonDirectory).Trim(), gitFolder);
            }

            var config = Path.Combine(gitFolder, "config");
            if (!File.Exists(config) || new FileInfo(config).Length > 256 * 1024)
                return null;

            return RepositoryAddress.FromGitConfig(File.ReadAllLines(config));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool HasGitRepository(string folder)
    {
        var git = Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private static bool HasManifest(string folder)
    {
        try
        {
            if (Manifests.Any(m => File.Exists(Path.Combine(folder, m))))
                return true;

            return EntryFilePatterns.Any(pattern => Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly).Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Turns the address of a Git remote into a web address a browser can open.</summary>
public static class RepositoryAddress
{
    /// <returns>The web address of the "origin" remote, or null if there is none or it is not on the web.</returns>
    public static string? FromGitConfig(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var inOrigin = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inOrigin = line.Replace(" ", string.Empty, StringComparison.Ordinal).Equals("[remote\"origin\"]", StringComparison.Ordinal);
                continue;
            }

            if (!inOrigin)
                continue;

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
                return FromRemote(line[(equals + 1)..].Trim());
        }

        return null;
    }

    /// <summary>
    /// Accepts https remotes and the usual SSH forms (git@host:owner/name and ssh://git@host/owner/name). Credentials
    /// written in the address are dropped: they must never reach a workspace or an exported file.
    /// </summary>
    public static string? FromRemote(string remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        remote = remote.Trim().Trim('"');

        string host;
        string path;
        if (remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || remote.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                return null;

            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            // Any other scheme, such as file://, is not on the web.
            if (remote.Contains("://", StringComparison.Ordinal))
                return null;

            // scp-like syntax: [user@]host:owner/name.git
            var colon = remote.IndexOf(':', StringComparison.Ordinal);
            var at = remote.IndexOf('@', StringComparison.Ordinal);
            if (colon <= 0 || remote.Contains('\\', StringComparison.Ordinal) || (colon == 1 && char.IsLetter(remote[0])))
                return null;

            host = remote[((at >= 0 && at < colon) ? at + 1 : 0)..colon];
            path = "/" + remote[(colon + 1)..].TrimStart('/');
        }

        path = path.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];

        if (host.Length == 0 || path.Trim('/').Length == 0 || host.Contains(' ', StringComparison.Ordinal))
            return null;

        var address = $"https://{host.ToLowerInvariant()}{path}";
        return ResourceTargets.TryGetWebAddress(address, out _) ? address : null;
    }
}
