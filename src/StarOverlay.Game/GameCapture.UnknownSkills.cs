using System.Text;
using System.Text.Json;

namespace StarOverlay.Game;

public sealed partial class GameCapture
{
    sealed class UnknownSkill
    {
        public string Hint { get; set; } = "";
        public long Hits { get; set; }
        public long Damage { get; set; }
        public SortedSet<string> Jobs { get; set; } = [];
        public string LastSeen { get; set; } = "";
    }

    // Damage ids that have no name (a level row without a name of its own...): they are shown as "미확인 <id>" and collected here
    // (with what hints at them) so they can be labeled by hand later. The file keeps adding up over the sessions.
    static readonly Dictionary<int, UnknownSkill> Unknown = [];
    static bool _unknownDirty;

    static void NoteUnknownSkill(int id, Fighter fighter, long damage)
    {
        lock (Unknown)
        {
            if (!Unknown.TryGetValue(id, out var entry))
            {
                entry = new UnknownSkill { Hint = HintFor(id) };
                Unknown[id] = entry;
            }

            entry.Hits++;
            entry.Damage += damage;
            var job = fighter.ProfessionId != 0 ? fighter.ProfessionId.ToString() + (fighter.Spec.Length > 0 ? "/" + fighter.Spec : "") : "";
            if (job.Length > 0) entry.Jobs.Add(job);
            _unknownDirty = true;
        }
    }

    /// <summary>What the game tables say about the id: the skill a level row belongs to, with that skill's name.</summary>
    static string HintFor(int id)
    {
        if (!_levelOwners.TryGetValue(id, out var owner)) return "";
        return _ownerHints.TryGetValue(owner, out var name) && name.Length > 0 ? $"스킬 {owner}({name})의 레벨 행" : $"스킬 {owner}의 레벨 행";
    }

    string UnknownSkillsFile => Path.Combine(_game, "bpsr", "state", "unknown-skills.json");

    bool _unknownLoaded;

    void FlushUnknownSkills()
    {
        try
        {
            lock (Unknown)
            {
                if (!_unknownDirty) return;
                _unknownDirty = false;
                if (!_unknownLoaded)
                {
                    _unknownLoaded = true;
                    Merge(Unknown, UnknownSkillsFile);
                }

                var folder = Path.GetDirectoryName(UnknownSkillsFile)!;
                Directory.CreateDirectory(folder);
                var rows = Unknown.OrderByDescending(pair => pair.Value.Damage).ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => new { hint = pair.Value.Hint, hits = pair.Value.Hits, damage = pair.Value.Damage, jobs = pair.Value.Jobs, lastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                File.WriteAllText(UnknownSkillsFile, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>What an earlier session wrote is added to what this session collected (the first flush of the session).</summary>
    static void Merge(Dictionary<int, UnknownSkill> current, string path)
    {
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out var id)) continue;
            var row = entry.Value;
            if (!current.TryGetValue(id, out var skill))
            {
                skill = new UnknownSkill();
                current[id] = skill;
            }

            if (skill.Hint.Length == 0 && row.TryGetProperty("hint", out var hint)) skill.Hint = hint.GetString() ?? "";
            if (row.TryGetProperty("hits", out var hits) && hits.TryGetInt64(out var h)) skill.Hits += h;
            if (row.TryGetProperty("damage", out var damage) && damage.TryGetInt64(out var d)) skill.Damage += d;
            if (row.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array)
                foreach (var job in jobs.EnumerateArray()) skill.Jobs.Add(job.GetString() ?? "");
        }
    }
}
