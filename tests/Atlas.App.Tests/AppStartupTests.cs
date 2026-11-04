using Atlas.App.Composition;
using Atlas.App.Resources;
using Atlas.App.ViewModels;
using Atlas.Core.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.App.Tests;

public sealed class AppStartupTests : IDisposable
{
    private readonly TemporaryDataFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Run_CreatesTheDatabaseAndAWorkingServiceGraph()
    {
        var outcome = AppStartup.Run(_folder.Paths);

        using var services = outcome.Services;
        Assert.NotNull(services);
        Assert.Null(outcome.FailureMessage);
        Assert.True(File.Exists(_folder.Paths.DatabasePath));
        Assert.NotNull(services.GetRequiredService<MainWindowViewModel>());
        Assert.IsType<WorkspaceService>(services.GetRequiredService<WorkspaceService>());
    }

    [Fact]
    public void Run_WritesItsLogInTheDataFolder()
    {
        using (AppStartup.Run(_folder.Paths).Services)
        {
        }

        Assert.NotEmpty(Directory.GetFiles(_folder.Paths.LogsDirectory, "atlas-*.log"));
    }

    [Fact]
    public void Run_ExplainsADatabaseFromANewerVersionAndLeavesItUntouched()
    {
        using (AppStartup.Run(_folder.Paths).Services)
        {
        }

        using (var connection = new SqliteConnection($"Data Source={_folder.Paths.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999";
            command.ExecuteNonQuery();
        }

        var before = File.ReadAllBytes(_folder.Paths.DatabasePath);

        var outcome = AppStartup.Run(_folder.Paths);

        Assert.Null(outcome.Services);
        Assert.Contains(_folder.Paths.DatabasePath, outcome.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("version plus récente", outcome.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_folder.Paths.DatabasePath));
    }

    [Fact]
    public void Run_ExplainsAnUnreadableFileAndNeverReplacesIt()
    {
        const string Content = "Ceci n'est pas une base de données, mais un fichier suffisamment long pour être lu comme tel.";
        File.WriteAllText(_folder.Paths.DatabasePath, Content);

        var outcome = AppStartup.Run(_folder.Paths);

        Assert.Null(outcome.Services);
        Assert.Contains(_folder.Paths.DatabasePath, outcome.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(Content, File.ReadAllText(_folder.Paths.DatabasePath));
    }

    [Fact]
    public void Run_ExplainsADataFolderThatCannotBeCreated()
    {
        var blocker = Path.Combine(_folder.Paths.DataDirectory, "fichier");
        File.WriteAllText(blocker, "Un fichier occupe la place du dossier.");
        var paths = new Atlas.Infrastructure.Storage.AtlasDataPaths(Path.Combine(blocker, "Atlas"));

        var outcome = AppStartup.Run(paths);

        Assert.Null(outcome.Services);
        Assert.Contains(paths.DataDirectory, outcome.FailureMessage, StringComparison.Ordinal);
        Assert.StartsWith(Strings.DataFolderUnavailableMessage[..20], outcome.FailureMessage, StringComparison.Ordinal);
    }
}
