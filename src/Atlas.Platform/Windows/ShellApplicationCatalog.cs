using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows;

/// <summary>
/// Reads the list of applications that the Start menu shows. Entries that are not applications, such as web
/// shortcuts or help files, are left out.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellApplicationCatalog : IApplicationCatalog
{
    private const string ApplicationsFolder = "shell:AppsFolder";

    public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The shell's objects expect a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Read());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Atlas application list",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return result.Task.WaitAsync(cancellationToken);
    }

    private static List<InstalledApplication> Read()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("The Windows shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic items = shell.NameSpace(ApplicationsFolder).Items();
            int count = items.Count;
            var applications = new List<InstalledApplication>(count);

            for (var index = 0; index < count; index++)
            {
                dynamic item = items.Item(index);
                string? name = item.Name;
                string? applicationId = item.Path;

                if (!string.IsNullOrWhiteSpace(name) && applicationId is not null && ResourceTargets.IsInstalledApplicationId(applicationId))
                    applications.Add(new InstalledApplication(name.Trim(), ResourceTargets.ForInstalledApplication(applicationId)));
            }

            var byName = StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true);
            return applications.DistinctBy(a => a.Target, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, byName).ToList();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
