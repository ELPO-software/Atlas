using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsShell : IShell
{
    private static readonly Guid ShellItemInterface = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool InstalledApplicationExists(string applicationId)
    {
        var result = SHCreateItemFromParsingName(
            ResourceTargets.ForInstalledApplication(applicationId), nint.Zero, ShellItemInterface, out var item);

        if (item != nint.Zero)
            Marshal.Release(item);

        return result == 0;
    }

    public bool IsDangerousFileType(string extension) => AssocIsDangerous(extension);

    public IStartedProcess? Open(string target, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };

        var process = Process.Start(startInfo);
        return process is null ? null : new StartedProcess(process);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssocIsDangerous(string extension);

    private sealed class StartedProcess(Process process) : IStartedProcess
    {
        public async Task<int?> WaitForExitAsync(TimeSpan timeout)
        {
            using var limit = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose() => process.Dispose();
    }
}
