using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Core.Settings;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Logging;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Storage;
using Atlas.Platform;
using Atlas.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Composition;

/// <summary>The only place that decides which implementation serves which interface.</summary>
internal static class AppComposition
{
    /// <param name="replace">Lets tests stand in for the services that act on the computer, such as opening resources.</param>
    public static ServiceProvider Build(AtlasDataPaths paths, Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(_ => FileLogging.CreateLoggerFactory(paths.LogsDirectory));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        services.AddSingleton(_ => new SqliteDatabase(paths.DatabasePath));
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<RestorationService>();

        // Opening resources depends on the operating system. Elsewhere, Atlas says it cannot instead of pretending.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IResourceOpener>(new WindowsResourceOpener(new WindowsShell()));
            services.AddSingleton<IApplicationCatalog, ShellApplicationCatalog>();
        }
        else
        {
            services.AddSingleton<IResourceOpener, UnsupportedResourceOpener>();
            services.AddSingleton<IApplicationCatalog, EmptyApplicationCatalog>();
        }

        services.AddSingleton<MainWindowHost>();
        services.AddSingleton<IDialogService, WindowDialogService>();
        services.AddSingleton<IThemeController, ApplicationThemeController>();
        services.AddSingleton<IFolderOpener, LauncherFolderOpener>();

        services.AddSingleton<PageServices>();
        services.AddTransient<MainWindowViewModel>();

        replace?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
