using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.App.Tests;

public sealed class WorkspaceEditorViewModelTests
{
    private readonly TestServices _services = new();

    [Fact]
    public void Form_StartsWithTheRequestedValues()
    {
        var form = CreateForm(name: "Études", description: "Mémoire", showDescription: false);

        Assert.Equal("Titre", form.Title);
        Assert.Equal("Valider", form.ConfirmLabel);
        Assert.Equal("Études", form.Name);
        Assert.Equal("Mémoire", form.Description);
        Assert.False(form.ShowDescription);
    }

    [Fact]
    public async Task Submit_ReportsTheSavedWorkspaceAndAsksToClose()
    {
        var form = CreateForm();
        var completions = 0;
        form.Completed += (_, _) => completions++;
        form.Name = "Études d'été";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Études d'été", form.Result?.Name);
        Assert.Equal(1, completions);
    }

    [Fact]
    public async Task Submit_ShowsANameErrorAndStaysOpen()
    {
        var form = CreateForm();
        var completions = 0;
        form.Completed += (_, _) => completions++;
        form.Name = "  ";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Donnez un nom à cet espace de travail.", form.NameError);
        Assert.Null(form.DescriptionError);
        Assert.Null(form.Result);
        Assert.Equal(0, completions);
    }

    [Fact]
    public async Task Submit_ShowsADescriptionErrorAndKeepsTheText()
    {
        var form = CreateForm();
        var description = new string('é', WorkspaceRules.DescriptionMaxLength + 1);
        form.Name = "Études";
        form.Description = description;

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Contains("2000", form.DescriptionError, StringComparison.Ordinal);
        Assert.Null(form.NameError);
        Assert.Equal(description, form.Description);
    }

    [Fact]
    public async Task Submit_ShowsBothErrorsTogether()
    {
        var form = CreateForm();
        form.Name = string.Empty;
        form.Description = new string('é', WorkspaceRules.DescriptionMaxLength + 1);

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(form.NameError);
        Assert.NotNull(form.DescriptionError);
    }

    [Fact]
    public async Task Submit_ExplainsAFailedSaveAndKeepsWhatWasTyped()
    {
        _services.Repository.WriteFailure = new IOException("Disque plein");
        var form = CreateForm();
        form.Name = "Études";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Contains(TestServices.LogsMarker, form.SaveError, StringComparison.Ordinal);
        Assert.Equal("Études", form.Name);
        Assert.Null(form.Result);
    }

    [Fact]
    public async Task Submit_ClearsThePreviousErrorsWhenItSucceeds()
    {
        var form = CreateForm();
        form.Name = string.Empty;
        await form.SubmitCommand.ExecuteAsync(null);
        form.Name = "Études";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Null(form.NameError);
        Assert.NotNull(form.Result);
    }

    [Fact]
    public async Task Submit_RequestedTwiceAtOnceSavesOnce()
    {
        var form = CreateForm();
        form.Name = "Études";
        var gate = new TaskCompletionSource();
        _services.Repository.WriteGate = gate.Task;

        var first = form.SubmitCommand.ExecuteAsync(null);
        var second = form.SubmitCommand.ExecuteAsync(null);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, _services.Repository.WriteCount);
    }

    private WorkspaceEditorViewModel CreateForm(string name = "", string description = "", bool showDescription = true) =>
        new(
            new WorkspaceEditorRequest(
                "Titre",
                "Valider",
                name,
                description,
                showDescription,
                (n, d) => _services.Workspaces.CreateAsync(n, d)),
            new AtlasDataPaths(_services.Paths.DataDirectory),
            NullLogger<WorkspaceEditorViewModel>.Instance);
}
