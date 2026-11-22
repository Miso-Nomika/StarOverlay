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

    static readonly System.Text.RegularExpressions.Regex Parts = new(@"^([A-Za-z]+)_([A-Za-z0-9]+)_(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The picture textures of the game that can belong to a sticker name. The game names the texture "kind_pack_n" (sticker_17_317003 is drawn from
    /// sticker_17_3, emoji_2_412001 from emoji_2_4, sticker_3_118010 from sticker_3_11): kind and pack are those of the name, and n is the front part of the
    /// name's number without its last three digits. More than one can fit (n 1 and n 11 both fit 118010): the caller decides with what it knows.
    /// </summary>
    public static List<string> Candidates(string name, IEnumerable<string> textureNames)
    {
        var result = new List<string>();
        var front = FrontOf(name, out var kind, out var pack);
        if (front == null) return result;
        foreach (var texture in textureNames.Distinct(StringComparer.Ordinal))
        {
            var match = Parts.Match(texture ?? "");
            if (!match.Success || match.Groups[1].Value != kind || match.Groups[2].Value != pack) continue;
            var n = match.Groups[3].Value;
            if (n.Length > 0 && n[0] != '0' && front.Length > n.Length && front.StartsWith(n, StringComparison.Ordinal)) result.Add(texture);
        }

        return result;
    }

    /// <summary>What the number of a sticker name carries after the texture's n (11 + "8" for 118010): the series part, the same for every sticker of a series.</summary>
    public static string SeriesOf(string name, string texture)
    {
        var front = FrontOf(name, out _, out _);
        var match = Parts.Match(texture ?? "");
        return front == null || !match.Success || !front.StartsWith(match.Groups[3].Value, StringComparison.Ordinal) ? null : front[match.Groups[3].Value.Length..];
    }

    /// <summary>The kind and pack of a sticker name ("sticker_3" for sticker_3_118010); null when the name does not have the form kind_pack_number.</summary>
    public static string PackOf(string name)
    {
        var match = Parts.Match(name ?? "");
        return match.Success ? match.Groups[1].Value + "_" + match.Groups[2].Value : null;
    }

    /// <summary>The number of a sticker name without its last three digits ("317003" gives "317"); null when the name does not have the form kind_pack_number.</summary>
    static string FrontOf(string name, out string kind, out string pack)
    {
        kind = pack = null;
        var match = Parts.Match(name ?? "");
        if (!match.Success || match.Groups[3].Value.Length <= 3) return null;
        kind = match.Groups[1].Value;
        pack = match.Groups[2].Value;
        return match.Groups[3].Value[..^3];
    }

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
