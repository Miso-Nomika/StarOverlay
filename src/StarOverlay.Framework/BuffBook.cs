namespace StarOverlay.Framework;

/// <summary>What the game table says about a buff: its name, its picture (a game asset path) and how long it lasts.</summary>
public sealed record BuffEntry(int Id, string Name, double Duration, string Description);

/// <summary>The buffs of the game table by uid, filled by the service when it reads the tables; plugins look a uid up here.</summary>
public static class BuffBook
{
    static volatile IReadOnlyDictionary<int, BuffEntry> _entries = new Dictionary<int, BuffEntry>();

    public static int Count => _entries.Count;

    public static void Set(IReadOnlyDictionary<int, BuffEntry> entries) => _entries = entries;

    public static BuffEntry? Find(int uid) => _entries.TryGetValue(uid, out var entry) ? entry : null;
}
