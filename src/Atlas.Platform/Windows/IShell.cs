namespace Atlas.Platform.Windows;

/// <summary>The system calls that opening resources needs, kept apart so that tests can stand in for them.</summary>
public interface IShell
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Looks the application up in the system's list without starting it.</summary>
    bool InstalledApplicationExists(string applicationId);

    /// <param name="extension">With its leading dot, such as ".exe".</param>
    bool IsDangerousFileType(string extension);

    /// <summary>
    /// Asks the system to open a file, a folder, a web address or an application, as a double-click would.
    /// No command-line argument is ever passed.
    /// </summary>
    /// <returns>The process the system started, when it reports one.</returns>
    IStartedProcess? Open(string target, string? workingDirectory);
}

public interface IStartedProcess : IDisposable
{
    /// <returns>The exit code if the process ended within the time given, or null if it is still running.</returns>
    Task<int?> WaitForExitAsync(TimeSpan timeout);
}
