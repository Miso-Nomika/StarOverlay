namespace StarOverlay.Framework;

/// <summary>One chat line. <paramref name="World"/> is the world chat channel number a "월드" line belongs to (0 = not a world line or not known).</summary>
/// <summary>An item linked in a chat message.</summary>
public sealed record ChatLink(int ItemId, long Count);

public sealed record ChatLineView(DateTime At, string Channel, string Name, string Text, int World = 0, IReadOnlyList<ChatLink>? Links = null);

public static class LiveChat
{
    const int Limit = 200;
    public const string WorldChannelName = "월드";
    static readonly object Gate = new();
    static readonly HashSet<(string, long, string, string)> _seen = [];
    static readonly Queue<(string, long, string, string)> _order = new();
    static ChatLineView[] _lines = [];
    static int _generation;
    static int _world;

    public static int Generation => Volatile.Read(ref _generation);

    public static IReadOnlyList<ChatLineView> Lines => Volatile.Read(ref _lines) ?? [];

    /// <summary>The world chat channel the game is on now (0 = not known yet). World lines arriving now belong to it.</summary>
    public static int WorldChannel => Volatile.Read(ref _world);

    public static void SetWorldChannel(int channel)
    {
        if (channel <= 0 || Interlocked.Exchange(ref _world, channel) == channel) return;
        Interlocked.Increment(ref _generation);
    }

    /// <summary>
    /// Adds a line. <paramref name="at"/> is when it was said (null = now); an older line (chat history) is placed among the
    /// newer ones by time. <paramref name="world"/> is the world channel of a history line (null = the channel the game is on now).
    /// </summary>
    public static bool Add(long messageId, string channel, string name, string text, DateTime? at = null, int? world = null, IReadOnlyList<ChatLink>? links = null)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        lock (Gate)
        {
            name = name.Trim();
            if (messageId != 0)
            {
                if (!_seen.Add((channel, messageId, name, text))) return false;
                _order.Enqueue((channel, messageId, name, text));
                while (_order.Count > 400)
                    _seen.Remove(_order.Dequeue());
            }

            var channelNumber = channel == WorldChannelName ? world ?? WorldChannel : 0;
            var line = new ChatLineView(at ?? DateTime.Now, channel, name, text, channelNumber, links is { Count: > 0 } ? links : null);
            var index = _lines.Length;
            while (index > 0 && _lines[index - 1].At > line.At) index--; // history goes before the newer lines
            var next = new ChatLineView[_lines.Length + 1];
            Array.Copy(_lines, 0, next, 0, index);
            next[index] = line;
            Array.Copy(_lines, index, next, index + 1, _lines.Length - index);
            if (next.Length > Limit)
                next = next[^Limit..];
            Volatile.Write(ref _lines, next);
            Interlocked.Increment(ref _generation);
            return true;
        }
    }

    public static string ChannelName(int type) => type switch
    {
        1 => WorldChannelName,
        2 => "필드",
        3 => "파티",
        4 => "길드",
        5 => "귓속말",
        6 => "그룹",
        7 => "공지",
        8 => "콘텐츠",
        9 => "뉴비",
        99 => "시스템",
        _ => "채팅",
    };

    public static void Clear()
    {
        lock (Gate)
        {
            _seen.Clear();
            _order.Clear();
            Volatile.Write(ref _lines, []);
            Interlocked.Increment(ref _generation);
        }
    }
}
