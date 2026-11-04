namespace Atlas.Core.Resources;

/// <summary>
/// The type of a resource, stored as text. A new type only needs a new value here, its own rules and
/// its own opener: neither the database schema nor the restoration loop has to change.
/// </summary>
public readonly record struct ResourceKind
{
    private const int MaxLength = 32;

    public static readonly ResourceKind Application = new("application");
    public static readonly ResourceKind File = new("file");
    public static readonly ResourceKind Folder = new("folder");
    public static readonly ResourceKind Url = new("url");

    private ResourceKind(string value) => Value = value;

    public string Value { get; }

    /// <summary>Accepts any well-formed name, so that a type Atlas does not know yet can be reported, not lost.</summary>
    public static bool TryParse(string? value, out ResourceKind kind)
    {
        if (IsWellFormed(value))
        {
            kind = new ResourceKind(value!);
            return true;
        }

        kind = default;
        return false;
    }

    public static ResourceKind Parse(string? value) =>
        TryParse(value, out var kind) ? kind : throw new FormatException($"'{value}' is not a valid resource type.");

    public override string ToString() => Value ?? string.Empty;

    private static bool IsWellFormed(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && char.IsAsciiLetterLower(value[0])
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
