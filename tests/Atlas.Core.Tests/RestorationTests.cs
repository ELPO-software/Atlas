using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Core.Workspaces;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Core.Tests;

public sealed class RestorationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeOpener _opener = new();
    private readonly RestorationService _service;

    public RestorationTests() => _service = new RestorationService(_opener, _clock);

    [Fact]
    public void Plan_OpensApplicationsFirstThenFilesAndFoldersThenLinks()
    {
        var workspace = Workspace.Create("Études", null, Now);
        workspace.AddResource(ResourceKind.Url, "Bibliothèque", "https://example.org", Now);
        workspace.AddResource(ResourceKind.Folder, "Sources", @"C:\Sources", Now);
        workspace.AddResource(ResourceKind.Application, "Éditeur", @"C:\Programmes\Éditeur.exe", Now);
        workspace.AddResource(ResourceKind.File, "Mémoire", @"C:\Sources\mémoire.docx", Now);
        workspace.AddResource(ResourceKind.Parse("carte-mentale"), "Plan", "plan.mm", Now);

        var plan = RestorePlan.For(workspace);

        Assert.Equal(["Éditeur", "Sources", "Mémoire", "Bibliothèque", "Plan"], plan.Steps.Select(s => s.Resource.DisplayName));
    }

    [Fact]
    public void Plan_ExplainsWhatItWillNotOpen()
    {
        var workspace = Workspace.Restore(
            Guid.NewGuid(),
            "Importé",
            string.Empty,
            string.Empty,
            Now,
            Now,
            null,
            [
                Resource(ResourceKind.Application, @"\\serveur\outils\outil.exe", requiresReview: true),
                Resource(ResourceKind.File, @"C:\a\lancer.bat."),
                Resource(ResourceKind.Url, "javascript:alert(1)"),
                Resource(ResourceKind.Parse("carte-mentale"), "plan.mm"),
                Resource(ResourceKind.File, @"C:\a\notes.txt"),
            ]);

        var plan = RestorePlan.For(workspace);

        Assert.Equal(
            [RestoreReason.NeedsApproval, RestoreReason.BlockedFileType, null, RestoreReason.InvalidTarget, RestoreReason.UnknownKind],
            plan.Steps.Select(s => s.SkipReason));
    }

    [Fact]
    public void Plan_ListsOnlyTheProgramsThatWillReallyStart()
    {
        var workspace = Workspace.Restore(
            Guid.NewGuid(),
            "Espace",
            string.Empty,
            string.Empty,
            Now,
            Now,
            null,
            [
                Resource(ResourceKind.Application, @"C:\a\un.exe"),
                Resource(ResourceKind.Application, @"C:\a\deux.exe", requiresReview: true),
                Resource(ResourceKind.Application, @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
                Resource(ResourceKind.File, @"C:\a\notes.txt"),
            ]);

        var plan = RestorePlan.For(workspace);

        Assert.Equal([@"C:\a\un.exe", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"], plan.ApplicationsToLaunch.Select(r => r.Target));
    }

    [Fact]
    public async Task Run_ReportsWhatTheSystemAnsweredForEachResource()
    {
        _opener.Answers[@"C:\a\absent.txt"] = OpenResult.NotFound();
        _opener.Answers[@"C:\a\sans-application.xyz"] = OpenResult.Failed(RestoreReason.NoAssociatedApplication);

        var report = await Run(Plan(
            (ResourceKind.Application, @"C:\a\outil.exe"),
            (ResourceKind.File, @"C:\a\absent.txt"),
            (ResourceKind.File, @"C:\a\sans-application.xyz"),
            (ResourceKind.Url, "https://example.org")));

        Assert.Equal(
            [RestoreStatus.Restored, RestoreStatus.Missing, RestoreStatus.Failed, RestoreStatus.Restored],
            report.Steps.Select(s => s.Status));
        Assert.Equal(2, report.Count(RestoreStatus.Restored));
        Assert.False(report.WasCancelled);
    }

    [Fact]
    public async Task Run_NeverAsksTheSystemToOpenWhatThePlanSkips()
    {
        var plan = RestorePlan.For(Workspace.Restore(
            Guid.NewGuid(),
            "Espace",
            string.Empty,
            string.Empty,
            Now,
            Now,
            null,
            [
                Resource(ResourceKind.File, @"\\serveur\partage\notes.txt", requiresReview: true),
                Resource(ResourceKind.File, @"C:\a\lancer.bat"),
                Resource(ResourceKind.File, @"C:\a\notes.txt"),
            ]));

        var report = await Run(plan);

        Assert.Equal([@"C:\a\notes.txt"], _opener.Opened);
        Assert.Equal([RestoreStatus.Skipped, RestoreStatus.Skipped, RestoreStatus.Restored], report.Steps.Select(s => s.Status));
        Assert.Equal(RestoreReason.NeedsApproval, report.Steps[0].Reason);
    }

    [Fact]
    public async Task Run_KeepsGoingAfterAResourceThrows()
    {
        _opener.Failures[@"C:\a\un.txt"] = new IOException("Le périphérique n'est pas prêt.");

        var report = await Run(Plan((ResourceKind.File, @"C:\a\un.txt"), (ResourceKind.File, @"C:\a\deux.txt")));

        Assert.Equal(RestoreStatus.Failed, report.Steps[0].Status);
        Assert.Equal(RestoreReason.SystemRefused, report.Steps[0].Reason);
        Assert.Equal("Le périphérique n'est pas prêt.", report.Steps[0].SystemMessage);
        Assert.Equal(RestoreStatus.Restored, report.Steps[1].Status);
    }

    [Fact]
    public async Task Run_GivesUpOnAResourceThatDoesNotAnswerAndGoesOn()
    {
        _opener.Hangs.Add(@"\\serveur\lent\notes.txt");

        var running = Run(Plan((ResourceKind.File, @"\\serveur\lent\notes.txt"), (ResourceKind.File, @"C:\a\deux.txt")));
        await WaitUntilAsync(() => _opener.Opened.Count == 1);
        _clock.Advance(RestorationService.StepTimeout);
        var report = await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(RestoreReason.TimedOut, report.Steps[0].Reason);
        Assert.Equal(RestoreStatus.Failed, report.Steps[0].Status);
        Assert.Equal(RestoreStatus.Restored, report.Steps[1].Status);
    }

    [Fact]
    public async Task Run_StopsBeforeTheNextResourceWhenAsked()
    {
        using var stop = new CancellationTokenSource();
        _opener.OnOpen = target =>
        {
            if (target == @"C:\a\un.txt")
                stop.Cancel();
        };

        var report = await _service.RunAsync(
            Plan((ResourceKind.File, @"C:\a\un.txt"), (ResourceKind.File, @"C:\a\deux.txt"), (ResourceKind.Url, "https://example.org")),
            stop: stop.Token);

        Assert.Equal([@"C:\a\un.txt"], _opener.Opened);
        Assert.Equal(RestoreStatus.Restored, report.Steps[0].Status);
        Assert.All(report.Steps.Skip(1), s => Assert.Equal(RestoreReason.Cancelled, s.Reason));
        Assert.True(report.WasCancelled);
    }

    [Fact]
    public async Task Run_ReportsAProgramThatStoppedRightAfterStartingAsFailed()
    {
        _opener.Answers[@"C:\a\fragile.exe"] = OpenResult.Launched(
            Task.FromResult(OpenResult.Failed(RestoreReason.ExitedImmediately, "3")));

        var report = await Run(Plan((ResourceKind.Application, @"C:\a\fragile.exe")));

        var step = Assert.Single(report.Steps);
        Assert.Equal(RestoreStatus.Failed, step.Status);
        Assert.Equal(RestoreReason.ExitedImmediately, step.Reason);
        Assert.Equal("3", step.SystemMessage);
    }

    [Fact]
    public async Task Run_StartsEveryProgramBeforeWaitingToSeeIfOneFails()
    {
        var firstCheck = new TaskCompletionSource<OpenResult>();
        _opener.Answers[@"C:\a\un.exe"] = OpenResult.Launched(firstCheck.Task);

        var running = Run(Plan((ResourceKind.Application, @"C:\a\un.exe"), (ResourceKind.Application, @"C:\a\deux.exe")));
        await WaitUntilAsync(() => _opener.Opened.Count == 2);

        Assert.False(running.IsCompleted);
        firstCheck.SetResult(OpenResult.Launched());
        var report = await running;
        Assert.All(report.Steps, s => Assert.Equal(RestoreStatus.Restored, s.Status));
    }

    [Fact]
    public async Task Run_KeepsAProgramAsLaunchedWhenItsCheckCannotConclude()
    {
        _opener.Answers[@"C:\a\un.exe"] = OpenResult.Launched(Task.FromException<OpenResult>(new InvalidOperationException()));

        var report = await Run(Plan((ResourceKind.Application, @"C:\a\un.exe")));

        Assert.Equal(RestoreReason.Launched, Assert.Single(report.Steps).Reason);
    }

    [Fact]
    public async Task Run_ReportsProgressForEveryResourceAndTheEnd()
    {
        var progress = new List<RestoreProgress>();

        await Run(
            Plan((ResourceKind.File, @"C:\a\un.txt"), (ResourceKind.File, @"C:\a\deux.txt")),
            new SynchronousProgress(progress.Add));

        Assert.Equal([0, 1, 2], progress.Select(p => p.Completed));
        Assert.All(progress, p => Assert.Equal(2, p.Total));
        Assert.Null(progress[^1].Current);
    }

    [Fact]
    public async Task Run_DatesTheReport()
    {
        var report = await Run(Plan((ResourceKind.File, @"C:\a\un.txt")));

        Assert.Equal(Now, report.StartedAt);
        Assert.Equal(Now, report.FinishedAt);
    }

    [Fact]
    public async Task Run_OfAnEmptyWorkspaceReportsNothing()
    {
        var report = await Run(RestorePlan.For(Workspace.Create("Vide", null, Now)));

        Assert.Empty(report.Steps);
    }

    /// <summary>Runs without a stop request, except the one that ends the test run.</summary>
    private Task<RestoreReport> Run(RestorePlan plan, IProgress<RestoreProgress>? progress = null) =>
        _service.RunAsync(plan, progress, TestContext.Current.CancellationToken);

    private static RestorePlan Plan(params (ResourceKind Kind, string Target)[] resources)
    {
        var workspace = Workspace.Create("Espace", null, Now);
        foreach (var (kind, target) in resources)
            workspace.AddResource(kind, Path.GetFileName(target) is { Length: > 0 } name ? name : target, target, Now);

        return RestorePlan.For(workspace);
    }

    private static Resource Resource(ResourceKind kind, string target, bool requiresReview = false) =>
        new(Guid.NewGuid(), kind, target, target, 0, requiresReview, Now, Now);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition());
    }

    private sealed class SynchronousProgress(Action<RestoreProgress> report) : IProgress<RestoreProgress>
    {
        public void Report(RestoreProgress value) => report(value);
    }

    private sealed class FakeOpener : IResourceOpener
    {
        private readonly object _gate = new();
        private readonly List<string> _opened = [];

        public Dictionary<string, OpenResult> Answers { get; } = [];

        public Dictionary<string, Exception> Failures { get; } = [];

        public HashSet<string> Hangs { get; } = [];

        public Action<string>? OnOpen { get; set; }

        public IReadOnlyList<string> Opened
        {
            get
            {
                lock (_gate)
                    return _opened.ToList();
            }
        }

        public Task<OpenResult> OpenAsync(ResourceKind kind, string target)
        {
            lock (_gate)
                _opened.Add(target);

            OnOpen?.Invoke(target);

            if (Hangs.Contains(target))
                return new TaskCompletionSource<OpenResult>().Task;

            if (Failures.TryGetValue(target, out var failure))
                throw failure;

            return Task.FromResult(Answers.GetValueOrDefault(target)
                ?? (kind == ResourceKind.Application ? OpenResult.Launched() : OpenResult.Opened()));
        }
    }
}
