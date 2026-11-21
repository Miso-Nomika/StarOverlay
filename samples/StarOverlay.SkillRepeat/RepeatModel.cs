using System.Text.Json;
using StarOverlay.Framework;

namespace StarOverlay.PreInput;

/// <summary>
/// The data of the "선입력" window: the skills the player has used (shown as rows with their picture, in the order they were first used) and
/// which of them are switched on. Both lists are kept in the plugin settings as JSON arrays of game skill ids.
/// A row stands for a whole skill: the steps of a combo, the enhanced version and the other forms the tables link to it (<see cref="SkillBook.FamilyOf"/>)
/// all belong to it, so switching the row on repeats all of them. The row is named and pictured by the lowest id of the family.
/// </summary>
public sealed class RepeatModel(Func<string, string?> read, Action<string, string> write, string libraryDirectory, Func<long>? nowMs = null)
{
    public const string OnKey = "skills";
    public const string SeenKey = "seen";
    public const string ShownKey = "shown";
    public const int MaxSeen = 16;
    const int OnColor = 0x66BB6A;

    private readonly object _lock = new();
    private readonly HashSet<int> _confirmed = []; // families whose shown skill was really pressed in this session
    private readonly Dictionary<int, long> _removedAt = []; // when a family was deleted: only a press after that brings it back

    private List<int> Load(string key)
    {
        try
        {
            var text = read(key);
            if (!string.IsNullOrWhiteSpace(text))
                return (JsonSerializer.Deserialize<List<int>>(text) ?? []).Where(id => id > 0).Select(SkillBook.FamilyOf).Distinct().ToList(); // an id stored before the tables were read is turned into its family now
        }
        catch (JsonException)
        {
        }

        return [];
    }

    private void Save(string key, List<int> ids) => write(key, JsonSerializer.Serialize(ids));

    /// <summary>Which form of each family the row shows (family id to the skill id the player really uses).</summary>
    private Dictionary<int, int> LoadShown()
    {
        try
        {
            var text = read(ShownKey);
            if (!string.IsNullOrWhiteSpace(text))
                return (JsonSerializer.Deserialize<Dictionary<int, int>>(text) ?? []).Where(pair => pair.Value > 0).ToDictionary(pair => SkillBook.FamilyOf(pair.Key), pair => pair.Value, null);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
        }

        return [];
    }

    /// <summary>The skills (their families) that are switched on.</summary>
    public IReadOnlyList<int> On()
    {
        lock (_lock) return Load(OnKey);
    }

    /// <summary>Every skill id that has to be repeated: all the ids of every family that is switched on.</summary>
    public IReadOnlyList<int> OnIds() => On().SelectMany(SkillBook.MembersOf).Distinct().ToArray();

    /// <summary>The skills used so far, in the order of their first use.</summary>
    public IReadOnlyList<int> Seen()
    {
        lock (_lock) return Load(SeenKey);
    }

    /// <summary>Adds the skills pressed lately that are not in the list yet (see the other overload).</summary>
    public void Observe(IEnumerable<int> pressedOldestFirst) => Observe(pressedOldestFirst.Select(id => (id, long.MaxValue)));

    /// <summary>
    /// Adds the skills pressed lately that are not in the list yet; beyond 16 the oldest one that is switched off makes room. A skill the player
    /// deleted comes back only when it is pressed again after the deletion (a press from before is not counted).
    /// </summary>
    public void Observe(IEnumerable<(int Skill, long Seen)> pressedOldestFirst)
    {
        lock (_lock)
        {
            var seen = Load(SeenKey);
            var on = Load(OnKey);
            var changed = false;
            var shown = LoadShown();
            var shownChanged = false;
            foreach (var id in on.Where(id => !seen.Contains(id)))
            {
                seen.Insert(0, id); // a skill that is on (from an earlier choice) must be in the list too, or it could not be switched off
                changed = true;
            }

            foreach (var (pressed, seenAt) in pressedOldestFirst)
            {
                if (pressed <= 0) continue;
                var id = SkillBook.FamilyOf(pressed); // every form of a skill gets the one row
                if (_removedAt.TryGetValue(id, out var removedAt) && seenAt <= removedAt) continue;
                // the row shows the form the player really uses: the first one pressed; it only changes when the shown one was not pressed
                // in this session (the player now uses another form of the skill, e.g. after a change of the skill)
                if (!shown.TryGetValue(id, out var current) || (current != pressed && !_confirmed.Contains(id)))
                {
                    shown[id] = pressed;
                    shownChanged = true;
                    current = pressed;
                }

                if (current == pressed) _confirmed.Add(id);
                if (seen.Contains(id)) continue;
                seen.Add(id);
                changed = true;
            }

            while (seen.Count > MaxSeen)
            {
                var victim = seen.FindIndex(id => !on.Contains(id));
                if (victim < 0) break; // all of them are on: none is dropped
                seen.RemoveAt(victim);
                changed = true;
            }

            if (changed) Save(SeenKey, seen);
            if (shownChanged) write(ShownKey, JsonSerializer.Serialize(shown));
        }
    }

    /// <summary>Switches a skill of the list on or off.</summary>
    public void Toggle(string key)
    {
        lock (_lock)
        {
            if (!int.TryParse(key, out var id)) return;
            id = SkillBook.FamilyOf(id);
            if (!Load(SeenKey).Contains(id)) return;
            var on = Load(OnKey);
            if (!on.Remove(id)) on.Add(id);
            Save(OnKey, on);
        }
    }

    /// <summary>Deletes a skill of the list (the delete button of its row): it is no longer shown or repeated until the player uses it again.</summary>
    public void Remove(string key)
    {
        lock (_lock)
        {
            if (!int.TryParse(key, out var id)) return;
            id = SkillBook.FamilyOf(id);
            var seen = Load(SeenKey);
            if (!seen.Remove(id)) return;
            var on = Load(OnKey);
            on.Remove(id);
            var shown = LoadShown();
            shown.Remove(id);
            _confirmed.Remove(id);
            _removedAt[id] = (nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))();
            Save(SeenKey, seen);
            Save(OnKey, on);
            write(ShownKey, JsonSerializer.Serialize(shown));
        }
    }

    /// <summary>One row per used skill: the picture, the name and "켜짐"/"꺼짐" (a dimmed row is off).</summary>
    public IReadOnlyList<IconView> Rows()
    {
        List<int> seen, on;
        Dictionary<int, int> shown;
        lock (_lock)
        {
            seen = Load(SeenKey);
            on = Load(OnKey);
            shown = LoadShown();
        }

        return seen.Select(id =>
        {
            var enabled = on.Contains(id);
            var form = shown.GetValueOrDefault(id, id); // the name and the picture are those of the form the player uses
            return new IconView(id.ToString(), SkillBook.NameOf(form), Picture(form), "", "", 0, 0, false, "ring", OnColor, !enabled, enabled ? 100 : 45);
        }).ToArray();
    }

    // the folders of the picture library, in the order they are looked in: the skills of the weapons, then the imagine skills ("오의"·"비기")
    private static readonly string[] PictureFolders = ["Skills", "Imagines"];

    /// <summary>The picture file of the skill (the game's own picture, bundled with the plugin), or empty: the row then shows the first letter of the name.</summary>
    private string Picture(int id)
    {
        var icon = SkillBook.IconOf(id);
        if (icon.Length == 0 || icon.IndexOfAny(['/', '\\', ':']) >= 0 || icon.Contains("..") || libraryDirectory.Length == 0) return "";
        foreach (var folder in PictureFolders)
        {
            var path = Path.Combine(libraryDirectory, folder, icon + ".png");
            if (File.Exists(path)) return "file:" + path;
        }

        return "";
    }
}
