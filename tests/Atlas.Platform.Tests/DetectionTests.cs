using System.Runtime.Versioning;
using Atlas.Core.Detection;
using Atlas.Core.Resources;
using Atlas.Platform.Windows;
using Atlas.Platform.Windows.Detection;

namespace Atlas.Platform.Tests;

public sealed class ProjectLocatorTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"Accueil d'essai {Guid.NewGuid():N}");
    private readonly ProjectLocator _locator;

    public ProjectLocatorTests()
    {
        Directory.CreateDirectory(_home);
        _locator = new ProjectLocator([_home]);
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private string Make(string relative, string? content = null)
    {
        var path = Path.Combine(_home, relative);
        if (content is null)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return path;
    }

    [Fact]
    public void AGitRepositoryIsTheProjectOfEverythingInside()
    {
        var root = Make(@"source\Atlas d'été");
        Make(@"source\Atlas d'été\.git\config", "[core]\n\tbare = false\n[remote \"origin\"]\n\turl = git@github.com:ELPO-software/Atlas.git\n");
        Make(@"source\Atlas d'été\Atlas.slnx", "<Solution />");
        Make(@"source\Atlas d'été\src\App\package.json", "{}");
        var file = Make(@"source\Atlas d'été\src\App\Program.cs", "class P {}");

        var project = _locator.Find(file);

        Assert.NotNull(project);
        Assert.Equal(root, project.Root);
        Assert.Equal("Atlas d'été", project.Name);
        Assert.Equal(Path.Combine(root, "Atlas.slnx"), project.EntryFile);
        Assert.Equal("https://github.com/ELPO-software/Atlas", project.RepositoryAddress);
    }

    [Fact]
    public void WithoutGitTheNearestManifestDecides()
    {
        var site = Make(@"travail\site", null);
        Make(@"travail\site\package.json", "{}");
        var page = Make(@"travail\site\pages\index.html", "<html />");

        var project = _locator.Find(page);

        Assert.Equal(site, project?.Root);
        Assert.Null(project?.RepositoryAddress);
    }

    [Fact]
    public void AFolderOutsideAnyProjectHasNone()
    {
        var notes = Make(@"notes\semaine.txt", "Lundi");

        Assert.Null(_locator.Find(notes));
    }

    [Fact]
    public void TheHomeFolderIsNeverAProject()
    {
        Make(@".git\config", "[core]");
        var file = Make(@"lettre.txt", "Bonjour");

        Assert.Null(_locator.Find(file));
        Assert.Null(_locator.Find(_home));
    }

    [Fact]
    public void AWorktreeFindsItsRepositoryAddressThroughItsGitFile()
    {
        var main = Make(@"depots\principal");
        Make(@"depots\principal\.git\config", "[remote \"origin\"]\n\turl = https://gitlab.com/elpo/atlas.git\n");
        Make(@"depots\principal\.git\worktrees\essai\commondir", "../..");
        var worktree = Make(@"depots\essai");
        Make(@"depots\essai\.git", $"gitdir: {Path.Combine(main, ".git", "worktrees", "essai")}\n");

        var project = _locator.Find(worktree);

        Assert.Equal(worktree, project?.Root);
        Assert.Equal("https://gitlab.com/elpo/atlas", project?.RepositoryAddress);
    }

    [Fact]
    public void SeveralSolutionsPickTheOneNamedAfterTheFolder()
    {
        var root = Make(@"src\Outils");
        Make(@"src\Outils\Outils.sln", "x");
        Make(@"src\Outils\Autre.sln", "x");
        Make(@"src\Outils\.git\HEAD", "ref: refs/heads/main");

        Assert.Equal(Path.Combine(root, "Outils.sln"), _locator.Find(root)?.EntryFile);
    }

    [Theory]
    [InlineData(@"\\serveur\partage\projet")]
    [InlineData("relatif\\dossier")]
    [InlineData(@"C:\absent\nulle part\fichier.txt")]
    public void PathsAtlasCannotUseHaveNoProject(string path) =>
        Assert.Null(_locator.Find(path));
}

public sealed class RepositoryAddressTests
{
    [Theory]
    [InlineData("https://github.com/ELPO-software/Atlas.git", "https://github.com/ELPO-software/Atlas")]
    [InlineData("https://github.com/ELPO-software/Atlas", "https://github.com/ELPO-software/Atlas")]
    [InlineData("https://user:ghp_secret123@github.com/ELPO-software/Atlas.git", "https://github.com/ELPO-software/Atlas")]
    [InlineData("git@github.com:ELPO-software/Atlas.git", "https://github.com/ELPO-software/Atlas")]
    [InlineData("ssh://git@GitLab.com/elpo/atlas.git", "https://gitlab.com/elpo/atlas")]
    [InlineData("http://git.exemple.fr/equipe/projet/", "https://git.exemple.fr/equipe/projet")]
    public void RemotesBecomeWebAddressesWithoutCredentials(string remote, string expected) =>
        Assert.Equal(expected, RepositoryAddress.FromRemote(remote));

    [Theory]
    [InlineData(@"C:\depots\atlas.git")]
    [InlineData("../atlas.git")]
    [InlineData("file:///C:/depots/atlas.git")]
    [InlineData("https://github.com/")]
    [InlineData("")]
    public void LocalOrEmptyRemotesHaveNoWebAddress(string remote) =>
        Assert.Null(RepositoryAddress.FromRemote(remote));

    [Fact]
    public void OnlyTheOriginRemoteCounts()
    {
        string[] config =
        [
            "[remote \"upstream\"]",
            "\turl = https://github.com/autre/projet.git",
            "[branch \"main\"]",
            "\tremote = origin",
            "[remote \"origin\"]",
            "\tfetch = +refs/heads/*:refs/remotes/origin/*",
            "\turl = https://github.com/ELPO-software/Atlas.git",
        ];

        Assert.Equal("https://github.com/ELPO-software/Atlas", RepositoryAddress.FromGitConfig(config));
        Assert.Null(RepositoryAddress.FromGitConfig(["[core]", "\tbare = false"]));
    }
}

public sealed class OpenItemsTests
{
    private static readonly string[] Excluded = [@"C:\Windows", @"C:\Program Files"];

    private static bool? Disk(string path) => path switch
    {
        @"D:\Cours\Rapport final.docx" => false,
        @"D:\Cours" => true,
        @"D:\Cours\lancer.bat" => false,
        @"C:\Program Files\Office\modele.dotx" => false,
        @"C:\Users\Léa\source\Atlas" => true,
        _ => null,
    };

    [Fact]
    public void CommandLinePathsThatExistAreKept()
    {
        var items = OpenItems.FromCommandLine(
            [@"C:\Program Files\Office\WINWORD.EXE", "/n", @"D:\Cours\Rapport final.docx", @"D:\Cours", @"D:\absent.docx"],
            Disk,
            Excluded);

        Assert.Equal([new ObservedItem(@"D:\Cours\Rapport final.docx", false), new ObservedItem(@"D:\Cours", true)], items);
    }

    [Fact]
    public void TheProgramScriptsAndSystemLocationsAreLeftOut()
    {
        var items = OpenItems.FromCommandLine(
            [@"D:\Cours\Rapport final.docx", @"D:\Cours\lancer.bat", @"C:\Program Files\Office\modele.dotx", "--type=renderer"],
            Disk,
            Excluded);

        Assert.Empty(items);
    }

    [Fact]
    public void SwitchesCarryingAFileAddressAreRead()
    {
        var items = OpenItems.FromCommandLine(["Code.exe", "--folder-uri=file:///c%3A/Users/L%C3%A9a/source/Atlas"], Disk, Excluded);

        Assert.Equal([new ObservedItem(@"C:\Users\Léa\source\Atlas", true)], items);
    }

    [Fact]
    public void CodeStorageListsTheWindowsAndRecentFolders()
    {
        const string Json = """
            {
              "windowsState": {
                "lastActiveWindow": { "folder": "file:///c%3A/Users/L%C3%A9a/source/Atlas", "backupPath": "x" },
                "openedWindows": [ { "folder": "file:///d%3A/Cours" }, { "workspaceIdentifier": { "id": "1" } } ]
              },
              "profileAssociations": { "workspaces": { "file:///d%3A/Notes": "__default__profile__", "vscode-remote://wsl/home": "x" } },
              "backupWorkspaces": { "folders": [ { "folderUri": "file:///c%3A/Users/L%C3%A9a/source/Atlas" } ] }
            }
            """;

        Assert.Equal([@"C:\Users\Léa\source\Atlas", @"D:\Cours", @"D:\Notes"], OpenItems.FoldersFromCodeStorage(Json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "windowsState": 3 }""")]
    public void AnUnexpectedCodeStorageGivesNoFolder(string json) =>
        Assert.Empty(OpenItems.FoldersFromCodeStorage(json));

    [Fact]
    public void VisualStudioSettingsListTheRecentSolutions()
    {
        const string Xml = """
            <content>
              <indexed>
                <collection name="CodeContainers.Offline">
                  <value name="value">[{"Key":"C:\\Users\\Léa\\source\\Atlas\\Atlas.slnx","Value":{"IsFavorite":false}},{"Key":"relative.sln"}]</value>
                </collection>
              </indexed>
            </content>
            """;

        Assert.Equal([@"C:\Users\Léa\source\Atlas\Atlas.slnx"], OpenItems.SolutionsFromVisualStudioSettings(Xml));
        Assert.Empty(OpenItems.SolutionsFromVisualStudioSettings("<content />"));
        Assert.Empty(OpenItems.SolutionsFromVisualStudioSettings("<!DOCTYPE x [<!ENTITY a 'b'>]><content />"));
    }

    [Fact]
    public void WindowTitlesTellWhichFolderIsOpen()
    {
        string[] folders = [@"C:\source\Atlas", @"D:\Cours", @"C:\old\Atlas-v1", @"E:\Notes", @"F:\Notes"];
        string[] titles = ["● Program.cs - Atlas - Visual Studio Code", "Bienvenue - Notes - Visual Studio Code", "Cours — Visual Studio Code"];

        var open = OpenItems.MatchTitles(titles, folders, f => Path.GetFileName(f));

        // "Notes" names two folders: Atlas cannot tell which one, so it claims neither.
        Assert.Equal([@"C:\source\Atlas", @"D:\Cours"], open);
    }
}

public sealed class NotificationXmlTests
{
    [Fact]
    public void TextsAreEscapedAndActionsCarryTheirArguments()
    {
        var xml = Atlas.Platform.Windows.Notifications.ToastXml.Build(new(
            "tag",
            "Nouvel environnement détecté",
            "Atlas a identifié « R&D <beta> »",
            [new("Enregistrer", "save"), new("Plus tard", "later"), new("Ignorer", "ignore")]));

        var document = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Equal(["Nouvel environnement détecté", "Atlas a identifié « R&D <beta> »"], document.Descendants("text").Select(t => t.Value));
        Assert.Equal(["save", "later", "ignore"], document.Descendants("action").Select(a => (string)a.Attribute("arguments")!));
        Assert.All(document.Descendants("action"), a => Assert.Equal("foreground", (string?)a.Attribute("activationType")));
    }

    [Fact]
    public void AtMostFiveActions()
    {
        var actions = Enumerable.Range(1, 8).Select(i => new Atlas.Core.Notifications.NotificationAction($"Action {i}", $"a{i}")).ToList();

        var xml = Atlas.Platform.Windows.Notifications.ToastXml.Build(new("tag", "Titre", "Message", actions));

        Assert.Equal(5, System.Xml.Linq.XDocument.Parse(xml).Descendants("action").Count());
    }

    /// <summary>
    /// The interface identifier of TypedEventHandler&lt;ToastNotification, object&gt; is not written anywhere: the Windows
    /// Runtime derives it from the generic definition and its arguments. This recomputes it the way the specification says.
    /// </summary>
    [Fact]
    public void TheActivationHandlerHasTheIdentifierTheWindowsRuntimeComputes()
    {
        const string Signature =
            "pinterface({9de1c534-6ae1-11e0-84e1-18a905bcc53f};rc(Windows.UI.Notifications.ToastNotification;{997e2675-059e-4e60-8b06-1760917c8b80});cinterface(IInspectable))";

        var declared = typeof(Atlas.Platform.Windows.Notifications.IToastActivatedHandler).GUID;

        Assert.Equal(ParameterizedInterfaceId(Signature), declared);
    }

    private static Guid ParameterizedInterfaceId(string signature)
    {
        var space = new Guid("11F47AD5-7B73-42C0-ABAE-878B1E16ADEE").ToByteArray();
        Array.Reverse(space, 0, 4);
        Array.Reverse(space, 4, 2);
        Array.Reverse(space, 6, 2);

        var hash = System.Security.Cryptography.SHA1.HashData([.. space, .. System.Text.Encoding.UTF8.GetBytes(signature)]);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
        return new Guid(bytes);
    }

}

[SupportedOSPlatform("windows")]
public sealed class WindowsDetectionTests
{
    [Fact]
    public void CommandLinesAreSplitAsWindowsDoes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

        Assert.Equal(
            [@"C:\Program Files\Office\WINWORD.EXE", "/n", @"D:\Cours\Rapport final.docx"],
            WindowsSession.Split("\"C:\\Program Files\\Office\\WINWORD.EXE\" /n \"D:\\Cours\\Rapport final.docx\""));
    }

    [Fact]
    public async Task ObservingTheSessionReadsWithoutFailing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var observer = new WindowsWorkObserver(new ShellApplicationCatalog(), ProjectLocator.ForCurrentUser());

        var observation = await observer.ObserveAsync(TestContext.Current.CancellationToken);

        Assert.True(observation.Applications.Count <= WindowsWorkObserver.MaxApplications);
        Assert.All(observation.Applications, a => Assert.Null(ResourceRules.CheckTarget(ResourceKind.Application, a.Target)));
        Assert.DoesNotContain(observation.Applications, a => a.Target.EndsWith(@"\Atlas.Platform.Tests.exe", StringComparison.OrdinalIgnoreCase));
    }

}
