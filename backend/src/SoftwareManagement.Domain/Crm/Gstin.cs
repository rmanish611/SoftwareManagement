using System.Text.RegularExpressions;

namespace SoftwareManagement.Domain.Crm;

/// <summary>
/// The Goods and Services Tax identification number (BR-CUST-01).
///
/// Fifteen characters with a fixed shape: two digits of state code, the ten-character PAN, an
/// entity digit, a literal Z, and a checksum character. Validating it here rather than trusting
/// the person typing means a wrong number is caught while they still have the certificate open,
/// not when an invoice is rejected weeks later.
/// </summary>
public static partial class Gstin
{
    public const int Length = 15;

    public const string Pattern = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$";

    /// <summary>The rule in words, for the message shown when a number is refused.</summary>
    public const string Explanation =
        "A GSTIN is 15 characters: two digits of state code, a ten-character PAN, an entity digit, "
        + "the letter Z, and a check character.";

    public static bool IsValid(string? value) =>
        value is not null && value.Length == Length && Matcher().IsMatch(value);

    /// <summary>
    /// Upper-cased and trimmed, because the number on a certificate is upper case and somebody
    /// typing it in lower case has still typed the right number.
    /// </summary>
    public static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex Matcher();
}
