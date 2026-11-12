using Atlas.Infrastructure.Storage;
using Atlas.Platform;
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
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AtlasApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
