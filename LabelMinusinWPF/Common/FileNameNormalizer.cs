using System.Text.RegularExpressions;

namespace LabelMinusinWPF.Common;

public static partial class FileNameNormalizer
{
    [GeneratedRegex(@"[_\-.\s]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"([a-z])(\d)")]
    private static partial Regex LetterDigitBoundary();

    [GeneratedRegex(@"(\d)([a-z])")]
    private static partial Regex DigitLetterBoundary();

    public static string Normalize(string nameWithoutExtension)
    {
        var s = nameWithoutExtension.ToLowerInvariant();

        s = SeparatorRegex().Replace(s, "_");

        s = LetterDigitBoundary().Replace(s, "$1_$2");
        s = DigitLetterBoundary().Replace(s, "$1_$2");

        var parts = s.Split('_');
        var normalized = parts
            .Where(p => p.Length > 0)
            .Select(SegmentStripLeadingZeros);

        var result = string.Join('_', normalized);

        return result.Length > 0 ? result : s;
    }

    private static string SegmentStripLeadingZeros(string segment)
    {
        if (segment.All(char.IsDigit))
        {
            var trimmed = segment.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }
        return segment;
    }
}
