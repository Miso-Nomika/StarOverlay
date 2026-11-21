namespace StarOverlay.Ingame.Protocol;

/// <summary>
/// Pairs the picture lines of our chat with the picture bubbles the game draws, to copy the right picture for each line. Every picture line
/// takes its turn (also the ones whose picture is already copied): a bubble of a sender that is already shown must not be handed to a
/// newer line of the same sender, or that line would get the wrong picture and keep it.
/// </summary>
public static class StickerPairing
{
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
