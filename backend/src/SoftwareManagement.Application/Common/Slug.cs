using System.Globalization;
using System.Text;

namespace SoftwareManagement.Application.Common;

/// <summary>
/// Slug rules from BR-SITE-02: lowercase, 3 to 120 characters, matching
/// ^[a-z0-9]+(-[a-z0-9]+)*$. Slugs are public URLs, so they are generated in exactly one
/// place and validated in exactly one place.
/// </summary>
public static class Slug
{
    public const int MinLength = 3;
    public const int MaxLength = 120;

    /// <summary>
    /// Turns arbitrary text into a candidate slug. Accented letters are folded to their base
    /// letter so "Café Manager" becomes "cafe-manager" rather than losing the word.
    /// Returns an empty string when nothing usable remains; callers must treat that as
    /// invalid input rather than persisting it.
    /// </summary>
    public static string From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalised = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalised.Length);
        var lastWasHyphen = true;

        foreach (var ch in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    /// <summary>True only for a slug that may be persisted and served as a public URL.</summary>
    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        if (slug[0] == '-' || slug[^1] == '-')
        {
            return false;
        }

        char previous = '\0';
        foreach (var ch in slug)
        {
            var isLower = ch is >= 'a' and <= 'z';
            var isDigit = ch is >= '0' and <= '9';

            if (!isLower && !isDigit && ch != '-')
            {
                return false;
            }

            if (ch == '-' && previous == '-')
            {
                return false;
            }

            previous = ch;
        }

        return true;
    }
}
