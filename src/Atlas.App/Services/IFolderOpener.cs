using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Services;

public interface IFolderOpener
{
    /// <returns>False if the system could not open the folder.</returns>
    Task<bool> OpenAsync(string path);
}

internal sealed class LauncherFolderOpener(MainWindowHost host, ILogger<LauncherFolderOpener> logger) : IFolderOpener
{
    public async Task<bool> OpenAsync(string path)
    {
        if (host.Window?.Launcher is not { } launcher)
            return false;

        try
        {
            return await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The folder {Path} could not be opened", path);
            return false;
        }
    }
}
