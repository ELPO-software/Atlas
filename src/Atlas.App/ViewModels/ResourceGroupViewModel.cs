using Atlas.App.Resources;
using Atlas.Core.Resources;

namespace Atlas.App.ViewModels;

public sealed record ResourceItemViewModel(string Name, string Target);

/// <summary>The resources of one category. Types Atlas does not know share a last group, so they stay visible.</summary>
public sealed record ResourceGroupViewModel(string Title, IReadOnlyList<ResourceItemViewModel> Items)
{
    public static IReadOnlyList<ResourceGroupViewModel> From(IEnumerable<Resource> resources) =>
        resources
            .GroupBy(r => CategoryOf(r.Kind))
            .OrderBy(group => group.Key)
            .Select(group => new ResourceGroupViewModel(
                TitleOf(group.Key),
                group.Select(r => new ResourceItemViewModel(r.DisplayName, r.Target)).ToList()))
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
