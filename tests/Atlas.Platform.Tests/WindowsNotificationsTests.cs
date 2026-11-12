using System.Runtime.Versioning;
using Atlas.Platform.Windows.Notifications;

namespace Atlas.Platform.Tests;

/// <summary>Only an identity Windows does not know is used here, so that no test ever shows a notification.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNotificationsTests
{
    private const string UnknownIdentity = "ELPO.Atlas.Tests.NotRegistered";

    [Fact]
    public void AnIdentityWindowsDoesNotKnowCannotNotify()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var notifications = new WindowsNotifications(isInstalledCopy: true, UnknownIdentity);

        Assert.False(notifications.IsAvailable);
        Assert.False(notifications.TryAdoptApplicationIdentity());
    }

    [Fact]
    public void ACopyThatWasNotInstalledNeverNotifies()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

        // Even under the real identity, which Windows may remember from an earlier installation.
        var notifications = new WindowsNotifications(isInstalledCopy: false);

        Assert.False(notifications.IsAvailable);
        Assert.False(notifications.Show(new Atlas.Core.Notifications.DesktopNotification("atlas-test", "Titre", "Message", [])));
    }

    [Theory]
    [InlineData(@"C:\Program Files\ELPO\Atlas\", @"C:\Program Files\ELPO\Atlas\", true)]
    [InlineData(@"C:\Program Files\ELPO\Atlas", @"c:\program files\elpo\atlas\", true)]
    [InlineData(@"C:\Program Files\ELPO\Atlas\", @"D:\Outils\Atlas\", false)]
    [InlineData(null, @"C:\Program Files\ELPO\Atlas\", false)]
    [InlineData("", @"C:\Program Files\ELPO\Atlas\", false)]
    public void TheInstalledCopyIsRecognizedByItsFolder(string? installLocation, string folder, bool expected) =>
        Assert.Equal(expected, Atlas.Platform.Windows.InstalledCopy.IsSameFolder(installLocation, folder));

    [Fact]
    public void RemovingWhatWasNeverShownIsHarmless()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var notifications = new WindowsNotifications(isInstalledCopy: true, UnknownIdentity);

        notifications.Remove("atlas-0000");
        notifications.RemoveAll();
    }
}
