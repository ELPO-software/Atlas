using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Exchange;

public sealed record DocumentResource(string Kind, string Name, string Target, bool RequiresReview);

public sealed record DocumentContent(string Name, string Description, string Notes, IReadOnlyList<DocumentResource> Resources);

public enum DocumentProblem
{
    FileNotFound,
    Unreadable,
    TooLarge,
    NotText,
    InvalidJson,
    DuplicateProperty,
    NotAnAtlasFile,
    NewerVersion,
    UnsupportedVersion,
    UnexpectedProperty,
    MissingProperty,
    WrongType,
    TooManyResources,
}

/// <param name="Location">Where in the file the problem is, such as "workspace.resources[3].target".</param>
public sealed record DocumentError(DocumentProblem Problem, string Location = "");

/// <summary>The content of a file, or why it cannot be used. Reading never throws for bad input.</summary>
public sealed record DocumentReadResult(DocumentContent? Content, DocumentError? Error)
{
    public static DocumentReadResult Failed(DocumentProblem problem, string location = "") => new(null, new DocumentError(problem, location));
}

/// <summary>
/// The file format of a workspace, used to export and import it and to keep saved versions. The reader is strict:
/// it refuses anything it does not expect, and it reads the file only, never what the file points to.
/// </summary>
public static class WorkspaceDocument
{
    public const string Format = "elpo-atlas-workspace";
    public const int CurrentVersion = 1;
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxResources = 1_000;
    public const int MaxDepth = 16;

    // Keeps accents and apostrophes readable in the file. The stricter encoders only matter for JSON placed inside a
    // web page, which these files never are; quotes, backslashes and control characters are still escaped.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        MaxDepth = MaxDepth,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    public static string Write(Workspace workspace, DateTimeOffset savedAt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteString("savedAt", savedAt.ToUniversalTime());
            writer.WriteStartObject("workspace");
            writer.WriteString("name", workspace.Name);
            writer.WriteString("description", workspace.Description);
            writer.WriteString("notes", workspace.Notes);
            writer.WriteStartArray("resources");
            foreach (var resource in workspace.Resources)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", resource.Kind.Value);
                writer.WriteString("name", resource.DisplayName);
                writer.WriteString("target", resource.Target);
                writer.WriteBoolean("requiresReview", resource.RequiresReview);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static DocumentReadResult Read(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            return DocumentReadResult.Failed(DocumentProblem.TooLarge);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ReaderOptions);
        }
        catch (JsonException ex)
        {
            return DocumentReadResult.Failed(DocumentProblem.InvalidJson, ex.LineNumber is { } line ? (line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        }

        using (document)
        {
            try
            {
                return new DocumentReadResult(ReadRoot(document.RootElement), null);
            }
            catch (DocumentException ex)
            {
                return new DocumentReadResult(null, ex.Error);
            }
        }
    }

    private static DocumentContent ReadRoot(JsonElement root)
    {
        var properties = PropertiesOf(root, string.Empty, ["format", "version", "savedAt", "workspace"]);

        if (!properties.TryGetValue("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format)
            throw new DocumentException(DocumentProblem.NotAnAtlasFile);

        var version = Required(properties, "version", string.Empty);
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            throw new DocumentException(DocumentProblem.WrongType, "version");
        if (number > CurrentVersion)
            throw new DocumentException(DocumentProblem.NewerVersion);
        if (number < 1)
            throw new DocumentException(DocumentProblem.UnsupportedVersion);

        if (properties.TryGetValue("savedAt", out var savedAt) && savedAt.ValueKind != JsonValueKind.String)
            throw new DocumentException(DocumentProblem.WrongType, "savedAt");

        var workspace = PropertiesOf(Required(properties, "workspace", string.Empty), "workspace", ["name", "description", "notes", "resources"]);
        var resources = new List<DocumentResource>();
        if (workspace.TryGetValue("resources", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array)
                throw new DocumentException(DocumentProblem.WrongType, "workspace.resources");
            if (list.GetArrayLength() > MaxResources)
                throw new DocumentException(DocumentProblem.TooManyResources);

            var index = 0;
            foreach (var item in list.EnumerateArray())
                resources.Add(ReadResource(item, $"workspace.resources[{index++}]"));
        }

        return new DocumentContent(
            String(Required(workspace, "name", "workspace"), "workspace.name"),
            Optional(workspace, "description", "workspace"),
            Optional(workspace, "notes", "workspace"),
            resources);
    }

    private static DocumentResource ReadResource(JsonElement item, string location)
    {
        var properties = PropertiesOf(item, location, ["kind", "name", "target", "requiresReview"]);
        var requiresReview = false;
        if (properties.TryGetValue("requiresReview", out var review))
        {
            if (review.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DocumentException(DocumentProblem.WrongType, location + ".requiresReview");

            requiresReview = review.GetBoolean();
        }

        return new DocumentResource(
            String(Required(properties, "kind", location), location + ".kind"),
            String(Required(properties, "name", location), location + ".name"),
            String(Required(properties, "target", location), location + ".target"),
            requiresReview);
    }

    /// <summary>Reads an object, refusing a property it does not expect and a property given twice.</summary>
    private static Dictionary<string, JsonElement> PropertiesOf(JsonElement element, string location, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw location.Length == 0
                ? new DocumentException(DocumentProblem.NotAnAtlasFile)
                : new DocumentException(DocumentProblem.WrongType, location);
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var path = location.Length == 0 ? property.Name : $"{location}.{property.Name}";
            if (!expected.Contains(property.Name, StringComparer.Ordinal))
                throw new DocumentException(DocumentProblem.UnexpectedProperty, path);
            if (!properties.TryAdd(property.Name, property.Value))
                throw new DocumentException(DocumentProblem.DuplicateProperty, path);
        }

        return properties;
    }

    private static JsonElement Required(Dictionary<string, JsonElement> properties, string name, string location) =>
        properties.TryGetValue(name, out var value)
            ? value
            : throw new DocumentException(DocumentProblem.MissingProperty, location.Length == 0 ? name : $"{location}.{name}");

    private static string Optional(Dictionary<string, JsonElement> properties, string name, string location) =>
        properties.TryGetValue(name, out var value) ? String(value, $"{location}.{name}") : string.Empty;

    private static string String(JsonElement value, string location) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new DocumentException(DocumentProblem.WrongType, location);

    private sealed class DocumentException(DocumentProblem problem, string location = "") : Exception(problem.ToString())
    {
        public DocumentError Error { get; } = new(problem, location);
    }
}
