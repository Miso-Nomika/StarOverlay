namespace StarOverlay.Game;

/// <summary>
/// When a buff ends on this PC's clock. The game gives the creation time of a buff on the server's clock; the difference between the
/// two clocks is estimated as the smallest "arrival time minus creation time" seen (a late packet only makes it larger).
/// </summary>
internal sealed class BuffClock
{
    long _offset;
    bool _known;

    public long OffsetMs => _offset;

    /// <summary>A buff with this creation time (server clock) arrived now (this PC's clock).</summary>
    public void Observe(long serverMs, long localNowMs)
    {
        if (serverMs <= 0) return;
        var difference = localNowMs - serverMs;
        if (!_known || difference < _offset)
        {
            _offset = difference;
            _known = true;
        }
    }

    /// <summary>The end on this PC's clock in ms, or 0 for a buff without a duration (it never ends).</summary>
    public long EndsAt(long serverCreateMs, double durationSeconds, long localNowMs)
    {
        if (durationSeconds <= 0) return 0;
        var start = serverCreateMs > 0 && _known ? serverCreateMs + _offset : localNowMs;
        return start + (long)Math.Round(durationSeconds * 1000);
    }
}
