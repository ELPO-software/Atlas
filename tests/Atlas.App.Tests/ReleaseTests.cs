using System.Reflection;
using System.Text.RegularExpressions;
using Atlas.App.About;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Persistence;
using Atlas.Platform;

namespace Atlas.App.Tests;

/// <summary>What a published Atlas promises: it works offline and says what it ships with.</summary>
public sealed partial class ReleaseTests
{
    private static readonly Assembly[] AtlasAssemblies =
    [
        typeof(Workspace).Assembly,
        typeof(SqliteDatabase).Assembly,
        typeof(SingleInstanceGuard).Assembly,
        typeof(AtlasApp).Assembly,
    ];

    [Theory]
    [InlineData("System.Net.Http")]
    [InlineData("System.Net.Sockets")]
    [InlineData("System.Net.Requests")]
    [InlineData("System.Net.WebClient")]
    [InlineData("System.Net.WebSockets")]
    [InlineData("System.Net.Mail")]
    [InlineData("System.Net.NetworkInformation")]
    public void NoAtlasCodeCanReachTheNetwork(string networkLibrary)
    {
        foreach (var assembly in AtlasAssemblies)
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name);
            Assert.DoesNotContain(networkLibrary, references);
        }
    }

    [Fact]
    public void Notices_NameEveryComponentShownInTheSettings()
    {
        var notices = File.ReadAllText(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"));

        Assert.All(ThirdPartyComponents.All, component =>
            Assert.True(notices.Contains(component.Name, StringComparison.Ordinal), $"{component.Name} is missing from THIRD-PARTY-NOTICES.md."));
    }

    [Fact]
    public void Notices_LinkOnlyToLicenseFilesThatExist()
    {
        var root = RepositoryRoot();
        var links = LicenseLink().Matches(File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"))).Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.True(File.Exists(Path.Combine(root, "licenses", link)), $"licenses/{link} does not exist."));
    }

    [Fact]
    public void Notices_LinkToEveryLicenseFileInTheFolder()
    {
        var root = RepositoryRoot();
        var notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));

        Assert.All(Directory.GetFiles(Path.Combine(root, "licenses")), file =>
            Assert.Contains($"licenses/{Path.GetFileName(file)}", notices, StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Atlas.slnx")))
                return folder.FullName;
        }

        throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    [GeneratedRegex(@"\(licenses/([^)]+)\)")]
    private static partial Regex LicenseLink();
}
