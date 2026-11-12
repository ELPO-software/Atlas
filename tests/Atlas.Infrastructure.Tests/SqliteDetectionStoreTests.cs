using Atlas.Core.Detection;
using Atlas.Core.Resources;
using Atlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Infrastructure.Tests;

public sealed class SqliteDetectionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 12, 9, 30, 0, TimeSpan.FromHours(1));

    private readonly TemporaryFolder _folder = new();
    private readonly SqliteDetectionStore _store;

    public SqliteDetectionStoreTests()
    {
        var database = new SqliteDatabase(_folder.DatabasePath);
        new DatabaseMigrator(database, TimeProvider.System, NullLogger<DatabaseMigrator>.Instance).Migrate();
        _store = new SqliteDetectionStore(database);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private static StoredProposal Proposal(string key = "project:C:\\USERS\\LÉA\\ATLAS", ProposalStatus status = ProposalStatus.Pending) =>
        new(
            new EnvironmentProposal(
                key,
                ProposalKind.Project,
                "Atlas d'été",
                DetectionConfidence.Detected,
                [
                    new ProposedResource(ResourceKind.Folder, "Atlas d'été", @"C:\Users\Léa\Atlas", ProposalReason.ProjectFolder),
                    new ProposedResource(ResourceKind.Application, "Visual Studio Code", @"C:\Programmes\Code.exe", ProposalReason.OpenInApplication),
                    new ProposedResource(ResourceKind.Url, "github.com/ELPO-software/Atlas", "https://github.com/ELPO-software/Atlas", ProposalReason.Repository),
                ]),
            status,
            Seen,
            Seen.AddMinutes(4),
            2,
            new DateOnly(2026, 11, 12),
            Seen.AddHours(8),
            Seen.AddMinutes(2),
            Guid.Parse("8b9c1a52-3f0e-4c1d-9b55-2f0f3d6e7a10"));

    [Fact]
    public async Task AProposalIsReadBackAsItWasSaved()
    {
        var proposal = Proposal();

        await _store.SaveAsync(proposal, Token);
        var read = Assert.Single(await _store.ListAsync(Token));

        Assert.Equal(proposal.Proposal.Key, read.Proposal.Key);
        Assert.Equal(proposal.Proposal.Title, read.Proposal.Title);
        Assert.Equal(proposal.Proposal.Resources, read.Proposal.Resources);
        Assert.Equal(proposal with { Proposal = read.Proposal }, read);
    }

    [Fact]
    public async Task SavingAgainReplacesTheProposal()
    {
        await _store.SaveAsync(Proposal(), Token);

        await _store.SaveAsync(Proposal(status: ProposalStatus.Ignored) with { SnoozedUntil = null, WorkspaceId = null }, Token);

        var read = Assert.Single(await _store.ListAsync(Token));
        Assert.Equal(ProposalStatus.Ignored, read.Status);
        Assert.Null(read.SnoozedUntil);
        Assert.Null(read.WorkspaceId);
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheGivenKeys()
    {
        await _store.SaveAsync(Proposal("project:A"), Token);
        await _store.SaveAsync(Proposal("project:B"), Token);
        await _store.SaveAsync(Proposal("context:C"), Token);

        await _store.DeleteAsync(["project:A", "context:C", "absent"], Token);

        Assert.Equal("project:B", Assert.Single(await _store.ListAsync(Token)).Key);
    }

    [Theory]
    [InlineData("status", "'archived'")]
    [InlineData("kind", "'meeting'")]
    [InlineData("confidence", "'certain'")]
    [InlineData("resources", "'[{\"Kind\":\"folder\",\"Name\":\"x\",\"Target\":\"C:\\\\x\",\"Reason\":\"telepathy\"}]'")]
    [InlineData("resources", "'not json'")]
    public async Task ARowFromANewerVersionStaysInTheFileButIsNotShown(string column, string value)
    {
        await _store.SaveAsync(Proposal("project:A"), Token);
        await _store.SaveAsync(Proposal("project:B"), Token);

        using (var connection = new SqliteConnection($"Data Source={_folder.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE detected_environments SET {column} = {value} WHERE key = 'project:A'";
            command.ExecuteNonQuery();
        }

        Assert.Equal("project:B", Assert.Single(await _store.ListAsync(Token)).Key);
    }
}
