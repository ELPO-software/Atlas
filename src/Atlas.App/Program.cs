using Atlas.Infrastructure.Storage;
using Atlas.Platform;
using Atlas.Platform.Windows;
using Atlas.Platform.Windows.Notifications;
using Avalonia;

namespace Atlas.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var guard = new SingleInstanceGuard(
            SingleInstanceGuard.NameFor(Environment.GetEnvironmentVariable(AtlasDataPaths.DirectoryVariable)));

        // A second start brings the running Atlas forward and ends without opening anything else.
        if (!guard.IsFirstInstance && guard.ActivateFirstInstance())
            return 0;

        AtlasApp.InstanceGuard = guard;

        // An installed Atlas has a Start menu entry with its identity: its windows and notifications then belong to it.
        // It must be set before the first window opens.
        if (OperatingSystem.IsWindows())
            new WindowsNotifications(InstalledCopy.IsThisCopy()).TryAdoptApplicationIdentity();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AtlasApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
