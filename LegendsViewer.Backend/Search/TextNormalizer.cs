using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LegendsViewer.Backend.Search;

/// <summary>
/// Deterministic, locale-independent text normalization used by indexing and querying.
/// Accents are folded, punctuation becomes whitespace, and Unicode letters and digits are kept.
/// </summary>
public static partial class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool pendingSpace = false;
        foreach (Rune rune in decomposed.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                pendingSpace = false;
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    public static bool HasSearchableCharacter(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (Rune rune in value.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                return true;
            }
        }
        return false;
    }

    public static string[] Tokenize(string normalized)
    {
        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Converts rendered event/collection HTML to a compact factual preview.</summary>
    public static string PlainText(string? value, int maximumLength = 360)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string withoutTags = HtmlTagRegex().Replace(value, " ");
        string decoded = WebUtility.HtmlDecode(withoutTags);
        string collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();
        if (collapsed.Length <= maximumLength)
        {
            return collapsed;
        }

        int cut = collapsed.LastIndexOf(' ', Math.Max(0, maximumLength - 1));
        if (cut < maximumLength / 2)
        {
            cut = maximumLength - 1;
        }
        return collapsed[..cut].TrimEnd() + "…";
    }

    public static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value ?? string.Empty;
        }
        return value[..(maximumLength - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
