namespace StarOverlay.Ingame.Protocol;

/// <summary>
/// Pairs the picture lines of our chat with the picture bubbles the game draws, to copy the right picture for each line. Every picture line
/// takes its turn (also the ones whose picture is already copied): a bubble of a sender that is already shown must not be handed to a
/// newer line of the same sender, or that line would get the wrong picture and keep it.
/// </summary>
public static class StickerPairing
{
    // the picture's name in the game: sticker_17_317003, emoji_2_2012005, sticker_ip003_714007 (the middle part may carry letters too)
    static readonly System.Text.RegularExpressions.Regex Code = new(@"[A-Za-z]+_[A-Za-z0-9]+_\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The sticker name inside a name or a chat text; null when there is none. The "%s" markers of the game's text are dropped first ("%ssticker_..." would read "ssticker_...").</summary>
    public static string FindName(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = Code.Match(text.Replace("%s", ""));
        return match.Success ? match.Value : null;
    }

    /// <summary>The sticker name of a picture chat message ("emojiPic=%s=%ssticker_ip003_714007"); null when the line is not a picture message.</summary>
    public static string NameOf(string text) =>
        text != null && text.StartsWith("emojiPic=", StringComparison.Ordinal) ? FindName(text) : null;

    /// <param name="lineSenders">The senders of all picture lines, newest first.</param>
    /// <param name="bubbleSenders">The senders of the game's picture bubbles, oldest first (top of the screen first).</param>
    /// <returns>For every line the index of its bubble, or -1 when it has none (the game's list no longer holds it).</returns>
    public static int[] Pair(IReadOnlyList<string> lineSenders, IReadOnlyList<string> bubbleSenders)
    {
        // every sender has a row of their own: the newest line of a sender takes the newest bubble of that sender, the next line the one before, and so on
        var bubbles = new Dictionary<string, Stack<int>>(StringComparer.Ordinal);
        for (var i = 0; i < bubbleSenders.Count; i++)
        {
            if (!bubbles.TryGetValue(bubbleSenders[i], out var stack)) bubbles[bubbleSenders[i]] = stack = new Stack<int>();
            stack.Push(i); // the newest ends up on top
        }

        var result = new int[lineSenders.Count];
        for (var i = 0; i < lineSenders.Count; i++)
            result[i] = bubbles.TryGetValue(lineSenders[i], out var own) && own.Count > 0 ? own.Pop() : -1;

        return result;
    }
}
