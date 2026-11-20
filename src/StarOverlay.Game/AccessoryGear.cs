namespace StarOverlay.Game;

static class AccessoryGear
{
    static readonly Dictionary<int, (string First, string Second)> Options = [];
    static readonly Dictionary<int, int> Professions = [];

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
        Professions.TryGetValue(equipId, out profession);
}
