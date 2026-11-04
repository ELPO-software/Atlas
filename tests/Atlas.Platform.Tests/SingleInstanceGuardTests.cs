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
}
