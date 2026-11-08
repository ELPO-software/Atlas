namespace Atlas.Platform.Tests;

public sealed class TitleBarColorTests
{
    [Fact]
    public void TrySet_ReportsFailureForAWindowThatDoesNotExist() =>
        Assert.False(TitleBarColor.TrySet(nint.Zero, 0x09, 0x0B, 0x10));
}
