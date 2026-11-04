using Atlas.App.Tests;
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Atlas.App.Tests;

/// <summary>Runs the real application class and theme without showing anything on screen.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AtlasApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
