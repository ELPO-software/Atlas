namespace Atlas.Core.Validation;

internal static class TextRules
{
    /// <summary>Checks a single-line value such as a name or a path. Accents and spaces are always allowed.</summary>
    public static void CheckSingleLine(
        string? value, int maxLength, bool required, ValidationField field, List<ValidationError> errors)
    {
        var text = value?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            if (required)
                errors.Add(new ValidationError(field, ValidationCode.Required));
            return;
        }

        if (text.Length > maxLength)
            errors.Add(new ValidationError(field, ValidationCode.TooLong));

        if (text.Any(char.IsControl))
            errors.Add(new ValidationError(field, ValidationCode.ControlCharacters));
    }

    /// <summary>Checks free text, where line breaks and tabs are legitimate.</summary>
    public static void CheckMultiline(string? value, int maxLength, ValidationField field, List<ValidationError> errors)
    {
        if (value is not null && value.Length > maxLength)
            errors.Add(new ValidationError(field, ValidationCode.TooLong));
    }
}
