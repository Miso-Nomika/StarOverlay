using System.Text.Json;

namespace StarOverlay.Game;

/// <summary>
/// What a worn item tells about its owner: the two extra stats of an accessory and the job of a weapon or armor piece. The data that ships with the
/// program covers the items of the older seasons; the game's own tables (saved by the agent) are read as well, so the items of a new season are
/// known as soon as the tables are there. A table's answer wins over the shipped one.
/// </summary>
static class AccessoryGear
{
    static readonly Dictionary<int, (string First, string Second)> Options = [];
    static readonly Dictionary<int, int> Professions = [];
    static volatile Dictionary<int, (string First, string Second)> _liveOptions = [];
    static volatile Dictionary<int, int> _liveProfessions = [];

    // the effect of an attribute library entry (the id the game gives it) and the stat it is
    static readonly Dictionary<int, string> EffectNames = new()
    {
        [11112] = "치명",
        [11122] = "속공",
        [11132] = "행운",
        [11142] = "숙련",
        [11152] = "만능",
    };

    // the parts of the four accessories (earring, necklace, ring, talisman); the bracelets do not count for the judgement
    static bool IsAccessoryPart(int part) => part is 205 or 206 or 207 or 210;

    // the parts that tell a job: the weapon (200) and the armor (201-204)
    static bool IsJobPart(int part) => part is >= 200 and <= 204;

    /// <summary>
    /// Works the options and jobs out of the game's equipment tables. An accessory's two extra stats are the effects of the two attribute libraries
    /// named in its row, when each library has exactly one effect (an item whose stats can be changed has several, and is left out). A weapon or an armor
    /// piece made for one job tells that job. Returns how many accessories are known from the tables (0 when the files are missing or unreadable).
    /// </summary>
    public static int UseTables(string equipPath, string libraryPath)
    {
        try
        {
            if (!File.Exists(equipPath) || !File.Exists(libraryPath)) return 0;
            var effects = ReadLibraryEffects(libraryPath);
            var options = new Dictionary<int, (string, string)>();
            var professions = new Dictionary<int, int>();
            using var stream = File.OpenRead(equipPath);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id)) continue;
                var row = entry.Value;
                var part = row.TryGetProperty("EquipPart", out var partElement) && partElement.TryGetInt32(out var p) ? p : 0;
                if (IsJobPart(part)
                    && row.TryGetProperty("EquipProfession", out var jobs) && jobs.ValueKind == JsonValueKind.Array && jobs.GetArrayLength() == 1
                    && jobs[0].TryGetInt32(out var job) && job > 0)
                    professions[id] = job;
                if (!IsAccessoryPart(part)
                    || !row.TryGetProperty("AdvancedAttrLibId", out var libs) || libs.ValueKind != JsonValueKind.Array || libs.GetArrayLength() < 3)
                    continue;
                if (libs[1].TryGetInt32(out var firstLib) && libs[2].TryGetInt32(out var secondLib)
                    && effects.TryGetValue(firstLib, out var firstEffect) && EffectNames.TryGetValue(firstEffect, out var first)
                    && effects.TryGetValue(secondLib, out var secondEffect) && EffectNames.TryGetValue(secondEffect, out var second))
                    options[id] = (first, second);
            }

            if (options.Count == 0 && professions.Count == 0) return 0;
            _liveOptions = options;
            _liveProfessions = professions;
            return options.Count;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Library id → the one effect all its rows give; a library with another shape (several effects, different rows) is not in the map.</summary>
    static Dictionary<int, int> ReadLibraryEffects(string path)
    {
        var single = new Dictionary<int, int>();
        var broken = new HashSet<int>();
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            var row = entry.Value;
            if (!row.TryGetProperty("AttrLibId", out var libElement) || !libElement.TryGetInt32(out var lib)) continue;
            if (broken.Contains(lib)) continue;
            var effect = 0;
            if (row.TryGetProperty("AttrEffect", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == 1
                && list[0].ValueKind == JsonValueKind.Array && list[0].GetArrayLength() == 2 && list[0][1].TryGetInt32(out var id))
                effect = id;
            if (effect == 0 || (single.TryGetValue(lib, out var known) && known != effect))
            {
                single.Remove(lib);
                broken.Add(lib);
                continue;
            }

            single[lib] = effect;
        }

        return single;
    }

    static AccessoryGear()
    {
        using var stream = typeof(AccessoryGear).Assembly.GetManifestResourceStream("StarOverlay.Game.Data.AccessoryOptions.txt");
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var part = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (part.Length == 4 && part[0] == "A" && int.TryParse(part[1], out var id))
                Options[id] = (part[2], part[3]);
            else if (part.Length == 3 && part[0] == "P" && int.TryParse(part[1], out var equip) && int.TryParse(part[2], out var job))
                Professions[equip] = job;
        }
    }

    public static bool TryOptions(int equipId, out string first, out string second)
    {
        if (_liveOptions.TryGetValue(equipId, out var live))
        {
            first = live.First;
            second = live.Second;
            return true;
        }

        if (Options.TryGetValue(equipId, out var pair))
        {
            first = pair.First;
            second = pair.Second;
            return true;
        }

        first = "";
        second = "";
        return false;
    }

    public static bool TryProfession(int equipId, out int profession) =>
        _liveProfessions.TryGetValue(equipId, out profession) || Professions.TryGetValue(equipId, out profession);
}
