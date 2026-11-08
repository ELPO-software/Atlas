using System.ComponentModel;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Platform.Windows;

namespace Atlas.Platform.Tests;

public sealed class WindowsResourceOpenerTests
{
    private const string Calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private readonly FakeShell _shell = new();
    private readonly WindowsResourceOpener _opener;

    public WindowsResourceOpenerTests() => _opener = new WindowsResourceOpener(_shell);

    [Fact]
    public async Task Program_IsStartedFromItsFolderWithoutArguments()
    {
        _shell.Files.Add(@"C:\Programmes\Éditeur d'été\Éditeur.exe");

        var result = await _opener.OpenAsync(ResourceKind.Application, @"C:\Programmes\Éditeur d'été\Éditeur.exe");

        Assert.Equal(RestoreReason.Launched, result.Reason);
        Assert.Equal((@"C:\Programmes\Éditeur d'été\Éditeur.exe", @"C:\Programmes\Éditeur d'été"), Assert.Single(_shell.Opened));
    }

    [Fact]
    public async Task Program_ThatIsMissingIsNotStarted()
    {
        var result = await _opener.OpenAsync(ResourceKind.Application, @"C:\Programmes\absent.exe");

        Assert.Equal(RestoreStatus.Missing, result.Status);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Program_WhosePathIsAFolderIsNotStarted()
    {
        _shell.Folders.Add(@"C:\Programmes\outil.exe");

        var result = await _opener.OpenAsync(ResourceKind.Application, @"C:\Programmes\outil.exe");

        Assert.Equal(RestoreReason.WrongType, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Theory]
    [InlineData(@"C:\Outils\lancer.bat")]
    [InlineData(@"C:\Outils\script.ps1")]
    [InlineData("outil.exe")]
    public async Task Program_ThatIsNotAProgramIsNeverStarted(string target)
    {
        _shell.Files.Add(target);

        var result = await _opener.OpenAsync(ResourceKind.Application, target);

        Assert.Equal(RestoreReason.InvalidTarget, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Program_StillRunningAfterTheCheckIsLaunched()
    {
        _shell.Files.Add(@"C:\a\outil.exe");
        _shell.NextProcess = new FakeProcess(exitCode: null);

        var result = await _opener.OpenAsync(ResourceKind.Application, @"C:\a\outil.exe");

        Assert.NotNull(result.Confirmation);
        var confirmed = await result.Confirmation;
        Assert.Equal(RestoreReason.Launched, confirmed.Reason);
        Assert.Equal(WindowsResourceOpener.EarlyExitWindow, _shell.NextProcess.WaitedFor);
        Assert.True(_shell.NextProcess.IsDisposed);
    }

    [Fact]
    public async Task Program_StoppingWithAnErrorRightAwayIsAFailure()
    {
        _shell.Files.Add(@"C:\a\outil.exe");
        _shell.NextProcess = new FakeProcess(exitCode: 3);

        var confirmed = await (await _opener.OpenAsync(ResourceKind.Application, @"C:\a\outil.exe")).Confirmation!;

        Assert.Equal(RestoreStatus.Failed, confirmed.Status);
        Assert.Equal(RestoreReason.ExitedImmediately, confirmed.Reason);
        Assert.Equal("3", confirmed.SystemMessage);
    }

    [Fact]
    public async Task Program_HandingOverToARunningCopyIsLaunched()
    {
        _shell.Files.Add(@"C:\a\outil.exe");
        _shell.NextProcess = new FakeProcess(exitCode: 0);

        var confirmed = await (await _opener.OpenAsync(ResourceKind.Application, @"C:\a\outil.exe")).Confirmation!;

        Assert.Equal(RestoreReason.Launched, confirmed.Reason);
    }

    [Fact]
    public async Task InstalledApplication_IsStartedThroughTheApplicationList()
    {
        _shell.Applications.Add(Calculator);

        var result = await _opener.OpenAsync(ResourceKind.Application, ResourceTargets.ForInstalledApplication(Calculator));

        Assert.Equal(RestoreReason.Launched, result.Reason);
        Assert.Null(result.Confirmation);
        Assert.Equal((ResourceTargets.ForInstalledApplication(Calculator), (string?)null), Assert.Single(_shell.Opened));
    }

    [Fact]
    public async Task InstalledApplication_ThatWasRemovedIsReportedMissing()
    {
        var result = await _opener.OpenAsync(ResourceKind.Application, ResourceTargets.ForInstalledApplication(Calculator));

        Assert.Equal(RestoreStatus.Missing, result.Status);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task InstalledApplication_WithAnIdentifierThatIsAWebAddressIsNotOpened()
    {
        var result = await _opener.OpenAsync(ResourceKind.Application, @"shell:AppsFolder\http://example.org");

        Assert.Equal(RestoreReason.InvalidTarget, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task File_IsOpenedWithItsApplication()
    {
        _shell.Files.Add(@"C:\Données d'été\mémoire.docx");

        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\Données d'été\mémoire.docx");

        Assert.Equal(RestoreReason.Opened, result.Reason);
        Assert.Equal((@"C:\Données d'été\mémoire.docx", @"C:\Données d'été"), Assert.Single(_shell.Opened));
    }

    [Theory]
    [InlineData(@"C:\a\lancer.bat.")]
    [InlineData(@"C:\a\script.py")]
    [InlineData(@"C:\a\inconnu.dangereux")]
    public async Task File_ThatWouldRunCodeIsNeverOpened(string target)
    {
        _shell.Files.Add(target);
        _shell.DangerousExtensions.Add(".dangereux");

        var result = await _opener.OpenAsync(ResourceKind.File, target);

        Assert.Equal(RestoreStatus.Skipped, result.Status);
        Assert.Equal(RestoreReason.BlockedFileType, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task File_ThatIsMissingIsReportedMissing()
    {
        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\a\absent.txt");

        Assert.Equal(RestoreStatus.Missing, result.Status);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task File_ThatIsAFolderIsNotOpened()
    {
        _shell.Folders.Add(@"C:\a\notes.txt");

        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\a\notes.txt");

        Assert.Equal(RestoreReason.WrongType, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task File_InAnAlternateStreamIsNeverOpened()
    {
        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\a\notes.txt:caché.exe");

        Assert.Equal(RestoreReason.InvalidTarget, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Folder_IsOpenedAsAFolderOnly()
    {
        _shell.Folders.Add(@"C:\Données d'été");

        var result = await _opener.OpenAsync(ResourceKind.Folder, @"C:\Données d'été\");

        Assert.Equal(RestoreReason.Opened, result.Reason);
        Assert.Equal((@"C:\Données d'été\", (string?)null), Assert.Single(_shell.Opened));
    }

    [Fact]
    public async Task Folder_GetsAFinalSeparatorSoThatOnlyAFolderCanAnswer()
    {
        _shell.Folders.Add(@"\\serveur\partage commun\Année 2026");

        await _opener.OpenAsync(ResourceKind.Folder, @"\\serveur\partage commun\Année 2026");

        Assert.Equal(@"\\serveur\partage commun\Année 2026\", Assert.Single(_shell.Opened).Target);
    }

    [Fact]
    public async Task Folder_ThatIsAFileIsNeverOpened()
    {
        _shell.Files.Add(@"C:\a\outil.exe");

        var result = await _opener.OpenAsync(ResourceKind.Folder, @"C:\a\outil.exe");

        Assert.Equal(RestoreReason.WrongType, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Folder_ThatIsMissingIsReportedMissing()
    {
        var result = await _opener.OpenAsync(ResourceKind.Folder, @"\\serveur\partage\absent");

        Assert.Equal(RestoreStatus.Missing, result.Status);
    }

    [Fact]
    public async Task WebAddress_IsOpenedInItsNormalForm()
    {
        var result = await _opener.OpenAsync(ResourceKind.Url, "https://example.org/été?a=1");

        Assert.Equal(RestoreReason.Opened, result.Reason);
        Assert.Equal(("https://example.org/%C3%A9t%C3%A9?a=1", (string?)null), Assert.Single(_shell.Opened));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    public async Task WebAddress_OtherThanHttpIsNeverOpened(string target)
    {
        var result = await _opener.OpenAsync(ResourceKind.Url, target);

        Assert.Equal(RestoreReason.InvalidTarget, result.Reason);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task UnknownKind_IsSkipped() =>
        Assert.Equal(RestoreReason.UnknownKind, (await _opener.OpenAsync(ResourceKind.Parse("carte-mentale"), "plan.mm")).Reason);

    [Theory]
    [InlineData(2, RestoreStatus.Missing, RestoreReason.NotFound)]
    [InlineData(3, RestoreStatus.Missing, RestoreReason.NotFound)]
    [InlineData(1155, RestoreStatus.Failed, RestoreReason.NoAssociatedApplication)]
    [InlineData(1223, RestoreStatus.Failed, RestoreReason.SystemRefused)]
    public async Task SystemErrors_AreReportedHonestly(int code, RestoreStatus status, RestoreReason reason)
    {
        _shell.Files.Add(@"C:\a\fichier.xyz");
        _shell.Failure = new Win32Exception(code);

        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\a\fichier.xyz");

        Assert.Equal(status, result.Status);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task SystemErrors_KeepTheSystemExplanation()
    {
        _shell.Files.Add(@"C:\a\fichier.xyz");
        _shell.Failure = new Win32Exception(5, "Accès refusé.");

        var result = await _opener.OpenAsync(ResourceKind.File, @"C:\a\fichier.xyz");

        Assert.Equal("Accès refusé.", result.SystemMessage);
    }

    private sealed class FakeShell : IShell
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Applications { get; } = [];

        public HashSet<string> DangerousExtensions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Target, string? WorkingDirectory)> Opened { get; } = [];

        public FakeProcess? NextProcess { get; set; }

        public Exception? Failure { get; set; }

        public bool FileExists(string path) => Files.Contains(path);

        public bool DirectoryExists(string path) => Folders.Contains(Path.TrimEndingDirectorySeparator(path));

        public bool InstalledApplicationExists(string applicationId) => Applications.Contains(applicationId);

        public bool IsDangerousFileType(string extension) => DangerousExtensions.Contains(extension);

        public IStartedProcess? Open(string target, string? workingDirectory)
        {
            if (Failure is not null)
                throw Failure;

            Opened.Add((target, workingDirectory));
            return NextProcess;
        }
    }

    private sealed class FakeProcess(int? exitCode) : IStartedProcess
    {
        public TimeSpan? WaitedFor { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<int?> WaitForExitAsync(TimeSpan timeout)
        {
            WaitedFor = timeout;
            return Task.FromResult(exitCode);
        }

        public void Dispose() => IsDisposed = true;
    }
}
