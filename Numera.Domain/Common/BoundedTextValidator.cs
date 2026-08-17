using System.Buffers;
using System.Globalization;
using System.Text;

namespace Numera.Domain.Common;

internal static class BoundedTextValidator
{
    internal static bool TryNormalize(
        ReadOnlySpan<char> candidate,
        int minimumCodePoints,
        int maximumCodePoints,
        out string normalized)
    {
        normalized = string.Empty;
        ReadOnlySpan<char> trimmed = candidate.Trim();

        if (!TryCountCodePoints(trimmed, maximumCodePoints, out int codePoints))
        {
            return false;
        }

        if (codePoints < minimumCodePoints)
        {
            return false;
        }

        normalized = trimmed.ToString();
        return true;
    }

    private static bool TryCountCodePoints(ReadOnlySpan<char> source, int maximumCodePoints, out int codePoints)
    {
        codePoints = 0;

        for (int index = 0; index < source.Length;)
        {
            if (Rune.DecodeFromUtf16(source[index..], out Rune rune, out int consumed) != OperationStatus.Done)
            {
                return false;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                return false;
            }

            codePoints++;
            if (codePoints > maximumCodePoints)
            {
                return false;
            }

            index += consumed;
        }

        return true;
    }
}
