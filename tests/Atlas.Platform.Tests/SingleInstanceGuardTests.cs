namespace Atlas.Platform.Tests;

public sealed class SingleInstanceGuardTests
{
    private static string UniqueName() => $@"Local\ELPO.Atlas.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void TheFirstInstanceIsRecognized()
    {
        using var guard = new SingleInstanceGuard(UniqueName());

        Assert.True(guard.IsFirstInstance);
    }

    [Fact]
    public void ASecondInstanceIsDetected()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);

        using var second = new SingleInstanceGuard(name);

        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void AnInstanceOnAnotherThreadIsDetected()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        bool? isFirst = null;

        var thread = new Thread(() =>
        {
            using var second = new SingleInstanceGuard(name);
            isFirst = second.IsFirstInstance;
        });
        thread.Start();
        thread.Join();

        Assert.False(isFirst);
    }

    [Fact]
    public void TheNameIsFreeAgainAfterTheInstanceEnds()
    {
        var name = UniqueName();
        new SingleInstanceGuard(name).Dispose();

        using var next = new SingleInstanceGuard(name);

        Assert.True(next.IsFirstInstance);
    }

    [Fact]
    public void TheDefaultNameIsLimitedToTheUserSession() =>
        Assert.StartsWith(@"Local\", SingleInstanceGuard.DefaultName, StringComparison.Ordinal);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TheDefaultDataFolderUsesTheDefaultName(string? customDirectory) =>
        Assert.Equal(SingleInstanceGuard.DefaultName, SingleInstanceGuard.NameFor(customDirectory));

    [Fact]
    public void EachDataFolderHasItsOwnName()
    {
        var first = SingleInstanceGuard.NameFor(@"C:\Essais\Atlas un");
        var second = SingleInstanceGuard.NameFor(@"C:\Essais\Atlas deux");

        Assert.NotEqual(first, second);
        Assert.NotEqual(SingleInstanceGuard.DefaultName, first);
        Assert.StartsWith(SingleInstanceGuard.DefaultName + ".", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Essais\Atlas")]
    [InlineData(@"c:\essais\ATLAS")]
    [InlineData(@"C:\Essais\Atlas\")]
    [InlineData(@"C:\Essais\.\Atlas")]
    public void TheSameFolderWrittenDifferentlyHasTheSameName(string spelling) =>
        Assert.Equal(SingleInstanceGuard.NameFor(@"C:\Essais\Atlas"), SingleInstanceGuard.NameFor(spelling));

    [Fact]
    public async Task ASecondStartBringsTheRunningOneForward()
    {
        var name = UniqueName();
        using var running = new SingleInstanceGuard(name);
        var activated = new TaskCompletionSource();
        running.ListenForActivation(() => activated.TrySetResult());

        using var second = new SingleInstanceGuard(name);
        Assert.True(second.ActivateFirstInstance());

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARequestMadeBeforeListeningIsNotLost()
    {
        var name = UniqueName();
        using var running = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);
        second.ActivateFirstInstance();

        var activated = new TaskCompletionSource();
        running.ListenForActivation(() => activated.TrySetResult());

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EachStartIsDeliveredOnce()
    {
        var name = UniqueName();
        using var running = new SingleInstanceGuard(name);
        var count = 0;
        var twice = new TaskCompletionSource();
        running.ListenForActivation(() =>
        {
            if (Interlocked.Increment(ref count) == 2)
                twice.TrySetResult();
        });

        using (var second = new SingleInstanceGuard(name))
            second.ActivateFirstInstance();
        await WaitUntilAsync(() => Volatile.Read(ref count) == 1);
        using (var third = new SingleInstanceGuard(name))
            third.ActivateFirstInstance();

        await twice.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref count));
    }

    [Fact]
    public void EveryRunningInstanceHoldsTheNameTheInstallerWaitsFor()
    {
        using var guard = new SingleInstanceGuard(SingleInstanceGuard.NameFor(@"C:\Essais\Atlas autre"));

        Assert.True(Mutex.TryOpenExisting(SingleInstanceGuard.RunningName, out var running));
        running.Dispose();
    }

    [Fact]
    public void OnlyTheRunningInstanceListens()
    {
        var name = UniqueName();
        using var running = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.Throws<InvalidOperationException>(() => second.ListenForActivation(() => { }));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
    }
}
