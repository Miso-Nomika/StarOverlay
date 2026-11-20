using System.Text;

namespace StarOverlay.Framework;

public static class JapaneseScript
{
    private static readonly Lazy<Lexicon> Words = new(Load);

    public static bool IsJapanese(string text)
    {
        var kana = false;
        var kanji = false;
        var hangul = false;
        foreach (var ch in text)
        {
            if (IsKana(ch)) kana = true;
            else if (IsKanji(ch)) kanji = true;
            else if (IsHangul(ch)) hangul = true;
        }

        if (kana) return true;
        return kanji && !hangul;
    }

    public static string Romaji(string text)
    {
        var lexicon = Words.Value;
        var sb = new StringBuilder();
        var afterWord = false;
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                afterWord = false;
                i++;
                continue;
            }

            if (IsPunct(ch))
            {
                sb.Append(MapPunct(ch));
                afterWord = false;
                i++;
                continue;
            }

            if (lexicon.Starts.Contains(ch) && TryWord(text, i, lexicon, out var reading, out var consumed))
            {
                AppendToken(sb, ToRomaji(reading), false);
                afterWord = true;
                i += consumed;
                continue;
            }

            if (IsKana(ch))
            {
                var start = i;
                while (i < text.Length && IsKana(text[i])) i++;
                var hira = ToHiragana(text.AsSpan(start, i - start));
                AppendToken(sb, ToRomaji(hira), afterWord || IsParticle(hira));
                afterWord = false;
                continue;
            }

            if (IsLatin(ch))
            {
                var start = i;
                while (i < text.Length && IsLatin(text[i])) i++;
                AppendToken(sb, text.Substring(start, i - start), false);
                afterWord = false;
                continue;
            }

            AppendToken(sb, ch.ToString(), false);
            afterWord = false;
            i++;
        }

        return sb.ToString().Trim();
    }

    private static bool TryWord(string text, int index, Lexicon lexicon, out string reading, out int consumed)
    {
        var limit = Math.Min(lexicon.Max, text.Length - index);
        for (var length = limit; length >= 1; length--)
        {
            if (lexicon.Map.TryGetValue(text.Substring(index, length), out reading!))
            {
                consumed = length;
                return true;
            }
        }

        reading = "";
        consumed = 0;
        return false;
    }

    private static void AppendToken(StringBuilder sb, string value, bool glue)
    {
        if (value.Length == 0) return;
        if (!glue && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        sb.Append(value);
    }

    private static string ToRomaji(string hira)
    {
        var sb = new StringBuilder(hira.Length * 2);
        for (var i = 0; i < hira.Length;)
        {
            var ch = hira[i];
            if (ch == 'っ')
            {
                var mora = NextMora(hira, i + 1, out var used);
                if (used == 0)
                {
                    i++;
                    continue;
                }

                if (mora.Length > 0 && mora[0] is >= 'b' and <= 'z') sb.Append(mora[0]);
                sb.Append(mora);
                i += 1 + used;
                continue;
            }

            if (ch == 'ん')
            {
                var next = i + 1 < hira.Length ? hira[i + 1] : '\0';
                sb.Append(IsNasalVowel(next) ? "nn" : "n");
                i++;
                continue;
            }

            if (ch == 'ー')
            {
                if (sb.Length > 0)
                {
                    for (var back = sb.Length - 1; back >= 0; back--)
                    {
                        if (sb[back] is 'a' or 'i' or 'u' or 'e' or 'o')
                        {
                            sb.Append(sb[back]);
                            break;
                        }
                    }
                }

                i++;
                continue;
            }

            sb.Append(NextMora(hira, i, out var step));
            i += Math.Max(1, step);
        }

        return sb.ToString();
    }

    private static string NextMora(string hira, int index, out int used)
    {
        used = 0;
        if (index >= hira.Length) return "";
        if (index + 1 < hira.Length && Digraph.TryGetValue(hira.Substring(index, 2), out var pair))
        {
            used = 2;
            return pair;
        }

        if (Mora.TryGetValue(hira[index], out var one))
        {
            used = 1;
            return one;
        }

        used = 1;
        var ch = hira[index];
        return ch == 'ー' ? "" : ch.ToString();
    }

    private static string ToHiragana(ReadOnlySpan<char> text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var code = (int)ch;
            sb.Append(code is >= 0x30A1 and <= 0x30F6 ? (char)(code - 0x60) : ch);
        }

        return sb.ToString();
    }

    private static bool IsParticle(string hira) => hira is
        "は" or "が" or "を" or "に" or "へ" or "と" or "で" or "も" or "の" or "ね" or "よ" or "か" or "わ" or "さ" or "ぞ" or "ぜ" or "な"
        or "から" or "まで" or "より" or "って" or "じゃ";

    private static bool IsNasalVowel(char ch) => ch is
        'あ' or 'い' or 'う' or 'え' or 'お' or 'や' or 'ゆ' or 'よ'
        or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ゃ' or 'ゅ' or 'ょ' or 'ん';

    private static bool IsKana(char ch)
    {
        var code = (int)ch;
        return code is >= 0x3041 and <= 0x3096 or >= 0x30A1 and <= 0x30FA || ch is 'ー' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ';
    }

    private static bool IsKanji(char ch)
    {
        var code = (int)ch;
        return code is >= 0x4E00 and <= 0x9FFF || ch == '々';
    }

    private static bool IsHangul(char ch)
    {
        var code = (int)ch;
        return code is >= 0xAC00 and <= 0xD7A3 or >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F;
    }

    private static bool IsLatin(char ch) => ch < 128 && (char.IsLetterOrDigit(ch) || ch is '\'' or '+' or '-');

    private static bool IsPunct(char ch) => ch is
        '。' or '、' or '！' or '？' or '!' or '?' or '.' or ',' or '…' or '‥' or '・'
        or '「' or '」' or '『' or '』' or '（' or '）' or '(' or ')' or '[' or ']' or '【' or '】'
        or '〜' or '～' or '~' or '：' or ':' or '；' or ';' or '"' or '\'' or '※';

    private static char MapPunct(char ch) => ch switch
    {
        '？' or '?' => '?',
        '！' or '!' => '!',
        '。' or '.' => '.',
        '、' or ',' => ',',
        '〜' or '～' or '~' => '~',
        _ => ch,
    };

    private static Lexicon Load()
    {
        var map = new Dictionary<string, string>(230000, StringComparer.Ordinal);
        var starts = new HashSet<char>();
        var max = 1;
        using var stream = typeof(JapaneseScript).Assembly.GetManifestResourceStream("StarOverlay.Framework.Data.jp-lexicon.txt");
        if (stream is null) return new Lexicon(map, starts, max);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1) continue;
            var surface = line[..tab];
            map.TryAdd(surface, line[(tab + 1)..]);
            starts.Add(surface[0]);
            if (surface.Length > max) max = surface.Length;
        }

        return new Lexicon(map, starts, max);
    }

    private sealed record Lexicon(Dictionary<string, string> Map, HashSet<char> Starts, int Max);

    private static readonly Dictionary<string, string> Digraph = new(StringComparer.Ordinal)
    {
        ["きゃ"] = "kya", ["きゅ"] = "kyu", ["きょ"] = "kyo",
        ["ぎゃ"] = "gya", ["ぎゅ"] = "gyu", ["ぎょ"] = "gyo",
        ["しゃ"] = "sha", ["しゅ"] = "shu", ["しょ"] = "sho",
        ["じゃ"] = "ja", ["じゅ"] = "ju", ["じょ"] = "jo",
        ["ちゃ"] = "cha", ["ちゅ"] = "chu", ["ちょ"] = "cho",
        ["にゃ"] = "nya", ["にゅ"] = "nyu", ["にょ"] = "nyo",
        ["ひゃ"] = "hya", ["ひゅ"] = "hyu", ["ひょ"] = "hyo",
        ["びゃ"] = "bya", ["びゅ"] = "byu", ["びょ"] = "byo",
        ["ぴゃ"] = "pya", ["ぴゅ"] = "pyu", ["ぴょ"] = "pyo",
        ["みゃ"] = "mya", ["みゅ"] = "myu", ["みょ"] = "myo",
        ["りゃ"] = "rya", ["りゅ"] = "ryu", ["りょ"] = "ryo",
        ["ふぁ"] = "fa", ["ふぃ"] = "fi", ["ふぇ"] = "fe", ["ふぉ"] = "fo",
        ["てぃ"] = "ti", ["でぃ"] = "di",
        ["うぃ"] = "wi", ["うぇ"] = "we", ["うぉ"] = "wo",
    };

    private static readonly Dictionary<char, string> Mora = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "wo",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['だ'] = "da", ['ぢ'] = "di", ['づ'] = "du", ['で'] = "de", ['ど'] = "do",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
        ['ゃ'] = "ya", ['ゅ'] = "yu", ['ょ'] = "yo", ['ゎ'] = "wa",
    };
}
