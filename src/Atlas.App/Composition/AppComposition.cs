using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Settings;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Logging;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Composition;

/// <summary>The only place that decides which implementation serves which interface.</summary>
internal static class AppComposition
{
    public static ServiceProvider Build(AtlasDataPaths paths)
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

        services.AddSingleton<MainWindowHost>();
        services.AddSingleton<IDialogService, WindowDialogService>();
        services.AddSingleton<IThemeController, ApplicationThemeController>();
        services.AddSingleton<IFolderOpener, LauncherFolderOpener>();

        services.AddSingleton<PageServices>();
        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
