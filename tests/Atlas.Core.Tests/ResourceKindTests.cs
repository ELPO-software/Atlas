using Atlas.Core.Resources;

namespace Atlas.Core.Tests;

public sealed class ResourceKindTests
{
    [Theory]
    [InlineData("application")]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("url")]
    [InlineData("future-kind2")]
    public void TryParse_AcceptsWellFormedNames(string value)
    {
        Assert.True(ResourceKind.TryParse(value, out var kind));
        Assert.Equal(value, kind.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Application")]
    [InlineData("2file")]
    [InlineData("a b")]
    [InlineData("../file")]
    [InlineData("fichié")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TryParse_RefusesMalformedNames(string? value)
    {
        Assert.False(ResourceKind.TryParse(value, out var kind));
        Assert.Equal(default, kind);
    }

    [Fact]
    public void Parse_ThrowsOnMalformedName() =>
        Assert.Throws<FormatException>(() => ResourceKind.Parse("Not Valid"));

    [Fact]
    public void Parse_ReturnsAValueEqualToTheKnownKind() =>
        Assert.Equal(ResourceKind.Url, ResourceKind.Parse("url"));

    [Fact]
    public void Default_HasAnEmptyTextForm() =>
        Assert.Equal(string.Empty, default(ResourceKind).ToString());
}
