using Atlas.Core.Resources;
using Atlas.Core.Validation;

namespace Atlas.Core.Tests;

public sealed class ResourceRulesTests
{
    [Theory]
    [InlineData("rapport.pdf", "pdf")]
    [InlineData(@"C:\Données d'été\Résumé final (v2).DOCX", "docx")]
    [InlineData(@"C:\dossier.avec.points\notes", "")]
    [InlineData(@"C:\a\script.bat.", "bat")]
    [InlineData(@"C:\a\script.bat . . ", "bat")]
    [InlineData(@"\\serveur\partage\outil.EXE", "exe")]
    public void ExtensionOf_IsWhatWindowsWillActOn(string path, string expected) =>
        Assert.Equal(expected, ResourceTargets.ExtensionOf(path));

    [Theory]
    [InlineData(@"C:\a\installer.exe")]
    [InlineData(@"C:\a\lancer.BAT")]
    [InlineData(@"C:\a\lancer.bat.")]
    [InlineData(@"C:\a\lancer.cmd  ")]
    [InlineData(@"C:\a\script.ps1")]
    [InlineData(@"C:\a\script.py")]
    [InlineData(@"C:\a\outil.jar")]
    [InlineData(@"C:\a\raccourci.lnk")]
    [InlineData(@"C:\a\site.url")]
    [InlineData(@"C:\a\aide.chm")]
    [InlineData(@"C:\a\page.hta")]
    [InlineData(@"C:\a\console.msc")]
    [InlineData(@"C:\a\réglages.reg")]
    [InlineData(@"C:\a\paquet.msi")]
    [InlineData(@"C:\a\écran.scr")]
    [InlineData(@"C:\a\macro.vbs")]
    public void RunsCode_RecognizesFilesThatWouldRunCode(string path) =>
        Assert.True(ResourceTargets.RunsCode(path));

    [Theory]
    [InlineData(@"C:\a\mémoire.docx")]
    [InlineData(@"C:\a\photo.png")]
    [InlineData(@"C:\a\notes.txt")]
    [InlineData(@"C:\a\page.html")]
    [InlineData(@"C:\a\archive.zip")]
    [InlineData(@"C:\a\exe")]
    [InlineData(@"C:\a\rapport.exe.pdf")]
    public void RunsCode_LetsDocumentsThrough(string path) =>
        Assert.False(ResourceTargets.RunsCode(path));

    [Theory]
    [InlineData(@"C:\Données d'été\mémoire.docx")]
    [InlineData(@"D:\")]
    [InlineData(@"\\serveur\partage commun\Année 2026")]
    public void IsUsablePath_AcceptsCompletePaths(string path) =>
        Assert.True(ResourceTargets.IsUsablePath(path));

    [Theory]
    [InlineData(@"mémoire.docx")]
    [InlineData(@"Documents\mémoire.docx")]
    [InlineData(@"\Documents\mémoire.docx")]
    [InlineData(@"C:mémoire.docx")]
    [InlineData(@"C:\notes.txt:caché.exe")]
    [InlineData(@"C:\notes.txt::$DATA")]
    [InlineData(@"\\serveur\partage\a:b")]
    [InlineData(@"\\?\C:\Windows\System32\cmd.exe")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"C:\a\*.txt")]
    [InlineData("C:\\a\\\"guillemets\".txt")]
    public void IsUsablePath_RefusesPathsWindowsWouldReadDifferently(string path) =>
        Assert.False(ResourceTargets.IsUsablePath(path));

    [Theory]
    [InlineData(@"\\serveur\partage", true)]
    [InlineData("//serveur/partage", true)]
    [InlineData(@"C:\partage", false)]
    public void IsNetworkPath_RecognizesSharedFolders(string path, bool expected) =>
        Assert.Equal(expected, ResourceTargets.IsNetworkPath(path));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\charmap.exe")]
    [InlineData(@"C:\ProgramData\Ableton\Live 12 Lite\Program\Ableton Live 12 Lite.exe")]
    [InlineData("308046B0AF4A39CB")]
    [InlineData("308046B0AF4A39CB;PrivateBrowsingAUMID")]
    [InlineData("4kdownload.com.4K YouTube to MP3")]
    public void IsInstalledApplicationId_AcceptsTheShapesWindowsUses(string applicationId) =>
        Assert.True(ResourceTargets.IsInstalledApplicationId(applicationId));

    [Theory]
    [InlineData("")]
    [InlineData("http://s.brother/ped11isimen")]
    [InlineData(@"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\appverif.chm")]
    [InlineData(@"C:\Programmes\lisez-moi.txt")]
    [InlineData("app\"injection")]
    public void IsInstalledApplicationId_RefusesEntriesThatAreNotApplications(string applicationId) =>
        Assert.False(ResourceTargets.IsInstalledApplicationId(applicationId));

    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"SHELL:appsfolder\Id", "Id")]
    public void TryGetInstalledApplicationId_ReadsTheIdentifier(string target, string expected)
    {
        Assert.True(ResourceTargets.TryGetInstalledApplicationId(target, out var applicationId));
        Assert.Equal(expected, applicationId);
    }

    [Theory]
    [InlineData("https://example.org/é?a=1&b=2#ancre")]
    [InlineData("http://localhost:8080/")]
    public void Links_AcceptWebAddresses(string target) =>
        Assert.Null(ResourceRules.CheckTarget(ResourceKind.Url, target));

    [Theory]
    [InlineData("example.org")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("ftp://example.org/")]
    [InlineData(@"\\serveur\partage")]
    [InlineData("https://")]
    public void Links_RefuseEverythingButWebAddresses(string target) =>
        Assert.Equal(ValidationCode.UnsupportedLink, ResourceRules.CheckTarget(ResourceKind.Url, target));

    [Fact]
    public void Files_AcceptDocumentsWithSpacesAndAccents() =>
        Assert.Null(ResourceRules.CheckTarget(ResourceKind.File, @"D:\Mes Documents\Résumé final (v2).pdf"));

    [Theory]
    [InlineData(@"C:\a\installer.exe")]
    [InlineData(@"C:\a\lancer.bat.")]
    [InlineData(@"C:\a\raccourci.lnk")]
    public void Files_RefuseFilesThatWouldRunCode(string target) =>
        Assert.Equal(ValidationCode.ExecutableFile, ResourceRules.CheckTarget(ResourceKind.File, target));

    [Theory]
    [InlineData(ResourceKindName.File, "notes.txt")]
    [InlineData(ResourceKindName.File, @"C:\notes.txt:caché.exe")]
    [InlineData(ResourceKindName.Folder, "Documents")]
    [InlineData(ResourceKindName.Folder, "https://example.org")]
    public void FilesAndFolders_RefuseIncompletePaths(ResourceKindName kind, string target) =>
        Assert.Equal(ValidationCode.InvalidPath, ResourceRules.CheckTarget(Kind(kind), target));

    [Theory]
    [InlineData(@"C:\Program Files\Éditeur d'été\Mon Appli.exe")]
    [InlineData(@"\\serveur\outils\outil.exe")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    public void Applications_AcceptProgramsAndInstalledApplications(string target) =>
        Assert.Null(ResourceRules.CheckTarget(ResourceKind.Application, target));

    [Theory]
    [InlineData(@"C:\Outils\lancer.bat")]
    [InlineData(@"C:\Outils\script.ps1")]
    [InlineData(@"C:\Outils\raccourci.lnk")]
    [InlineData(@"C:\Outils\document.pdf")]
    [InlineData("outil.exe")]
    [InlineData("https://example.org/outil.exe")]
    [InlineData(@"shell:AppsFolder\")]
    [InlineData(@"shell:AppsFolder\http://example.org")]
    public void Applications_RefuseAnythingElse(string target) =>
        Assert.Equal(ValidationCode.NotAnApplication, ResourceRules.CheckTarget(ResourceKind.Application, target));

    [Fact]
    public void UnknownKinds_KeepTheirTarget() =>
        Assert.Null(ResourceRules.CheckTarget(ResourceKind.Parse("carte-mentale"), "plan.mm"));

    [Fact]
    public void Validate_ReportsTheTypeRuleOnlyForATargetThatIsPresent()
    {
        var errors = ResourceRules.Validate(ResourceKind.Url, "Lien", " ");

        Assert.Equal(new ValidationError(ValidationField.ResourceTarget, ValidationCode.Required), Assert.Single(errors));
    }

    [Fact]
    public void Validate_ChecksTheTargetAsItWillBeStored() =>
        Assert.Empty(ResourceRules.Validate(ResourceKind.Url, "Lien", "  https://example.org  "));

    [Fact]
    public void Validate_ReportsTheNameAndTheTargetTogether()
    {
        var errors = ResourceRules.Validate(ResourceKind.Url, " ", "example.org");

        Assert.Equal(
            [
                new ValidationError(ValidationField.ResourceDisplayName, ValidationCode.Required),
                new ValidationError(ValidationField.ResourceTarget, ValidationCode.UnsupportedLink),
            ],
            errors);
    }

    public enum ResourceKindName
    {
        File,
        Folder,
    }

    private static ResourceKind Kind(ResourceKindName name) => name == ResourceKindName.File ? ResourceKind.File : ResourceKind.Folder;
}
