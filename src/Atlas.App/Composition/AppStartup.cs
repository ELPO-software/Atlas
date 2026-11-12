using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.App.Composition;

/// <param name="Services">The running services, or null if Atlas cannot start.</param>
/// <param name="FailureMessage">What to tell the user when <paramref name="Services"/> is null.</param>
internal sealed record StartupOutcome(ServiceProvider? Services, string? FailureMessage);

/// <summary>
/// Prepares the data folder and the database. A problem becomes a message for the user: Atlas never
/// repairs, replaces or deletes a data file on its own to get past a failure.
/// </summary>
internal static class AppStartup
{
    public static StartupOutcome RunForCurrentUser()
    {
        try
        {
            return Run(AtlasDataPaths.ForCurrentUser());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var custom = Environment.GetEnvironmentVariable(AtlasDataPaths.DirectoryVariable);
            var location = string.IsNullOrWhiteSpace(custom) ? "%LOCALAPPDATA%" : custom;
            return new StartupOutcome(null, UiText.Format(Strings.DataFolderUnavailableMessage, location));
        }
    }

    public static StartupOutcome Run(AtlasDataPaths paths, Action<IServiceCollection>? replace = null)
    {
        ServiceProvider? services = null;
        try
        {
            Directory.CreateDirectory(paths.DataDirectory);
            services = AppComposition.Build(paths, replace);
            services.GetRequiredService<DatabaseMigrator>().Migrate();
            return new StartupOutcome(services, null);
        }
        catch (Exception ex) when (ex is AtlasStorageException or IOException or UnauthorizedAccessException)
        {
            TryLog(services, ex);
            services?.Dispose();
            return new StartupOutcome(null, Describe(ex, paths));
        }
    }

    private static string Describe(Exception exception, AtlasDataPaths paths)
    {
        var (message, location) = exception switch
        {
            DatabaseTooNewException e => (Strings.DatabaseTooNewMessage, e.DatabasePath),
            ForeignDatabaseException e => (Strings.ForeignDatabaseMessage, e.DatabasePath),
            DatabaseMigrationException e => (Strings.DatabaseMigrationFailedMessage, e.DatabasePath),
            DatabaseUnreadableException e => (Strings.DatabaseUnreadableMessage, e.DatabasePath),
            _ => (Strings.DataFolderUnavailableMessage, paths.DataDirectory),
        };

        return UiText.Format(message, location);
    }

    private static void TryLog(ServiceProvider? services, Exception exception)
    {
        try
        {
            services?.GetRequiredService<ILoggerFactory>().CreateLogger("Atlas.Startup").LogError(exception, "Atlas could not start");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log folder itself is unavailable. The window shown to the user still explains the problem.
        }
    }
}
