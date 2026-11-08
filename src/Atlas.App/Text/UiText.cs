using System.Globalization;
using Atlas.App.Resources;

namespace Atlas.App.Text;

/// <summary>
/// Formats interface text. The interface is written in French, so dates and numbers are too, whatever
/// the language of the system: a French sentence with an English date would be hard to read.
/// </summary>
internal static class UiText
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");

    public static string Format(string format, params object?[] values) =>
        string.Format(Culture, format, values);

    public static string WorkspaceCount(int count) =>
        Plural(count, Strings.WorkspaceCountOne, Strings.WorkspaceCountMany);

    public static string ResourceCount(int count) =>
        Plural(count, Strings.ResourceCountOne, Strings.ResourceCountMany);

    // In French, zero takes the singular.
    private static string Plural(int count, string one, string many) =>
        Format(count <= 1 ? one : many, count);
}
