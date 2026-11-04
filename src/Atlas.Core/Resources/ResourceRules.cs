using Atlas.Core.Validation;

namespace Atlas.Core.Resources;

/// <summary>Rules shared by every resource type. Rules specific to a type are added with that type's behaviour.</summary>
public static class ResourceRules
{
    public const int DisplayNameMaxLength = 120;

    /// <summary>The longest path Windows accepts.</summary>
    public const int TargetMaxLength = 32_767;

    public static IReadOnlyList<ValidationError> Validate(string? displayName, string? target)
    {
        var errors = new List<ValidationError>();
        TextRules.CheckSingleLine(displayName, DisplayNameMaxLength, required: true, ValidationField.ResourceDisplayName, errors);
        TextRules.CheckSingleLine(target, TargetMaxLength, required: true, ValidationField.ResourceTarget, errors);
        return errors;
    }
}
