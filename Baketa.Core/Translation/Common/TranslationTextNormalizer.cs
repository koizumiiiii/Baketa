using System;
using System.Globalization;
using System.Text;

namespace Baketa.Core.Translation.Common;

/// <summary>Joins OCR wrapping lines before text is sent for translation.</summary>
public static class TranslationTextNormalizer
{
    /// <summary>
    /// Removes line breaks, preserving word boundaries for space-delimited scripts.
    /// Existing horizontal whitespace at a wrap is collapsed to one space.
    /// A trailing ASCII hyphen is preserved and joined without an inserted space.
    /// Null input produces an empty string; whitespace away from wraps is unchanged.
    /// </summary>
    public static string JoinWrappedLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                result.Append(text[i++]);
                continue;
            }

            var start = i;
            var hasLineBreak = false;
            var hasHorizontalSpace = false;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                var isLineBreak = text[i++] is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';
                hasLineBreak |= isLineBreak;
                hasHorizontalSpace |= !isLineBreak;
            }

            if (!hasLineBreak)
            {
                result.Append(text, start, i - start);
                continue;
            }

            // Leading/trailing wraps do not introduce spaces.
            if (start == 0 || i == text.Length)
                continue;

            var before = GetBoundaryBaseRune(text.AsSpan(0, start), fromEnd: true);
            var after = GetBoundaryBaseRune(text.AsSpan(i), fromEnd: false);
            if (hasHorizontalSpace ||
                (before.Value != '-' && !UsesUnspacedScript(before) && !UsesUnspacedScript(after)))
            {
                result.Append(' ');
            }
        }

        return result.ToString();
    }

    private static Rune GetBoundaryBaseRune(ReadOnlySpan<char> text, bool fromEnd)
    {
        while (!text.IsEmpty)
        {
            Rune value;
            int consumed;
            if (fromEnd)
                Rune.DecodeLastFromUtf16(text, out value, out consumed);
            else
                Rune.DecodeFromUtf16(text, out value, out consumed);

            // Ignore modifiers only for boundary classification; preserve them in the output.
            if (!IsBoundaryModifier(value))
                return value;

            text = fromEnd ? text[..^consumed] : text[consumed..];
        }

        return Rune.ReplacementChar;
    }

    private static bool IsBoundaryModifier(Rune value) =>
        value.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF or
            0x200C or 0x200D || // Zero-width non-joiner and joiner
        Rune.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool UsesUnspacedScript(Rune value) => value.Value is
        >= 0x0E00 and <= 0x0EFF or // Thai and Lao
        >= 0x1000 and <= 0x109F or // Myanmar
        >= 0x1780 and <= 0x17FF or // Khmer
        >= 0x3000 and <= 0x30FF or // CJK punctuation, hiragana and katakana
        >= 0x31F0 and <= 0x31FF or // Katakana extensions
        >= 0x3400 and <= 0x4DBF or // CJK Extension A
        >= 0x4E00 and <= 0x9FFF or // CJK unified ideographs
        >= 0xF900 and <= 0xFAFF or // CJK compatibility ideographs
        >= 0xFE30 and <= 0xFE4F or // CJK compatibility forms
        >= 0xFF01 and <= 0xFF60
            and not (>= 0xFF10 and <= 0xFF19)
            and not (>= 0xFF21 and <= 0xFF3A)
            and not (>= 0xFF41 and <= 0xFF5A) or // Fullwidth forms excluding Latin letters and digits
        >= 0xFF61 and <= 0xFF9F or // Halfwidth katakana and punctuation
        >= 0xFFE0 and <= 0xFFE6 or // Fullwidth symbols
        >= 0x1AFF0 and <= 0x1AFFF or // Kana Extended-B
        >= 0x1B000 and <= 0x1B0FF or // Kana Supplement
        >= 0x1B100 and <= 0x1B12F or // Kana Extended-A
        >= 0x1B130 and <= 0x1B16F or // Small Kana Extension
        >= 0x20000 and <= 0x2FA1F or // Supplementary CJK ideographs
        >= 0x30000 and <= 0x323AF; // CJK Extensions G and H
}
