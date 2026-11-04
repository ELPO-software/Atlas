using System.Reflection;
using System.Text.RegularExpressions;

namespace Atlas.Infrastructure.Persistence;

/// <param name="Version">The schema version this script produces. Versions start at 1 and have no gaps.</param>
public sealed record Migration(int Version, string Name, string Sql);

internal static partial class EmbeddedMigrations
{
    private const string Prefix = "Atlas.Migrations.";

    /// <summary>Loads the numbered scripts compiled into this assembly, in order.</summary>
    public static IReadOnlyList<Migration> Load()
    {
        var assembly = typeof(EmbeddedMigrations).Assembly;
        var migrations = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var match = FileName().Match(resource[Prefix.Length..]);
            if (!match.Success)
                throw new InvalidOperationException($"Migration script '{resource}' must be named 0001_name.sql.");

            migrations.Add(new Migration(int.Parse(match.Groups[1].Value), match.Groups[2].Value, ReadText(assembly, resource)));
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    private static string ReadText(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^(\d{4})_([a-z0-9_]+)\.sql$")]
    private static partial Regex FileName();
}
