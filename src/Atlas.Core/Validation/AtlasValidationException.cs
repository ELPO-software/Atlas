namespace Atlas.Core.Validation;

/// <summary>A value was refused by a business rule. Nothing has been changed when this is thrown.</summary>
public sealed class AtlasValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("The submitted values were refused: " + string.Join(", ", errors.Select(e => $"{e.Field}/{e.Code}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
