namespace Nutrir.Web.Components.UI;

public static class PhoneFormatter
{
    /// <summary>
    /// Strips all non-digit characters from the input.
    /// </summary>
    public static string ToDigits(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return new string(input.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Formats a digit string as (XXX) XXX-XXXX.
    /// Handles partial input gracefully.
    /// </summary>
    public static string Format(string? input)
    {
        var digits = ToDigits(input);
        if (digits.Length == 0) return string.Empty;

        return digits.Length switch
        {
            <= 3 => $"({digits}",
            <= 6 => $"({digits[..3]}) {digits[3..]}",
            <= 10 => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}",
            _ => $"({digits[..3]}) {digits[3..6]}-{digits[6..10]}"
        };
    }

    /// <summary>
    /// Formats for display. Returns null if input is empty.
    /// </summary>
    public static string? FormatOrNull(string? input)
    {
        var formatted = Format(input);
        return string.IsNullOrEmpty(formatted) ? null : formatted;
    }
}
