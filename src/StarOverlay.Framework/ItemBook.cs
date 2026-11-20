namespace StarOverlay.Framework;

/// <summary>What the game table says about an item: name, quality (1 = plainest), description and icon (the game asset path of its picture).</summary>
public sealed record ItemEntry(int Id, string Name, int Quality, string Description, string Icon = "");

/// <summary>The items of the game table by id, filled by the service when it reads the tables; the chat looks loot and item links up here.</summary>
public static class ItemBook
{
    static volatile IReadOnlyDictionary<int, ItemEntry> _entries = new Dictionary<int, ItemEntry>();

    public static int Count => _entries.Count;

    public static void Set(IReadOnlyDictionary<int, ItemEntry> entries) => _entries = entries;

    public static ItemEntry? Find(int id) => _entries.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>The name for the chat: the table's name, or "아이템 &lt;id&gt;" when the table does not have it.</summary>
    public static string NameOf(int id) => Find(id)?.Name is { Length: > 0 } name ? name : "아이템 " + id;
}
