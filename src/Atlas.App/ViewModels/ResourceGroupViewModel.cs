using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Resources;
using CommunityToolkit.Mvvm.Input;

namespace Atlas.App.ViewModels;

public sealed class ResourceItemViewModel(Resource resource, Func<Resource, Task> edit, Func<Resource, Task> remove)
{
    public Guid Id => resource.Id;

    public string Name => resource.DisplayName;

    public string Target { get; } = RestoreText.Target(resource);

    public bool NeedsApproval => resource.RequiresReview;

    public string EditLabel { get; } = UiText.Format(Strings.EditResourceLabel, resource.DisplayName);

    public string RemoveLabel { get; } = UiText.Format(Strings.RemoveResourceLabel, resource.DisplayName);

    public IAsyncRelayCommand EditCommand { get; } =
        new AsyncRelayCommand(() => edit(resource), AsyncRelayCommandOptions.AllowConcurrentExecutions);

    public IAsyncRelayCommand RemoveCommand { get; } =
        new AsyncRelayCommand(() => remove(resource), AsyncRelayCommandOptions.AllowConcurrentExecutions);
}

/// <summary>The resources of one category. Types Atlas does not know share a last group, so they stay visible.</summary>
public sealed record ResourceGroupViewModel(string Title, IReadOnlyList<ResourceItemViewModel> Items)
{
    public static IReadOnlyList<ResourceGroupViewModel> From(
        IEnumerable<Resource> resources, Func<Resource, Task> edit, Func<Resource, Task> remove) =>
        resources
            .GroupBy(r => CategoryOf(r.Kind))
            .OrderBy(group => group.Key)
            .Select(group => new ResourceGroupViewModel(
                TitleOf(group.Key),
                group.Select(r => new ResourceItemViewModel(r, edit, remove)).ToList()))
            .ToList();

    private static int CategoryOf(ResourceKind kind)
    {
        if (kind == ResourceKind.Application)
            return 0;
        if (kind == ResourceKind.File)
            return 1;
        if (kind == ResourceKind.Folder)
            return 2;

        return kind == ResourceKind.Url ? 3 : 4;
    }

    private static string TitleOf(int category) => category switch
    {
        0 => Strings.ResourceGroupApplications,
        1 => Strings.ResourceGroupFiles,
        2 => Strings.ResourceGroupFolders,
        3 => Strings.ResourceGroupLinks,
        _ => Strings.ResourceGroupOther,
    };
}
