using System.Text;
using System.Text.RegularExpressions;

namespace StarOverlay.Framework;

/// <summary>
/// Turns what the translation model answered into one chat-ready line: no thinking text, no label, no emoji the game font
/// cannot draw, no runaway repetition. Pure text work, so it can be tested without a model.
/// </summary>
public static partial class TranslationText
{
    const int MaxRepeat = 4; // "ㅋㅋㅋㅋ" is a laugh, a hundred of them is a model stuck in a loop

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex Think();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The line to show, or an empty string when the answer is empty or just the source text again.</summary>
    public static string Clean(string? answer, string source)
    {
        var text = Think().Replace(answer ?? "", "").Replace("<think>", "", StringComparison.Ordinal).Replace("</think>", "", StringComparison.Ordinal);
        text = string.Join(' ', text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)); // every line counts, not only the first
        text = StripEmoji(text);
        text = Spaces().Replace(text, " ").Trim();
        text = text.Trim('"', '\'', '「', '」', '『', '』', '“', '”', ' ');
        if (text.StartsWith("번역:", StringComparison.Ordinal) || text.StartsWith("번역：", StringComparison.Ordinal)) text = text[3..].Trim();
        text = text.Trim('"', '\'', '「', '」', '『', '』', '“', '”', ' ');
        text = CollapseRepeats(text);
        return text.Length == 0 || text == source ? "" : text;
    }

    static string StripEmoji(string text)
    {
        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsEmoji(rune.Value)) kept.Append(rune.ToString());
        }

        return kept.ToString();
    }

    static bool IsEmoji(int value) => value switch
    {
        >= 0x1F000 and <= 0x1FAFF => true, // pictographs, faces, flags, skin tones
        >= 0x2700 and <= 0x27BF => true,   // dingbats (✨ ❤ ✌)
        >= 0x2B50 and <= 0x2B55 => true,   // ⭐ ⭕
        0xFE0F or 0x200D or 0x20E3 => true, // emoji joiners and variation marks
        0x2605 or 0x2606 or 0x2661 or 0x2665 or 0x266A or 0x266B => false, // ★ ☆ ♡ ♥ ♪ ♫ are plain text marks
        >= 0x2600 and <= 0x26FF => true,   // weather, faces, signs (☀ ☺ ⚡)
        _ => false,
    };

    static string CollapseRepeats(string text)
    {
        var result = new StringBuilder(text.Length);
        var count = 0;
        var previous = '\0';
        foreach (var ch in text)
        {
            count = ch == previous ? count + 1 : 1;
            previous = ch;
            if (count > MaxRepeat && !char.IsDigit(ch)) continue; // long numbers stay whole
            result.Append(ch);
        }

        return result.ToString();
    }
}
