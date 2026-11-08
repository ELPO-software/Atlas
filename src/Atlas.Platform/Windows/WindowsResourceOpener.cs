using System.ComponentModel;
using System.Globalization;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;

namespace Atlas.Platform.Windows;

/// <summary>
/// Opens resources the way a double-click in Windows would, after checking that they are there.
/// Files that run code are never opened, and programs are started without any argument.
/// </summary>
public sealed class WindowsResourceOpener(IShell shell) : IResourceOpener
{
    /// <summary>A program that stops with an error within this time is reported as failed.</summary>
    public static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(1.5);

    private const int FileNotFound = 2;
    private const int PathNotFound = 3;
    private const int NoAssociation = 1155;

    public Task<OpenResult> OpenAsync(ResourceKind kind, string target)
    {
        try
        {
            return Task.FromResult(Open(kind, target));
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(Describe(ex));
        }
    }

    private OpenResult Open(ResourceKind kind, string target)
    {
        if (kind == ResourceKind.Application)
            return Launch(target);
        if (kind == ResourceKind.File)
            return OpenFile(target);
        if (kind == ResourceKind.Folder)
            return OpenFolder(target);

        return kind == ResourceKind.Url ? OpenWebAddress(target) : OpenResult.Skipped(RestoreReason.UnknownKind);
    }

    private OpenResult Launch(string target)
    {
        if (ResourceTargets.TryGetInstalledApplicationId(target, out var applicationId))
        {
            if (!ResourceTargets.IsInstalledApplicationId(applicationId))
                return OpenResult.Skipped(RestoreReason.InvalidTarget);

            if (!shell.InstalledApplicationExists(applicationId))
                return OpenResult.NotFound();

            return Started(shell.Open(ResourceTargets.ForInstalledApplication(applicationId), workingDirectory: null));
        }

        if (!ResourceTargets.IsUsablePath(target) || !ResourceTargets.IsProgram(target))
            return OpenResult.Skipped(RestoreReason.InvalidTarget);

        if (shell.DirectoryExists(target))
            return OpenResult.Failed(RestoreReason.WrongType);

        if (!shell.FileExists(target))
            return OpenResult.NotFound();

        return Started(shell.Open(target, Path.GetDirectoryName(target)));
    }

    private OpenResult OpenFile(string target)
    {
        if (!ResourceTargets.IsUsablePath(target))
            return OpenResult.Skipped(RestoreReason.InvalidTarget);

        var extension = ResourceTargets.ExtensionOf(target);
        if (ResourceTargets.RunsCode(target) || (extension.Length > 0 && shell.IsDangerousFileType("." + extension)))
            return OpenResult.Skipped(RestoreReason.BlockedFileType);

        if (shell.DirectoryExists(target))
            return OpenResult.Failed(RestoreReason.WrongType);

        if (!shell.FileExists(target))
            return OpenResult.NotFound();

        shell.Open(target, Path.GetDirectoryName(target))?.Dispose();
        return OpenResult.Opened();
    }

    private OpenResult OpenFolder(string target)
    {
        if (!ResourceTargets.IsUsablePath(target))
            return OpenResult.Skipped(RestoreReason.InvalidTarget);

        if (!shell.DirectoryExists(target))
            return shell.FileExists(target) ? OpenResult.Failed(RestoreReason.WrongType) : OpenResult.NotFound();

        // The final separator makes the system treat the path as a folder only: a file cannot answer to it.
        shell.Open(Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar, workingDirectory: null)?.Dispose();
        return OpenResult.Opened();
    }

    private OpenResult OpenWebAddress(string target)
    {
        if (!ResourceTargets.TryGetWebAddress(target, out var address))
            return OpenResult.Skipped(RestoreReason.InvalidTarget);

        shell.Open(address.AbsoluteUri, workingDirectory: null)?.Dispose();
        return OpenResult.Opened();
    }

    private static OpenResult Started(IStartedProcess? process) =>
        process is null ? OpenResult.Launched() : OpenResult.Launched(ConfirmAsync(process));

    private static async Task<OpenResult> ConfirmAsync(IStartedProcess process)
    {
        using (process)
        {
            // A program that stops at once without an error has usually handed over to a copy already running.
            var exitCode = await process.WaitForExitAsync(EarlyExitWindow);
            return exitCode is null or 0
                ? OpenResult.Launched()
                : OpenResult.Failed(RestoreReason.ExitedImmediately, exitCode.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static OpenResult Describe(Win32Exception exception) => exception.NativeErrorCode switch
    {
        FileNotFound or PathNotFound => OpenResult.NotFound(),
        NoAssociation => OpenResult.Failed(RestoreReason.NoAssociatedApplication),
        _ => OpenResult.Failed(RestoreReason.SystemRefused, exception.Message),
    };
}
