using System.Globalization;
using System.Text.Json;
using StarOverlay.Framework;

namespace StarOverlay.DpsMeter;

public sealed class DpsMeterPlugin : IPlugin
{
    private readonly MeterSession _session = new();

    public PluginInfo Info { get; } = new("dps.meter", "미터기", "0.4.9");

    public void Load(IPluginContext context)
    {
        _session.Open(context.GameDirectory);
        context.Controls.UseFrameButton(0, "\u2699\uFE0F", opensSettings: true);
        context.Controls.UseFrameButton(1, "\U0001F504", press: _session.Reset);
        context.Controls.AddToggle("show-name", "이름 표시", true);
        context.Controls.AddToggle("show-job", "직업명 표시", true);
        context.Controls.AddToggle("show-class", "직업 아이콘", true);
        context.Controls.AddToggle("show-imagines", "이매진 표시", true);
        context.Controls.AddToggle("show-power", "전투력 표시", true);
        context.Controls.AddToggle("show-season", "시즌 강도 표시", true);
        LiveCombat.SetRecordExtra(context.Settings.Get("record-extra") != "false"); // on until the player switches it off
        context.Controls.AddToggle("record-extra", "극한공간 하드, 마스터 1, 마스터 6, 길드 사냥 기록 추가", true, LiveCombat.SetRecordExtra);
        LiveCombat.SetVaultFifthOnly(context.Settings.Get("record-vault-fifth") == "true"); // off until the player asks for it
        context.Controls.AddToggle("record-vault-fifth", "시디문 5층 단위로만 기록", false, LiveCombat.SetVaultFifthOnly);
        context.Controls.AddText("row-height", "행 높이", "24", 18, 72);
        context.Controls.AddText("font-size", "글자 크기", "15", 8, 40);
        context.Ui.AddMeter("dps.meter.rows", () => _session.Rows(context), () => _session.RowHeight(context), () => _session.TextSize(context));
        context.Ui.AddEncounterLog("dps.meter.fights", _session.Records, _session.Folder, _session.DeleteRecord, _session.Heads, _session.Load);
    }

    public void Unload()
    {
    }
}

public sealed partial class MeterSession
{
    public const int SelfText = MeterColors.SelfText;
    public const int FuryText = MeterColors.FuryText;
    public const int FarSeaText = MeterColors.FarSeaText;

    private readonly Dictionary<string, long> _baseline = new(StringComparer.Ordinal);
    private readonly List<PlayerSnap> _players = [];

    private string _game = "";
    private DateTime _stamp = DateTime.MinValue;
    private DateTime _started = DateTime.UtcNow;
    private string _source = "";
    private int _liveGeneration;
    private int _sceneChanges;
    private int _encounterEnds;
    private int _combatEpoch;
    private bool _holdClock = true;
    private bool _placeRecordable; // whether the place the damage was dealt in is recorded, and its name: what was true at the last look,
    private string _placeName = ""; // because the game has already published the next place when the meter notices the move
    private bool _keepMembers;

    public void Open(string gameDirectory) => _game = gameDirectory;

    public void Reset()
    {
        LiveCombat.CancelScheduledEnd();
        ZeroFight(EncounterEndKind.Manual);
    }

    private void ZeroFight(EncounterEndKind kind)
    {
        _keepMembers = true;
        Poll();
        RecordPlayers();
        FinishDraft(kind);
        _baseline.Clear();
        foreach (var player in _players)
            _baseline[player.Name] = player.Damage;
        RememberSkills();
        _knownBuffs.Clear();
        LiveCombat.ArmCombat();
        HoldClock();
    }

    private void ResetMap()
    {
        RecordPlayers();
        FinishDraft(EncounterEndKind.MapMove);
        _keepMembers = false;
        _players.Clear();
        _baseline.Clear();
        _skillBaseline.Clear();
        _knownBuffs.Clear();
        LiveCombat.CancelScheduledEnd();
        HoldClock();
    }

    private void HoldClock()
    {
        _holdClock = true;
        _started = DateTime.UtcNow;
    }

    private void ApplyCombatClock()
    {
        var combat = LiveCombat.CombatEpoch;
        if (combat != _combatEpoch)
        {
            _combatEpoch = combat;
            var running = !_holdClock; // the clock goes on after a break out of combat; only a reset (button, end of an encounter, new map) holds it
            _holdClock = false;
            if (!running)
                _started = DateTime.UtcNow;
            if (!LiveCombat.EndPending) _keepMembers = false;
            return;
        }

        if (_holdClock)
            _started = DateTime.UtcNow;
    }

    public int TextSize(IPluginContext context)
    {
        var text = context.Settings.Get("font-size");
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = 15;
        return Math.Clamp(value, 8, 40);
    }

    public int RowHeight(IPluginContext context)
    {
        var text = context.Settings.Get("row-height");
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = 24;
        return Math.Clamp(value, 18, 72);
    }

    public IReadOnlyList<MeterRowView> Rows(IPluginContext context)
    {
        WatchMap();
        var zeroed = WatchEncounter();
        ApplyCombatClock();
        if (zeroed)
        {
            _keepMembers = true;
            HoldClock();
        }
        else if (LiveCombat.EndPending)
            _keepMembers = true;

        _placeRecordable = LiveCombat.Recordable; // from here on the damage belongs to this place
        _placeName = LiveCombat.Location;
        LiveCombat.SetStarted(_started);
        Poll();
        RecordPlayers();
        var showName = On(context, "show-name");
        var showJob = On(context, "show-job");
        var showClass = On(context, "show-class");
        var showImagines = On(context, "show-imagines");
        var showPower = On(context, "show-power");
        var showSeason = On(context, "show-season");
        var elapsed = Math.Max(1, (DateTime.UtcNow - _started).TotalSeconds);

        var dealt = _players
            .Select(player => (Player: player, Damage: ShownDamage(player)))
            .OrderByDescending(row => row.Damage)
            .ThenBy(row => row.Player.Name, StringComparer.Ordinal)
            .ToArray();
        var sum = dealt.Sum(row => row.Damage);
        var top = dealt.Length == 0 ? 0 : dealt[0].Damage;
        var width = dealt.Length < 101 ? 2 : 3;
        var rows = new List<MeterRowView>(dealt.Length);
        for (var i = 0; i < dealt.Length; i++)
        {
            var (player, damage) = dealt[i];
            var share = sum == 0 ? 0 : damage * 100.0 / sum;
            var bar = top == 0 ? 0 : damage * 100.0 / top;
            var perSecond = damage / elapsed;
            var imagines = showImagines
                ? player.Imagines.Take(2).Concat(Enumerable.Repeat(new ImagineSnap("", 0, ""), 2)).Take(2)
                    .Select(imagine => new MeterImagineView(imagine.Name, imagine.Tier, imagine.Icon)).ToArray()
                : [];
            var self = IsSelf(player);
            rows.Add(new MeterRowView(
                $" {(i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}.",
                Caption(player, showName, showJob, showPower, showSeason),
                showClass ? BaseProfession(player.ProfessionId) : -1,
                $"{Short(damage)} ({Short(perSecond)}) {share.ToString("F0", CultureInfo.InvariantCulture).PadLeft(3)}%",
                bar,
                player.Dead,
                BarColor(player.ProfessionId),
                imagines,
                self ? SelfText : IsFury(player.Weapon) ? FuryText : IsFarSea(player.Weapon) ? FarSeaText : 0,
                showClass ? IconColor(player) : 0,
                player.Cuisine,
                player.Syrup,
                player.CuisineEffect,
                player.SyrupEffect));
        }

        return rows;
    }

    private void WatchMap()
    {
        var changes = LiveCombat.SceneChanges;
        if (changes == _sceneChanges) return;
        _sceneChanges = changes;
        if (changes > 0) ResetMap();
    }

    private bool WatchEncounter()
    {
        var ends = LiveCombat.EncounterEnds;
        if (ends != _encounterEnds)
        {
            _encounterEnds = ends;
            if (ends == 0) return false;
            LiveCombat.CancelScheduledEnd();
            ZeroFight(LiveCombat.EndKind);
            return true;
        }

        if (!LiveCombat.TakeScheduledEnd()) return false;
        ZeroFight(LiveCombat.EndKind);
        return true;
    }

    private long ShownDamage(PlayerSnap player)
    {
        var baseline = _baseline.TryGetValue(player.Name, out var stored) ? stored : 0;
        return Math.Max(0, player.Damage - baseline);
    }

    private void Poll()
    {
        var generation = LiveCombat.Generation;
        if (generation > 0)
        {
            if (generation == _liveGeneration) return;
            var rows = LiveCombat.Rows.Select(FromLive).Where(player => !string.IsNullOrWhiteSpace(player.Name)).ToList();
            _liveGeneration = generation;
            if (_keepMembers && _players.Count > 0)
                MergePlayers(rows);
            else
            {
                _players.Clear();
                _players.AddRange(rows);
            }

            NoteSelf();
            return;
        }

        if (string.IsNullOrEmpty(_game)) return;
        var meter = Path.Combine(_game, "bpsr", "state", "meter.json");
        var self = Path.Combine(_game, "bpsr", "state", "self.json");
        var path = File.Exists(meter) ? meter : self;
        if (!File.Exists(path)) return;
        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return;
        }

        if (stamp == _stamp && path == _source) return;
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return;
        }

        List<PlayerSnap> players;
        try
        {
            players = ReadPlayers(json);
        }
        catch (JsonException)
        {
            return;
        }

        _stamp = stamp;
        _source = path;
        _players.Clear();
        _players.AddRange(players.Where(player => !string.IsNullOrWhiteSpace(player.Name)));
        NoteSelf();
    }

    private void MergePlayers(List<PlayerSnap> incoming)
    {
        foreach (var player in incoming)
        {
            var index = _players.FindIndex(existing => string.Equals(existing.Name, player.Name, StringComparison.Ordinal));
            if (index >= 0) _players[index] = player;
            else _players.Add(player);
        }
    }

    private static bool On(IPluginContext context, string key) => (context.Settings.Get(key) ?? "true") == "true";

    private static string Caption(PlayerSnap player, bool showName, bool showJob, bool showPower, bool showSeason)
    {
        var text = showName ? player.Name : "";
        if (showJob && player.Profession.Length > 0)
            text = text.Length == 0 ? player.Profession : $"{text}-{player.Profession}";
        if (showPower && showSeason)
            text += $" ({player.AbilityScore}+{player.SeasonStrength})";
        else if (showPower)
            text += $" ({player.AbilityScore})";
        else if (showSeason)
            text += $" ({player.SeasonStrength})";
        return text;
    }

    private static int BaseProfession(int professionId) =>
        professionId >= 10000 ? professionId / 10000 : professionId;

    private const int TankIcon = 0x3B82F6;
    private const int HealerIcon = 0x3DDC78;
    private const int DamageIcon = 0xE23B3B;

    private static int IconColor(PlayerSnap player)
    {
        var profession = BaseProfession(player.ProfessionId);
        if (profession is 9 or 12) return TankIcon;
        if (player.Profession is "협주" or "치유") return HealerIcon;
        return DamageIcon;
    }

    private static int BarColor(int professionId) => ProfessionColors.Bar(professionId);

    private static string Short(double value)
    {
        string[] suffix = ["", "K", "M", "B", "t"];
        var absolute = Math.Abs(value);
        if (absolute < 1000) return Math.Round(absolute).ToString("0", CultureInfo.InvariantCulture);
        var place = (int)Math.Floor(Math.Log(absolute, 1000));
        if (place >= suffix.Length) place = suffix.Length - 1;
        var shortNumber = Math.Round(absolute / Math.Pow(1000, place), 2);
        return shortNumber.ToString("0.00", CultureInfo.InvariantCulture) + suffix[place];
    }

    private static PlayerSnap FromLive(LiveFighter fighter) => new(
        fighter.Name,
        fighter.ProfessionId,
        fighter.Profession,
        fighter.AbilityScore,
        fighter.SeasonStrength,
        fighter.Damage,
        fighter.Dead,
        (fighter.Imagines ?? []).Select(imagine => new ImagineSnap(imagine.Name, imagine.Tier, imagine.Icon)).ToArray(),
        fighter.Self,
        fighter.Weapon,
        fighter.Skills.Select(skill => new SkillSnap(skill.Name, skill.Damage, skill.Hits, skill.Crits, skill.Id, skill.Luckies)).ToArray(),
        fighter.Buffs.Select(buff => new BuffSnap(buff.Id, buff.Name, buff.Duration, buff.Cooldown, buff.Stacks, buff.Description)).ToArray(),
        fighter.CharId,
        fighter.Cuisine,
        fighter.Syrup,
        fighter.CuisineEffect,
        fighter.SyrupEffect);

    private static List<PlayerSnap> ReadPlayers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(element => ReadPlayer(element)).ToList();
        if (Try(root, "players", out var players) && players.ValueKind == JsonValueKind.Array)
            return players.EnumerateArray().Select(element => ReadPlayer(element)).ToList();
        if (Try(root, "name", out _))
            return [ReadPlayer(root, forceSelf: true)];
        return [];
    }

    private static PlayerSnap ReadPlayer(JsonElement element, bool forceSelf = false)
    {
        var damage = 0L;
        if (Try(element, "totalDamage", out var total) && total.TryGetInt64(out var totalValue))
            damage = totalValue;
        else if (Try(element, "damage", out var single) && single.TryGetInt64(out var singleValue))
            damage = singleValue;

        var ability = 0L;
        if (Try(element, "abilityScore", out var score) && score.TryGetInt64(out var scoreValue))
            ability = scoreValue;
        else if (Try(element, "fightPoint", out var fight) && fight.TryGetInt64(out var fightValue))
            ability = fightValue;

        var imagines = new List<ImagineSnap>();
        if (Try(element, "imagines", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    imagines.Add(new ImagineSnap(item.GetString() ?? "", 0, ""));
                else if (item.ValueKind == JsonValueKind.Object)
                    imagines.Add(new ImagineSnap(Text(item, "name"), Number(item, "tier"), Text(item, "icon")));
            }
        }

        var dead = Try(element, "dead", out var flag) && flag.ValueKind == JsonValueKind.True;
        var self = forceSelf || (Try(element, "self", out var mine) && mine.ValueKind == JsonValueKind.True);
        long charId = 0;
        if (Try(element, "charId", out var id) && id.TryGetInt64(out var idValue)) charId = idValue;
        var cuisine = Try(element, "cuisine", out var food) && food.ValueKind == JsonValueKind.True;
        var syrup = Try(element, "syrup", out var potion) && potion.ValueKind == JsonValueKind.True;
        return new PlayerSnap(
            Text(element, "name").Trim(),
            Number(element, "professionId"),
            Text(element, "profession").Trim(),
            ability,
            Number(element, "seasonStrength"),
            damage,
            dead,
            imagines,
            self,
            Text(element, "weapon").Trim(),
            ReadSkills(element),
            ReadBuffs(element),
            charId,
            cuisine,
            syrup);
    }

    private static bool Try(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return element.TryGetProperty(pascal, out value);
    }

    private static string Text(JsonElement element, string name) =>
        Try(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int Number(JsonElement element, string name) =>
        Try(element, name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static double Decimal(JsonElement element, string name) =>
        Try(element, name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static List<SkillSnap> ReadSkills(JsonElement element)
    {
        var skills = new List<SkillSnap>();
        if (!Try(element, "skills", out var list) || list.ValueKind != JsonValueKind.Array) return skills;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = Text(item, "name").Trim();
            var id = Number(item, "id");
            if (name.Length == 0 && id == 0) continue;
            long damage = 0;
            if (Try(item, "damage", out var value) && value.TryGetInt64(out var number)) damage = number;
            skills.Add(new SkillSnap(name, damage, Number(item, "hits"), Number(item, "crits"), id, Number(item, "luckies")));
        }

        return skills;
    }

    private static List<BuffSnap> ReadBuffs(JsonElement element)
    {
        var buffs = new List<BuffSnap>();
        if (!Try(element, "buffs", out var list) || list.ValueKind != JsonValueKind.Array) return buffs;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                buffs.Add(new BuffSnap(0, name.Trim()));
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var named = Text(item, "name").Trim();
                var id = Number(item, "id");
                if (named.Length > 0 || id != 0)
                    buffs.Add(new BuffSnap(id, named, Decimal(item, "duration"), Decimal(item, "cooldown"), Number(item, "stacks"), Text(item, "description")));
            }
        }

        return buffs;
    }

    private static bool IsFury(string weapon) => MeterColors.IsFury(weapon);

    private static bool IsFarSea(string weapon) => MeterColors.IsFarSea(weapon);

    private sealed record ImagineSnap(string Name, int Tier, string Icon);

    private sealed record SkillSnap(string Name, long Damage, int Hits, int Crits, int Id = 0, int Luckies = 0);

    private sealed record BuffSnap(int Id, string Name, double Duration = 0, double Cooldown = 0, int Stacks = 0, string Description = "");

    private sealed record PlayerSnap(
        string Name,
        int ProfessionId,
        string Profession,
        long AbilityScore,
        int SeasonStrength,
        long Damage,
        bool Dead,
        IReadOnlyList<ImagineSnap> Imagines,
        bool Self,
        string Weapon,
        IReadOnlyList<SkillSnap> Skills,
        IReadOnlyList<BuffSnap> Buffs,
        long CharId = 0,
        bool Cuisine = false,
        bool Syrup = false,
        string CuisineEffect = "",
        string SyrupEffect = "");
}
