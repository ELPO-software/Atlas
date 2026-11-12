using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Atlas.App.Controls;

/// <summary>The icon of each type of resource, from the icons of the theme.</summary>
public static class ResourceIcons
{
    /// <summary>Turns a resource type, such as "folder", into its icon. A type Atlas does not know gets a question mark.</summary>
    public static readonly IValueConverter Geometry = new FuncValueConverter<string?, Geometry?>(kind => Find(KeyFor(kind)));

    public static string KeyFor(string? kind) => kind switch
    {
        "application" => "IconApplication",
        "file" => "IconFile",
        "folder" => "IconFolder",
        "url" => "IconLink",
        _ => "IconUnknown",
    };

    private static Geometry? Find(string key) =>
        Application.Current is { } application && application.TryGetResource(key, application.ActualThemeVariant, out var resource)
            ? resource as Geometry
            : null;
}
