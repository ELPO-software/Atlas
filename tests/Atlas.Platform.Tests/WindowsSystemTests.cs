using System.Runtime.Versioning;
using Atlas.Core.Resources;
using Atlas.Platform.Windows;

namespace Atlas.Platform.Tests;

/// <summary>Reads from the real system. Nothing here opens or starts anything.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemTests
{
    [Theory]
    [InlineData(".exe", true)]
    [InlineData(".bat", true)]
    [InlineData(".lnk", true)]
    [InlineData(".txt", false)]
    [InlineData(".pdf", false)]
    public void Shell_KnowsWhichFileTypesWindowsConsidersDangerous(string extension, bool expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

        Assert.Equal(expected, new WindowsShell().IsDangerousFileType(extension));
    }

    [Fact]
    public void Shell_ReportsAnApplicationThatIsNotInstalled()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

        Assert.False(new WindowsShell().InstalledApplicationExists("Contoso.DoesNotExist_8wekyb3d8bbwe!App"));
    }

    [Fact]
    public async Task Catalog_ListsInstalledApplicationsThatAtlasCanStore()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

        var applications = await new ShellApplicationCatalog().ListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(applications);
        Assert.All(applications, application =>
        {
            Assert.False(string.IsNullOrWhiteSpace(application.Name));
            Assert.Null(ResourceRules.CheckTarget(ResourceKind.Application, application.Target));
        });
        Assert.Equal(applications.Count, applications.Select(a => a.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Catalog_ListsOnlyApplicationsTheSystemCanFind()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var shell = new WindowsShell();

        var applications = await new ShellApplicationCatalog().ListAsync(TestContext.Current.CancellationToken);

        Assert.All(applications.Take(10), application =>
        {
            Assert.True(ResourceTargets.TryGetInstalledApplicationId(application.Target, out var applicationId));
            Assert.True(shell.InstalledApplicationExists(applicationId), application.Name);
        });
    }

    [Fact]
    public async Task UnsupportedOpener_SaysItCannotOpenInsteadOfPretending()
    {
        var result = await new UnsupportedResourceOpener().OpenAsync(ResourceKind.File, @"C:\a\notes.txt");

        Assert.Equal(Core.Restoration.RestoreStatus.Failed, result.Status);
    }
}
