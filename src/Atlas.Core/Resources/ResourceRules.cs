using Atlas.Core.Validation;

namespace Atlas.Core.Resources;

public static class ResourceRules
{
    public const int DisplayNameMaxLength = 120;

    /// <summary>The longest path Windows accepts.</summary>
    public const int TargetMaxLength = 32_767;

    public static IReadOnlyList<ValidationError> Validate(ResourceKind kind, string? displayName, string? target)
    {
        var errors = new List<ValidationError>();
        TextRules.CheckSingleLine(displayName, DisplayNameMaxLength, required: true, ValidationField.ResourceDisplayName, errors);

        var targetErrors = new List<ValidationError>();
        TextRules.CheckSingleLine(target, TargetMaxLength, required: true, ValidationField.ResourceTarget, targetErrors);
        errors.AddRange(targetErrors);

        // The rules of a type only make sense for a target that is present and on one line.
        if (targetErrors.Count == 0 && CheckTarget(kind, target!.Trim()) is { } code)
            errors.Add(new ValidationError(ValidationField.ResourceTarget, code));

        return errors;
    }

    /// <returns>Why the target cannot be used for this type, or null if it can.</returns>
    public static ValidationCode? CheckTarget(ResourceKind kind, string target)
    {
        if (kind == ResourceKind.Url)
            return ResourceTargets.TryGetWebAddress(target, out _) ? null : ValidationCode.UnsupportedLink;

        if (kind == ResourceKind.Folder)
            return ResourceTargets.IsUsablePath(target) ? null : ValidationCode.InvalidPath;

        if (kind == ResourceKind.File)
        {
            if (!ResourceTargets.IsUsablePath(target))
                return ValidationCode.InvalidPath;

            return ResourceTargets.RunsCode(target) ? ValidationCode.ExecutableFile : null;
        }

        if (kind == ResourceKind.Application)
        {
            var isApplication = ResourceTargets.TryGetInstalledApplicationId(target, out var applicationId)
                ? ResourceTargets.IsInstalledApplicationId(applicationId)
                : ResourceTargets.IsUsablePath(target) && ResourceTargets.IsProgram(target);

            return isApplication ? null : ValidationCode.NotAnApplication;
        }

        // A type from a newer version keeps its target as it is: Atlas will not open it anyway.
        return null;
    }
}
