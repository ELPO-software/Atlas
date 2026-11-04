using Atlas.App.ViewModels;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.App.Tests;

public sealed class MainWindowViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, "0 espace de travail")]
    [InlineData(1, "1 espace de travail")]
    [InlineData(2, "2 espaces de travail")]
    public async Task LoadAsync_ShowsTheCountWithTheRightPlural(int count, string expected)
    {
        var viewModel = CreateViewModel(new StubRepository(count));

        await viewModel.LoadAsync(Token);

        Assert.Equal(expected, viewModel.WorkspaceCountText);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheCountCannotBeRead()
    {
        var viewModel = CreateViewModel(new StubRepository(new IOException("Disque indisponible")));

        await viewModel.LoadAsync(Token);

        Assert.Contains("Impossible de lire", viewModel.WorkspaceCountText, StringComparison.Ordinal);
        Assert.Contains("journaux-test", viewModel.WorkspaceCountText, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceCountText_IsAnnouncedAsLoadingBeforeTheFirstRead() =>
        Assert.Equal("Chargement…", CreateViewModel(new StubRepository(0)).WorkspaceCountText);

    private static MainWindowViewModel CreateViewModel(IWorkspaceRepository repository) =>
        new(
            new WorkspaceService(repository, TimeProvider.System),
            new AtlasDataPaths(Path.Combine(Path.GetTempPath(), "journaux-test")),
            NullLogger<MainWindowViewModel>.Instance);

    private sealed class StubRepository : IWorkspaceRepository
    {
        private readonly int _count;
        private readonly Exception? _failure;

        public StubRepository(int count) => _count = count;

        public StubRepository(Exception failure) => _failure = failure;

        public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            _failure is null ? Task.FromResult(_count) : Task.FromException<int>(_failure);

        public Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
