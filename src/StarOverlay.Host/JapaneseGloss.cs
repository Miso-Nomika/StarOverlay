using System.Collections.Concurrent;
using System.Text;
using StarOverlay.Framework;

namespace StarOverlay.Host;

internal static class JapaneseGloss
{
    private static readonly ConcurrentDictionary<string, string> Korean = new(StringComparer.Ordinal);
    private static int _revision;
    private static int _listening;
    private static long _afterTicks = DateTime.MinValue.Ticks;

    public static int Revision => Volatile.Read(ref _revision);

    public static void Listen()
    {
        if (Interlocked.Exchange(ref _listening, 1) != 0) return;
        TranslationPrompt.Changed += () => Volatile.Write(ref _afterTicks, DateTime.Now.Ticks);
        ChatGloss.Changed += Touch;
    }

    public static void Touch() => Interlocked.Increment(ref _revision);

    public static void Store(string text, string korean)
    {
        if (korean.Length == 0) return;
        if (Korean.TryAdd(text, korean)) Interlocked.Increment(ref _revision);
    }

    /// <summary>Text plus romaji and Korean lines, as the overlay chat shows it.</summary>
    public static string Body(string text, DateTime at)
    {
        var (romaji, korean) = Parts(text, at);
        if (romaji.Length == 0 && korean.Length == 0) return text;
        var body = new StringBuilder(text);
        if (romaji.Length > 0) body.Append('\n').Append(romaji);
        if (korean.Length > 0) body.Append('\n').Append(korean);
        return body.ToString();
    }

    /// <summary>
    /// Romaji and Korean translation for a Japanese line, separately (the in-game window draws them as its own lines).
    /// Empty strings when the text is not Japanese, the option is off, or the translation is not ready yet
    /// (it is requested here and appears on a later call).
    /// </summary>
    public static (string Romaji, string Korean) Parts(string text, DateTime at)
    {
        Listen();
        if (!JapaneseScript.IsJapanese(text)) return ("", "");
        var showIme = ChatGloss.ShowIme;
        var showTranslation = ChatGloss.ShowTranslation;
        if (!showIme && !showTranslation) return ("", "");

        var romaji = "";
        if (showIme)
        {
            try
            {
                romaji = JapaneseScript.Romaji(text);
            }
            catch (Exception)
            {
                romaji = "";
            }

            if (string.Equals(romaji, text, StringComparison.Ordinal)) romaji = "";
        }

        if (!showTranslation) return (romaji, "");
        if (Korean.TryGetValue(text, out var korean) && korean.Length > 0) return (romaji, korean);
        if (at.Ticks >= Volatile.Read(ref _afterTicks))
            QwenTranslator.Request(text);
        return (romaji, "");
    }
}
