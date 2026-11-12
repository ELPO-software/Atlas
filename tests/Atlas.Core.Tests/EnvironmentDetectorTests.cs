using Atlas.Core.Detection;
using Atlas.Core.Resources;

namespace Atlas.Core.Tests;

public sealed class EnvironmentDetectorTests
{
    private const string Code = @"C:\Program Files\Microsoft VS Code\Code.exe";
    private const string Word = @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE";
    private const string Terminal = @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string Root = @"C:\Users\Léa\source\Atlas";

    private static readonly ObservedProject Atlas = new(Root, "Atlas", Root + @"\Atlas.slnx", "https://github.com/ELPO-software/Atlas");

    [Fact]
    public void AProjectOpenInAnEditorIsDetected()
    {
        var observation = new WorkObservation(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(Root, true), new ObservedItem(Root + @"\README.md", false)])],
            [],
            [Atlas]);

        var proposal = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.Equal(ProposalKind.Project, proposal.Kind);
        Assert.Equal(DetectionConfidence.Detected, proposal.Confidence);
        Assert.Equal("Atlas", proposal.Title);
        Assert.Equal(
            [
                (ResourceKind.Folder, Root, ProposalReason.ProjectFolder),
                (ResourceKind.File, Root + @"\Atlas.slnx", ProposalReason.ProjectEntryFile),
                (ResourceKind.Application, Code, ProposalReason.OpenInApplication),
                (ResourceKind.File, Root + @"\README.md", ProposalReason.OpenInApplication),
                (ResourceKind.Url, "https://github.com/ELPO-software/Atlas", ProposalReason.Repository),
            ],
            proposal.Resources.Select(r => (r.Kind, r.Target, r.Reason)));
        Assert.Equal("github.com/ELPO-software/Atlas", proposal.Resources[^1].DisplayName);
    }

    [Fact]
    public void AProjectSeenOnlyInAFolderWindowIsOnlyProbable()
    {
        var observation = new WorkObservation([], [Root + @"\docs"], [Atlas]);

        var proposal = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.Equal(DetectionConfidence.Probable, proposal.Confidence);
        Assert.DoesNotContain(proposal.Resources, r => r.Kind == ResourceKind.Application);
    }

    [Fact]
    public void AProjectNothingPointsToIsNotProposed()
    {
        var observation = new WorkObservation([new ObservedApplication("Word", Word, [])], [], [Atlas]);

        Assert.DoesNotContain(EnvironmentDetector.Propose(observation), p => p.Kind == ProposalKind.Project);
    }

    [Fact]
    public void AProjectWithOnlyItsFolderIsTooThinToPropose()
    {
        var bare = new ObservedProject(Root, "Atlas", null, null);
        var observation = new WorkObservation([], [Root], [bare]);

        Assert.Empty(EnvironmentDetector.Propose(observation));
    }

    [Fact]
    public void FilesThatRunCodeAreNeverProposed()
    {
        var observation = new WorkObservation(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(Root + @"\build\publish.ps1", false), new ObservedItem(Root + @"\run.bat", false)])],
            [],
            [Atlas]);

        var proposal = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.DoesNotContain(proposal.Resources, r => r.Target.EndsWith(".ps1", StringComparison.Ordinal) || r.Target.EndsWith(".bat", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAddressThatIsNotOnTheWebIsLeftOut()
    {
        var project = Atlas with { RepositoryAddress = "file:///C:/repos/atlas.git" };
        var observation = new WorkObservation([new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(Root, true)])], [], [project]);

        var proposal = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.DoesNotContain(proposal.Resources, r => r.Kind == ResourceKind.Url);
    }

    [Fact]
    public void AProjectKeepsAtMostAFewFiles()
    {
        var files = Enumerable.Range(1, 12).Select(i => new ObservedItem($@"{Root}\notes-{i}.md", false)).ToList();
        var observation = new WorkObservation([new ObservedApplication("Visual Studio Code", Code, files)], [], [Atlas]);

        var proposal = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.Equal(EnvironmentDetector.MaxFilesPerProject, proposal.Resources.Count(r => r.Reason == ProposalReason.OpenInApplication && r.Kind == ResourceKind.File));
    }

    [Fact]
    public void PathsAreComparedAsWindowsDoes()
    {
        var observation = new WorkObservation(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(@"c:\users\léa\SOURCE\atlas\", true)])],
            [],
            [Atlas]);

        Assert.Equal(DetectionConfidence.Detected, Assert.Single(EnvironmentDetector.Propose(observation)).Confidence);
    }

    [Fact]
    public void ASiblingFolderWithALongerNameIsNotInsideTheProject()
    {
        var observation = new WorkObservation(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(Root + "-old", true)])],
            [],
            [Atlas]);

        Assert.DoesNotContain(EnvironmentDetector.Propose(observation), p => p.Kind == ProposalKind.Project);
    }

    [Fact]
    public void WhatIsNotPartOfAProjectFormsAContext()
    {
        var observation = new WorkObservation(
            [
                new ObservedApplication("Word", Word, [new ObservedItem(@"D:\Cours\Rapport.docx", false)]),
                new ObservedApplication("Terminal", Terminal, []),
                new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(Root, true)]),
            ],
            [@"D:\Cours"],
            [Atlas]);

        var context = Assert.Single(EnvironmentDetector.Propose(observation), p => p.Kind == ProposalKind.RecurringContext);

        Assert.Equal(DetectionConfidence.Probable, context.Confidence);
        Assert.Equal("Word, Terminal", context.Title);
        Assert.Equal(
            [Word, Terminal, @"D:\Cours", @"D:\Cours\Rapport.docx"],
            context.Resources.Select(r => r.Target));
        Assert.DoesNotContain(context.Resources, r => r.Target == Code);
    }

    [Fact]
    public void AContextNeedsAnApplicationAndEnoughResources()
    {
        Assert.Empty(EnvironmentDetector.Propose(new WorkObservation([], [@"D:\Cours", @"D:\Photos", @"D:\Musique"], [])));
        Assert.Empty(EnvironmentDetector.Propose(new WorkObservation([new ObservedApplication("Word", Word, [])], [@"D:\Cours"], [])));
    }

    [Fact]
    public void TheSameContextKeepsTheSameKey()
    {
        WorkObservation Observe(params string[] folders) =>
            new([new ObservedApplication("Word", Word, []), new ObservedApplication("Terminal", Terminal, [])], folders, []);

        var first = Assert.Single(EnvironmentDetector.Propose(Observe(@"D:\Cours", @"D:\Notes")));
        var again = Assert.Single(EnvironmentDetector.Propose(Observe(@"D:\Notes", @"d:\cours\")));
        var other = Assert.Single(EnvironmentDetector.Propose(Observe(@"D:\Cours", @"D:\Photos")));

        Assert.Equal(first.Key, again.Key);
        Assert.NotEqual(first.Key, other.Key);
    }

    [Fact]
    public void SimilarityCountsSharedResources()
    {
        ProposedResource Folder(string path) => new(ResourceKind.Folder, "x", path, ProposalReason.OpenInExplorer);

        Assert.Equal(1, EnvironmentDetector.Similarity([Folder(@"D:\A"), Folder(@"D:\B")], [Folder(@"d:\a\"), Folder(@"D:\B")]));
        Assert.Equal(1.0 / 3, EnvironmentDetector.Similarity([Folder(@"D:\A"), Folder(@"D:\B")], [Folder(@"D:\B"), Folder(@"D:\C")]), 3);
        Assert.Equal(0, EnvironmentDetector.Similarity([Folder(@"D:\A")], [Folder(@"D:\B")]));
    }

    [Fact]
    public void AnInvalidApplicationIsLeftOut()
    {
        var observation = new WorkObservation(
            [
                new ObservedApplication("Document", @"C:\Docs\lettre.docx", []),
                new ObservedApplication("Word", Word, []),
                new ObservedApplication("Terminal", Terminal, []),
            ],
            [@"D:\Cours"],
            []);

        var context = Assert.Single(EnvironmentDetector.Propose(observation));

        Assert.DoesNotContain(context.Resources, r => r.Target.EndsWith(".docx", StringComparison.Ordinal) && r.Kind == ResourceKind.Application);
    }

    [Theory]
    [InlineData(@"C:\A\B", @"C:\A", true)]
    [InlineData(@"C:\A", @"C:\A\", true)]
    [InlineData(@"c:\a\b\c.txt", @"C:\A", true)]
    [InlineData(@"C:\AB", @"C:\A", false)]
    [InlineData(@"C:\", @"C:\A", false)]
    [InlineData(@"C:\A\B", @"C:\", true)]
    public void IsInside_FollowsFolders(string path, string root, bool expected) =>
        Assert.Equal(expected, PathText.IsInside(path, root));
}
