using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows.Detection;

/// <summary>Lists the folders open in File Explorer windows and tabs, through the shell's list of its windows.</summary>
[SupportedOSPlatform("windows")]
internal static class ExplorerWindows
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<string> ListFolders()
    {
        // The shell objects live in a single-threaded apartment, shared with the application list.
        var read = ShellApartment.RunAsync(Read);
        try
        {
            return read.Wait(Limit) ? read.Result : [];
        }
        catch (AggregateException)
        {
            return [];
        }
    }

    private static List<string> Read()
    {
        var folders = new List<string>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return folders;

        // Never released by force: the shell may hand the same object to the application list, read at the same time.
        try
        {
            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return folders;

            dynamic windows = ((dynamic)shell).Windows();
            int count = windows.Count;
            for (var i = 0; i < count && i < 100; i++)
            {
                try
                {
                    dynamic? window = windows.Item(i);
                    string? path = window?.Document?.Folder?.Self?.Path;
                    if (path is not null && ResourceTargets.IsUsablePath(path) && Directory.Exists(path)
                        && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(path);
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
                {
                    // A browser window or a window that closed meanwhile: it holds no folder to report.
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
            return folders;
        }

        return folders;
    }
}
