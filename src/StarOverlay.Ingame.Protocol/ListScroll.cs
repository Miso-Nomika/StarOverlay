namespace StarOverlay.Ingame.Protocol;

/// <summary>How far a list of rows that is scrolled row by row can go: not past the point where the rest already fits in the window.</summary>
public static class ListScroll
{
    /// <param name="heights">The height of every row.</param>
    /// <param name="gap">The space between two rows.</param>
    /// <param name="window">The height the rows are shown in.</param>
    /// <returns>The first row that can be at the top; 0 when everything fits.</returns>
    public static int Max(IReadOnlyList<double> heights, double gap, double window)
    {
        var first = heights.Count;
        var used = 0.0;
        for (var i = heights.Count - 1; i >= 0; i--)
        {
            used += heights[i] + gap;
            if (used - gap > window) break;
            first = i;
        }

        return Math.Max(0, Math.Min(first, heights.Count - 1));
    }
}
