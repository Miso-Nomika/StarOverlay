using System.Globalization;
using System.Text.Json;
using StarOverlay.Framework;

namespace StarOverlay.DpsMeter;

public sealed partial class MeterSession
{
    private static readonly JsonSerializerOptions RecordJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Dictionary<string, Dictionary<string, SkillSnap>> _skillBaseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, BuffSnap>> _knownBuffs = new(StringComparer.Ordinal);
    private readonly List<EncounterView> _finished = [];
    private readonly Dictionary<string, (long Length, DateTime Write, EncounterHead Head)> _heads = new(StringComparer.OrdinalIgnoreCase);

    private string _selfName = "";
    private FightDraft? _draft;
    private DateTime _saved = DateTime.MinValue;

    public string Folder()
    {
        if (string.IsNullOrEmpty(_game)) return "";
        var folder = Path.Combine(_game, "bpsr", "records", "dps.meter");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public IReadOnlyList<EncounterView> Records()
    {
        var records = new List<EncounterView>();
        if (_draft is not null) records.Add(ToView(_draft));
        if (!string.IsNullOrEmpty(_game) && Directory.Exists(Folder()))
        {
            foreach (var file in Directory.GetFiles(Folder(), "*.json"))
            {
                FightDraft? saved;
                try
                {
                    saved = JsonSerializer.Deserialize<FightDraft>(File.ReadAllText(file), RecordJson);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    continue;
                }

                if (saved is null || string.IsNullOrEmpty(saved.Id)) continue;
                if (_draft is not null && saved.Id == _draft.Id) continue;
                records.Add(ToView(saved) with { Current = false });
            }
        }

        records.AddRange(_finished.Where(record => records.All(existing => existing.Id != record.Id)));
        return records.OrderByDescending(record => record.Started).ToArray();
    }

    public IReadOnlyList<EncounterHead> Heads()
    {
        var heads = new List<EncounterHead>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (_draft is not null && _draft.Id.Length > 0)
        {
            heads.Add(new EncounterHead(_draft.Id, _draft.Name, _draft.Started, true));
            seen.Add(_draft.Id);
        }

        if (!string.IsNullOrEmpty(_game))
        {
            var folder = Path.Combine(_game, "bpsr", "records", "dps.meter");
            if (Directory.Exists(folder))
            {
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                {
                    files.Add(file);
                    var id = Path.GetFileNameWithoutExtension(file);
                    if (id.Length == 0 || !seen.Add(id)) continue;
                    var head = CachedHead(file);
                    if (head is not null) heads.Add(head);
                }

                foreach (var stale in _heads.Keys.Where(path => !files.Contains(path)).ToArray())
                    _heads.Remove(stale);
            }
        }

        foreach (var record in _finished)
        {
            if (!seen.Add(record.Id)) continue;
            heads.Add(new EncounterHead(record.Id, record.Name, record.Started, false));
        }

        return heads.OrderByDescending(head => head.Started).ToArray();
    }

    public EncounterView? Load(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['\\', '/', ':']) >= 0) return null;
        if (_draft is not null && _draft.Id == id) return ToView(_draft);
        var held = _finished.FirstOrDefault(record => record.Id == id);
        if (held is not null) return held;
        if (string.IsNullOrEmpty(_game)) return null;
        var path = Path.Combine(Folder(), id + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<FightDraft>(File.ReadAllText(path), RecordJson);
            if (saved is null || string.IsNullOrEmpty(saved.Id)) return null;
            return ToView(saved) with { Current = false };
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private EncounterHead? CachedHead(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (IOException)
        {
            return null;
        }

        if (_heads.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Write == info.LastWriteTimeUtc)
            return cached.Head;
        var head = ReadHead(info);
        if (head is null) return null;
        _heads[path] = (info.Length, info.LastWriteTimeUtc, head);
        return head;
    }

    private static EncounterHead? ReadHead(FileInfo info)
    {
        try
        {
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[1024];
            var filled = 0;
            var state = new JsonReaderState(new JsonReaderOptions());
            var id = "";
            var name = "";
            var started = DateTime.MinValue;
            var current = false;
            var depth = 0;
            var property = "";
            var done = false;
            while (!done)
            {
                var read = stream.Read(buffer, filled, buffer.Length - filled);
                var available = filled + read;
                if (available == 0) break;
                var reader = new Utf8JsonReader(buffer.AsSpan(0, available), read == 0, state);
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                            depth++;
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            depth--;
                            break;
                        case JsonTokenType.PropertyName when depth == 1:
                            property = reader.GetString() ?? "";
                            done = property == "players";
                            break;
                        case JsonTokenType.String when depth == 1 && property == "id":
                            id = reader.GetString() ?? "";
                            break;
                        case JsonTokenType.String when depth == 1 && property == "name":
                            name = reader.GetString() ?? "";
                            break;
                        case JsonTokenType.String when depth == 1 && property == "started":
                            if (reader.TryGetDateTime(out var when)) started = when;
                            break;
                        case JsonTokenType.True when depth == 1 && property == "current":
                            current = true;
                            break;
                        case JsonTokenType.False when depth == 1 && property == "current":
                            current = false;
                            break;
                    }

                    if (done) break;
                }

                if (done || read == 0) break;
                state = reader.CurrentState;
                var consumed = (int)reader.BytesConsumed;
                filled = available - consumed;
                if (consumed > 0 && filled > 0)
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, filled);
                else if (consumed == 0)
                    Array.Resize(ref buffer, buffer.Length * 2);
            }

            if (id.Length == 0) id = Path.GetFileNameWithoutExtension(info.Name);
            if (id.Length == 0) return null;
            return new EncounterHead(id, name, started, current);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void NoteSelf()
    {
        var marked = _players.FirstOrDefault(player => player.Self);
        if (marked is not null)
        {
            _selfName = marked.Name;
            return;
        }

        var selfPath = Path.Combine(_game, "bpsr", "state", "self.json");
        if (!File.Exists(selfPath)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(selfPath));
            var name = Text(document.RootElement, "name").Trim();
            if (name.Length > 0) _selfName = name;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    private bool IsSelf(PlayerSnap player) =>
        player.Self || (_selfName.Length > 0 && string.Equals(player.Name, _selfName, StringComparison.Ordinal));

    private void RecordPlayers()
    {
        if (_draft is null && !LiveCombat.Recordable) return;
        var second = Math.Max(0, (int)(DateTime.UtcNow - _started).TotalSeconds);
        var elapsed = Math.Max(1, (DateTime.UtcNow - _started).TotalSeconds);
        var wrote = false;
        foreach (var player in _players)
        {
            var damage = ShownDamage(player);
            var skills = ShownSkills(player);
            if (damage <= 0 && skills.All(skill => skill.Damage <= 0)) continue;
            _draft ??= new FightDraft
            {
                Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
                Name = LiveCombat.Location,
                Started = DateTime.Now,
                Current = true,
            };
            var entry = Entry(player);
            var perSecond = damage / elapsed;
            var sample = entry.Samples.LastOrDefault();
            if (sample is null || sample.Second != second)
                entry.Samples.Add(new FightSample { Second = second, Damage = damage, PerSecond = perSecond });
            else
            {
                sample.Damage = damage;
                sample.PerSecond = perSecond;
            }

            if (entry.FirstHitTicks == 0 && damage > 0) entry.FirstHitTicks = DateTime.UtcNow.Ticks; // the first moment this person was seen dealing damage
            entry.TotalDamage = damage;
            entry.Skills = skills.Select(skill => new FightSkill
            {
                Id = skill.Id,
                Name = skill.Name,
                Damage = skill.Damage,
                Hits = skill.Hits,
                Crits = skill.Crits,
                Luckies = skill.Luckies,
            }).ToList();
            NoteBuffs(entry, player, second);
            wrote = true;
        }

        if (wrote && DateTime.UtcNow - _saved >= TimeSpan.FromSeconds(1)) SaveDraft();
    }

    private FightPlayer Entry(PlayerSnap player)
    {
        var found = player.CharId != 0
            ? _draft!.Players.FirstOrDefault(entry => entry.CharId == player.CharId)
            : null;
        found ??= _draft!.Players.FirstOrDefault(entry =>
            entry.CharId == 0 && string.Equals(entry.Name, player.Name, StringComparison.Ordinal));
        if (found is not null)
        {
            found.Name = player.Name;
            found.Self = player.Self || IsSelf(player);
            if (player.CharId != 0) found.CharId = player.CharId;
            if (player.Profession.Length > 0) found.Profession = player.Profession;
            if (player.ProfessionId > 0) found.ProfessionId = player.ProfessionId;
            if (player.AbilityScore > 0) found.FightPoint = player.AbilityScore;
            if (player.Cuisine) found.Cuisine = true;
            if (player.Syrup) found.Syrup = true;
            if (player.CuisineEffect.Length > 0) found.CuisineEffect = player.CuisineEffect;
            if (player.SeasonStrength > 0) found.SeasonStrength = player.SeasonStrength;
            if (player.Weapon.Length > 0) found.Weapon = player.Weapon;
            if (player.Imagines.Count > 0) found.Imagines = player.Imagines.Take(2).Select(i => new FightImagine { Name = i.Name, Tier = i.Tier, Icon = i.Icon }).ToList();
            if (player.SyrupEffect.Length > 0) found.SyrupEffect = player.SyrupEffect;
            return found;
        }

        found = new FightPlayer
        {
            Name = player.Name,
            Self = player.Self || IsSelf(player),
            CharId = player.CharId,
            Profession = player.Profession,
            ProfessionId = player.ProfessionId,
            FightPoint = player.AbilityScore,
            Cuisine = player.Cuisine,
            Syrup = player.Syrup,
            CuisineEffect = player.CuisineEffect,
            SeasonStrength = player.SeasonStrength,
            Weapon = player.Weapon,
            Imagines = player.Imagines.Take(2).Select(i => new FightImagine { Name = i.Name, Tier = i.Tier, Icon = i.Icon }).ToList(),
            SyrupEffect = player.SyrupEffect,
        };
        _draft!.Players.Add(found);
        return found;
    }

    private void NoteBuffs(FightPlayer entry, PlayerSnap player, int second)
    {
        if (!_knownBuffs.TryGetValue(player.Name, out var known))
        {
            known = new Dictionary<string, BuffSnap>(StringComparer.Ordinal);
            _knownBuffs[player.Name] = known;
        }

        var active = player.Buffs
            .Where(buff => (buff.Id != 0 || buff.Name.Length > 0) && !SkipBuff(buff))
            .Select(buff => (Key: BuffKey(buff), buff))
            .ToArray();
        var keys = active.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in active)
        {
            if (!known.TryGetValue(item.Key, out var previous))
                entry.Buffs.Add(Mark(second, item.buff, true));
            else if (previous.Stacks != item.buff.Stacks)
            {
                var open = entry.Buffs.LastOrDefault(mark => mark.Active && mark.Id == item.buff.Id && mark.Name == item.buff.Name);
                if (open != null) open.Stacks = item.buff.Stacks;
            }
        }
        foreach (var pair in known.Where(pair => !keys.Contains(pair.Key)))
            entry.Buffs.Add(Mark(second, pair.Value, false));

        known.Clear();
        foreach (var item in active) known[item.Key] = item.buff;
    }

    private static bool SkipBuff(BuffSnap buff) =>
        buff.Stacks <= 1 && buff.Duration <= 0 && buff.Cooldown <= 0;

    private static FightBuff Mark(int second, BuffSnap buff, bool active) => new()
    {
        Second = second,
        Id = buff.Id,
        Name = buff.Name,
        Duration = buff.Duration,
        Cooldown = buff.Cooldown,
        Stacks = buff.Stacks,
        Description = buff.Description,
        Active = active,
    };

    private static string BuffKey(BuffSnap buff) => buff.Id.ToString(CultureInfo.InvariantCulture) + "\n" + buff.Name;

    private static string SkillKey(SkillSnap skill) =>
        skill.Id > 0 ? skill.Id.ToString(CultureInfo.InvariantCulture) : skill.Name;

    private List<SkillSnap> ShownSkills(PlayerSnap player)
    {
        _skillBaseline.TryGetValue(player.Name, out var baselines);
        var shown = new List<SkillSnap>();
        foreach (var skill in player.Skills)
        {
            var baseline = baselines != null && baselines.TryGetValue(SkillKey(skill), out var stored)
                ? stored
                : new SkillSnap(skill.Name, 0, 0, 0, skill.Id);
            shown.Add(new SkillSnap(
                skill.Name,
                Math.Max(0, skill.Damage - baseline.Damage),
                Math.Max(0, skill.Hits - baseline.Hits),
                Math.Max(0, skill.Crits - baseline.Crits),
                skill.Id,
                Math.Max(0, skill.Luckies - baseline.Luckies)));
        }

        return shown.Where(skill => skill.Damage > 0 || skill.Hits > 0).ToList();
    }

    private void RememberSkills()
    {
        _skillBaseline.Clear();
        foreach (var player in _players)
        {
            var map = new Dictionary<string, SkillSnap>(StringComparer.Ordinal);
            foreach (var skill in player.Skills)
                map[SkillKey(skill)] = skill;
            _skillBaseline[player.Name] = map;
        }
    }

    public bool DeleteRecord(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['\\', '/', ':']) >= 0) return false;
        var path = Path.Combine(Folder(), id + ".json");
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        _finished.RemoveAll(record => record.Id == id);
        if (_draft?.Id == id)
        {
            _draft = null;
            _saved = DateTime.MinValue;
        }

        return true;
    }

    private void FinishDraft()
    {
        if (_draft is null) return;
        _draft.Current = false;
        _draft.Ended = DateTime.UtcNow;
        if (_draft.Players.Any(player => player.TotalDamage > 0 || player.Skills.Count > 0) && !SaveDraft())
            _finished.Add(ToView(_draft));

        _draft = null;
        _saved = DateTime.MinValue;
    }

    private bool SaveDraft()
    {
        if (_draft is null || string.IsNullOrEmpty(_game)) return false;
        try
        {
            var path = Path.Combine(Folder(), _draft.Id + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(_draft, RecordJson));
            _saved = DateTime.UtcNow;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static EncounterView ToView(FightDraft draft)
    {
        var players = draft.Players.Count > 0
            ? draft.Players
            : LegacyPlayers(draft);
        var views = players
            .OrderByDescending(player => player.TotalDamage)
            .ThenBy(player => player.Name, StringComparer.Ordinal)
            .Select(player => new EncounterPlayerView(
                player.Name,
                player.Self,
                player.TotalDamage,
                player.Samples.Select(sample => new DamageSampleView(sample.Second, sample.Damage, sample.PerSecond)).ToArray(),
                player.Skills.Select(skill => new SkillDamageView(skill.Name, skill.Damage, skill.Hits, skill.Crits, skill.Id, skill.Luckies)).ToArray(),
                player.Buffs.Select(buff => new BuffMarkView(buff.Second, buff.Name, buff.Active, buff.Id, buff.Duration, buff.Cooldown, buff.Stacks, buff.Description)).ToArray(),
                player.CharId,
                player.Profession,
                player.FightPoint,
                player.Cuisine,
                player.Syrup,
                player.ProfessionId,
                player.CuisineEffect,
                player.SyrupEffect,
                player.Weapon,
                player.SeasonStrength,
                player.Imagines.Select(i => new MeterImagineView(i.Name, i.Tier, i.Icon)).ToArray(),
                player.FirstHitTicks))
            .ToArray();
        return new EncounterView(
            draft.Id,
            draft.Name,
            draft.Started,
            draft.Ended,
            draft.Current,
            views.Sum(player => player.TotalDamage),
            views);
    }

    private static List<FightPlayer> LegacyPlayers(FightDraft draft)
    {
        if (draft.TotalDamage <= 0 && draft.Skills.Count == 0 && draft.Samples.Count == 0 && draft.Buffs.Count == 0)
            return [];
        return
        [
            new FightPlayer
            {
                Name = draft.Name,
                Self = true,
                TotalDamage = draft.TotalDamage,
                Samples = draft.Samples,
                Skills = draft.Skills,
                Buffs = draft.Buffs,
            },
        ];
    }

    private sealed class FightDraft
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public DateTime Started { get; set; }

        public DateTime? Ended { get; set; }

        public bool Current { get; set; }

        public long TotalDamage { get; set; }

        public List<FightSample> Samples { get; set; } = [];

        public List<FightSkill> Skills { get; set; } = [];

        public List<FightBuff> Buffs { get; set; } = [];

        public List<FightPlayer> Players { get; set; } = [];
    }

    private sealed class FightPlayer
    {
        public string Name { get; set; } = "";

        public long CharId { get; set; }

        public string Profession { get; set; } = "";

        public int ProfessionId { get; set; }

        public long FightPoint { get; set; }

        public bool Cuisine { get; set; }

        public bool Syrup { get; set; }

        public string CuisineEffect { get; set; } = "";

        public int SeasonStrength { get; set; }

        public string Weapon { get; set; } = "";

        public long FirstHitTicks { get; set; }

        public List<FightImagine> Imagines { get; set; } = [];

        public string SyrupEffect { get; set; } = "";

        public bool Self { get; set; }

        public long TotalDamage { get; set; }

        public List<FightSample> Samples { get; set; } = [];

        public List<FightSkill> Skills { get; set; } = [];

        public List<FightBuff> Buffs { get; set; } = [];
    }

    private sealed class FightImagine
    {
        public string Name { get; set; } = "";

        public int Tier { get; set; }

        public string Icon { get; set; } = "";
    }

    private sealed class FightSample
    {
        public int Second { get; set; }

        public long Damage { get; set; }

        public double PerSecond { get; set; }
    }

    private sealed class FightSkill
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public long Damage { get; set; }

        public int Hits { get; set; }

        public int Crits { get; set; }

        public int Luckies { get; set; }
    }

    private sealed class FightBuff
    {
        public int Second { get; set; }

        public int Id { get; set; }

        public string Name { get; set; } = "";

        public double Duration { get; set; }

        public double Cooldown { get; set; }

        public int Stacks { get; set; }

        public string Description { get; set; } = "";

        public bool Active { get; set; }
    }
}
