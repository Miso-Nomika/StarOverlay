using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BPSR_ZDPSLib;
using StarOverlay.Core;
using StarOverlay.Framework;
using StarOverlay.Game.Packets;
using Google.Protobuf;
using Zproto;
using static Zproto.WorldNtfCsharp.Types;
using WorldNtf = BPSR_ZDPSLib.ServiceMethods.WorldNtf;
using TeamNtf = BPSR_ZDPSLib.ServiceMethods.GrpcTeamNtf;
using ChatNtf = BPSR_ZDPSLib.ServiceMethods.ChitChatNtf;
using WorldCall = BPSR_ZDPSLib.ServiceMethods.WorldProxy;

namespace StarOverlay.Game;

public sealed partial class GameCapture
{
    // 표 JSON은 게임에서 직접 뽑은 것(데이터 폴더의 tables 폴더, 에이전트가 저장)이다. 게임을 한 번 켜서 저장되기 전에는 파일이 없어
    // 표를 읽지 못하고(이름 대신 ID 번호가 보이고 던전·보스 판정은 비어 있다), 저장되면 바로 다시 읽는다.
    static string _dumpRoot = "";

    static string TablePath(string file) => Path.Combine(_dumpRoot, file);

    const string BasicAttackName = "일반 공격";

    static string SkillTable => TablePath("SkillTable.json");
    static string BuffTable => TablePath("BuffTable.json");
    static string SkillLevelTable => TablePath("SkillFightLevelTable.json");
    static string RecountTable => TablePath("RecountTable.json");
    static string DamageAttrTable => TablePath("DamageAttrTable.json");
    static string SceneTable => TablePath("SceneTable.json");
    static string DungeonTable => TablePath("DungeonsTable.json");
    static string TargetTable => TablePath("TargetTable.json");
    static string MonsterTable => TablePath("MonsterTable.json");
    static string ItemTable => TablePath("ItemTable.json");
    static string EquipTable => TablePath("EquipTable.json");
    static string EquipAttrLibTable => TablePath("EquipAttrLibTable.json");

    /// <summary>The service got newly dumped game tables: read every table again.</summary>
    public static void ReloadTables() => LoadNames();

    /// <summary>
    /// The agent saved new tables while the game runs: read them and judge the place again (the dungeon and raid lists were empty
    /// when the place was entered before the tables existed).
    /// </summary>
    public void OnTablesWritten()
    {
        LoadNames();
        RecomputeEquip();
        bool inPlace;
        lock (_gate) inPlace = _sceneId > 0;
        if (inPlace) PublishLocation();
    }

    internal static string? LoadedSkillName(int id) => _skillNames.TryGetValue(id, out var name) ? name : null;

    internal static string? LoadedBuffName(int id) => _buffMeta.TryGetValue(id, out var meta) ? meta.Name : null;

    static Dictionary<int, string> _skillNames = [];
    static Dictionary<int, BuffMeta> _buffMeta = [];
    static HashSet<int> _cuisine = [];
    static HashSet<int> _syrup = [];
    static Dictionary<int, double> _cooldowns = [];
    static Dictionary<int, string> _scenes = [];
    static Dictionary<int, string> _scenesKo = [];
    static Dictionary<int, ImagineInfo> _imagines = [];
    static HashSet<int> _masterScenes = [];
    static HashSet<int> _raidScenes = [];
    static HashSet<int> _vaultScenes = [];
    static HashSet<int> _bosses = [];

    static void LoadNames()
    {
        // every skill name comes from the game itself: the names of its damage recount first (what the game's own damage list calls a damage id),
        // then the names of its skill table; an id with neither is unknown ("미확인 <id>")
        var skills = ReadSkills(SkillTable);
        var buffs = ReadBuffMeta(BuffTable);
        ApplyKoreanBuffs(buffs.Meta);
        var cooldowns = ReadCooldowns(SkillLevelTable);
        var scenes = ReadNames(SceneTable);
        var dungeons = ReadDungeons(DungeonTable);
        var gameNames = new Dictionary<int, string>(skills.Names);
        var recount = ReadRecountNames(RecountTable, DamageAttrTable);
        foreach (var pair in recount) skills.Names[pair.Key] = pair.Value;
        foreach (var pair in skills.Imagines.Keys.ToArray())
        {
            if (skills.Imagines[pair].Name.Length == 0 && recount.TryGetValue(pair, out var name))
                skills.Imagines[pair] = skills.Imagines[pair] with { Name = name };
        }

        // the steps of a basic attack combo are all called "일반 공격" in the game table: "<name of the first skill>-<step>"
        foreach (var pair in gameNames)
        {
            if (pair.Value != BasicAttackName) continue;
            var step = pair.Key % 100;
            if (step >= 2 && gameNames.TryGetValue(pair.Key - step + 1, out var first) && first.Length > 0 && first != BasicAttackName)
                skills.Names[pair.Key] = $"{first}-{step}";
        }

        if (skills.Names.Count > 0)
        {
            _skillNames = skills.Names;
            SkillBook.Set(skills.Names, ReadSkillIcons(SkillTable), ReadSkillFamilies(SkillTable));
        }
        if (skills.Imagines.Count > 0) _imagines = skills.Imagines;
        if (buffs.Meta.Count > 0)
        {
            _buffMeta = buffs.Meta;
            BuffBook.Set(buffs.Meta.ToDictionary(pair => pair.Key, pair => new BuffEntry(pair.Key, pair.Value.Name, pair.Value.Duration, pair.Value.Description)));
        }
        if (buffs.Cuisine.Count > 0) _cuisine = buffs.Cuisine;
        if (buffs.Syrup.Count > 0) _syrup = buffs.Syrup;
        if (cooldowns.Count > 0) _cooldowns = cooldowns;
        if (scenes.Count > 0) _scenes = scenes;
        var scenesKo = ReadKoreanScenes(Path.Combine(AppContext.BaseDirectory, "SceneNames.ko.json"));
        foreach (var id in scenesKo.Keys.Where(scenes.ContainsKey).ToArray())
            scenesKo.Remove(id);
        _scenesKo = scenesKo;
        var survey = ReadSurveyTargets(TargetTable);
        if (survey.Count > 0) _surveyTargets = survey;
        var killAll = ReadKillAllTargets(TargetTable);
        if (killAll.Count > 0) _killAllTargets = killAll;
        if (dungeons.Master.Count > 0) _masterScenes = dungeons.Master;
        var extra = ReadExtraDungeons(DungeonTable);
        if (extra.ExtremeKinds.Count > 0)
        {
            _extremeKinds = extra.ExtremeKinds;
            _extremeHardScenes = extra.ExtremeKinds.Where(pair => pair.Value == HardName).Select(pair => pair.Key).ToHashSet();
        }

        if (extra.GuildHunt.Count > 0) _guildHuntScenes = extra.GuildHunt;
        if (dungeons.Raid.Count > 0) _raidScenes = dungeons.Raid;
        if (dungeons.Vault.Count > 0) _vaultScenes = dungeons.Vault;
        var items = ReadItems(ItemTable);
        if (items.Count > 0) ItemBook.Set(items);
        var bosses = ReadBosses(MonsterTable);
        if (bosses.Count > 0) _bosses = bosses;
        AccessoryGear.UseTables(EquipTable, EquipAttrLibTable);
    }

    static Dictionary<int, string> _extremeKinds = [];
    static HashSet<int> _extremeHardScenes = [];
    static HashSet<int> _guildHuntScenes = [];
    static HashSet<int> _surveyTargets = [];
    static HashSet<int> _killAllTargets = [];

    const int SurveyTargetType = 27;

    /// <summary>
    /// The dungeon goals that read "... 조사를 완료하고 보스전 시작하기" (target type 27): the one that ends a trash section. Other goals of a dungeon
    /// (kill this monster, collect that, reach the end) are not section clears.
    /// </summary>
    internal static HashSet<int> ReadSurveyTargets(string path) =>
        ReadTargets(path, (kind, words) => kind == SurveyTargetType && words.Contains("조사", StringComparison.Ordinal));

    /// <summary>The goal "모든 적 처치하기" of a Stimen Vault floor: when it is done, the floor was cleared (when the vault ends without it, time ran out).</summary>
    internal static HashSet<int> ReadKillAllTargets(string path) =>
        ReadTargets(path, (_, words) => words.Contains("모든 적 처치", StringComparison.Ordinal));

    static HashSet<int> ReadTargets(string path, Func<int, string, bool> wanted)
    {
        var ids = new HashSet<int>();
        if (!File.Exists(path)) return ids;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var row = entry.Value;
                if (!row.TryGetProperty("TargetType", out var type) || !type.TryGetInt32(out var kind)) continue;
                if (!row.TryGetProperty("TargetDes", out var text) || text.ValueKind != JsonValueKind.String || text.GetString() is not { } words || !wanted(kind, words)) continue;
                if (row.TryGetProperty("Id", out var id) && id.TryGetInt32(out var number) && number > 0) ids.Add(number);
            }
        }
        catch (Exception)
        {
        }

        return ids;
    }

    const int ExtremeSpacePlayType = 9;
    const int GuildHuntPlayType = 13;
    const string HardName = "하드";

    /// <summary>
    /// The extreme space places with their level as the game table names it (play type 9, dungeon type "하드" or "노멀": the same scene name
    /// serves both) and the guild hunts (play type 13). The hard extreme space and the guild hunts are only recorded when the player asks for it.
    /// </summary>
    internal static (Dictionary<int, string> ExtremeKinds, HashSet<int> GuildHunt) ReadExtraDungeons(string path)
    {
        var kinds = new Dictionary<int, string>();
        var hunt = new HashSet<int>();
        if (!File.Exists(path)) return (kinds, hunt);
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var row = entry.Value;
                if (!row.TryGetProperty("PlayType", out var playElement) || !playElement.TryGetInt32(out var play)) continue;
                var ids = new List<int>(2);
                if (row.TryGetProperty("SceneID", out var scene) && scene.TryGetInt32(out var sceneId) && sceneId > 0) ids.Add(sceneId);
                if (row.TryGetProperty("Id", out var id) && id.TryGetInt32(out var dungeonId) && dungeonId > 0) ids.Add(dungeonId);
                if (play == GuildHuntPlayType)
                {
                    foreach (var number in ids) hunt.Add(number);
                }
                else if (play == ExtremeSpacePlayType && row.TryGetProperty("DungeonTypeName", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() is { Length: > 0 } level)
                {
                    foreach (var number in ids) kinds[number] = level;
                }
            }
        }
        catch (Exception)
        {
        }

        return (kinds, hunt);
    }

    static (HashSet<int> Master, HashSet<int> Raid, HashSet<int> Vault) ReadDungeons(string path)
    {
        var master = new HashSet<int>();
        var raid = new HashSet<int>();
        var vault = new HashSet<int>();
        if (!File.Exists(path)) return (master, raid, vault);
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("PlayType", out var playElement) || !playElement.TryGetInt32(out var play))
                    continue;
                if (play is not (16 or 17 or 18)) continue;
                var target = play switch
                {
                    16 => vault,
                    17 => master,
                    _ => raid,
                };
                if (entry.Value.TryGetProperty("SceneID", out var scene) && scene.TryGetInt32(out var sceneId) && sceneId > 0)
                    target.Add(sceneId);
                if (entry.Value.TryGetProperty("Id", out var id) && id.TryGetInt32(out var dungeonId) && dungeonId > 0)
                    target.Add(dungeonId);
            }
        }
        catch (Exception)
        {
        }

        return (master, raid, vault);
    }

    /// <summary>
    /// Korean place names by scene id (<c>{"7":{"name":"...","src":"mt|human","en":"..."}}</c>). Only the display uses them;
    /// the English name from the game table still drives every decision (guild, vault, raid). No file: English stays.
    /// </summary>
    internal static Dictionary<int, string> ReadKoreanScenes(string path)
    {
        var map = new Dictionary<int, string>();
        try
        {
            if (!File.Exists(path)) return map;
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id) || entry.Value.ValueKind != JsonValueKind.Object) continue;
                var name = entry.Value.TryGetProperty("name", out var element) ? element.GetString() ?? "" : "";
                if (name.Length > 0) map[id] = name;
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    /// <summary>The Korean name of a scene when the table has one, else the original name.</summary>
    internal static string LocalizeScene(IReadOnlyDictionary<int, string> korean, int sceneId, string english) =>
        korean.TryGetValue(sceneId, out var name) ? name : english;

    const string RecountOther = "기타"; // the game's catch-all bucket of its damage list: no name of a skill

    /// <summary>
    /// The names the game's own damage list (RecountTable) gives: its rows name a list of damage ids (DamageAttrTable ids), and each of those
    /// belongs to the skill id we receive in the damage packets (its TypeEnum). When several rows name one skill id the lowest damage id wins.
    /// </summary>
    internal static Dictionary<int, string> ReadRecountNames(string recountPath, string damageAttrPath)
    {
        var names = new Dictionary<int, string>();
        try
        {
            if (!File.Exists(recountPath) || !File.Exists(damageAttrPath)) return names;
            var byDamage = new Dictionary<long, string>();
            using (var stream = File.OpenRead(recountPath))
            using (var document = JsonDocument.Parse(stream))
            {
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    var row = entry.Value;
                    var name = row.TryGetProperty("RecountName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() ?? "" : "";
                    if (name.Length == 0 || name == RecountOther) continue;
                    if (!row.TryGetProperty("DamageId", out var ids) || ids.ValueKind != JsonValueKind.Array) continue;
                    foreach (var id in ids.EnumerateArray())
                        if (id.TryGetInt64(out var damageId)) byDamage.TryAdd(damageId, name);
                }
            }

            var damages = new List<(long Id, int Skill)>();
            using (var stream = File.OpenRead(damageAttrPath))
            using (var document = JsonDocument.Parse(stream))
            {
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    var row = entry.Value;
                    if (row.TryGetProperty("Id", out var idElement) && idElement.TryGetInt64(out var id)
                        && row.TryGetProperty("TypeEnum", out var typeElement) && typeElement.TryGetInt32(out var skill) && skill > 0)
                        damages.Add((id, skill));
                }
            }

            damages.Sort((left, right) => left.Id.CompareTo(right.Id));
            foreach (var (id, skill) in damages)
                if (byDamage.TryGetValue(id, out var name)) names.TryAdd(skill, name);
        }
        catch (Exception)
        {
        }

        return names;
    }

    /// <summary>
    /// A buff the game gives no name (most of them are internal buffs the game never shows) is named from the developers' own name in the table
    /// (Chinese) together with our machine translation of it: "질풍 강습 · 青岚强袭". With the Chinese next to it the name reads as "still to be checked";
    /// a name that is only Korean is the game's. Without a translation only the Chinese name is shown. The game's own name always wins.
    /// </summary>
    static void ApplyKoreanBuffs(Dictionary<int, BuffMeta> buffs)
    {
        var translations = ReadKoreanBuffs();
        var confirmed = ReadConfirmedBuffNames();
        foreach (var id in buffs.Keys.ToArray())
        {
            var meta = buffs[id];
            translations.TryGetValue(id, out var translation);
            // a name that was checked by hand stands alone, without the Chinese next to it
            var name = meta.Name.Length > 0 ? meta.Name : confirmed.TryGetValue(id, out var sure) ? sure : CombineBuffName(translation.Name, meta.Design);
            var description = meta.Description.Length > 0 ? meta.Description : translation.Desc ?? "";
            if (name != meta.Name || description != meta.Description) buffs[id] = meta with { Name = name, Description = description };
        }
    }

    internal static string CombineBuffName(string? translated, string design)
    {
        translated ??= "";
        if (translated.Length > 0 && design.Length > 0 && translated != design) return translated + " · " + design;
        return translated.Length > 0 ? translated : design;
    }

    /// <summary>Buff names the game leaves empty that were checked by hand (<c>BuffNames.confirmed.json</c>, id → name): they are shown as they are.</summary>
    static Dictionary<int, string> ReadConfirmedBuffNames()
    {
        var map = new Dictionary<int, string>();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "BuffNames.confirmed.json");
            if (!File.Exists(path)) return map;
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var name = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? "" : "";
                if (int.TryParse(entry.Name, out var id) && name.Length > 0) map[id] = name;
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    static Dictionary<int, (string Name, string Desc)> ReadKoreanBuffs()
    {
        var map = new Dictionary<int, (string Name, string Desc)>();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "BuffText.ko.json");
            if (!File.Exists(path)) return map;
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id) || entry.Value.ValueKind != JsonValueKind.Object) continue;
                var name = entry.Value.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                var desc = entry.Value.TryGetProperty("desc", out var descElement) ? descElement.GetString() ?? "" : "";
                if (name.Length == 0 && desc.Length == 0) continue;
                map[id] = (name, desc);
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    /// <summary>
    /// The skills that belong together, as the skill table links them: the steps of a combo ("NextSkillId", "SkillLevelGroup"), the same skill in the
    /// air or switched ("SkySkillId", "SwitchSkillId") and the enhanced version of a skill (it points to the skill it enhances). Each skill gets the
    /// lowest id of its family; a skill without any link is not listed.
    /// </summary>
    internal static Dictionary<int, int> ReadSkillFamilies(string path)
    {
        var parent = new Dictionary<int, int>();
        int Find(int id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }

            return id;
        }

        void Join(int a, int b)
        {
            if (a == b || !parent.ContainsKey(a) || !parent.ContainsKey(b)) return;
            var (low, high) = (Find(a), Find(b));
            if (low == high) return;
            if (low > high) (low, high) = (high, low);
            parent[high] = low; // the lowest id stays the root
        }

        var links = new List<(int From, int To)>();
        if (!File.Exists(path)) return [];
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id)) continue;
                parent[id] = id;
                foreach (var field in new[] { "SkillLevelGroup", "SkySkillId", "NextSkillId", "SwitchSkillId" })
                {
                    if (entry.Value.TryGetProperty(field, out var value) && value.TryGetInt32(out var other) && other > 0) links.Add((id, other));
                }
            }
        }
        catch (Exception)
        {
            return [];
        }

        foreach (var (from, to) in links) Join(from, to);
        var roots = parent.Keys.ToDictionary(id => id, Find);
        var size = roots.Values.GroupBy(root => root).ToDictionary(group => group.Key, group => group.Count());
        return roots.Where(pair => size[pair.Value] > 1).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>The picture of every skill of the skill table: the last part of its "Icon" path ("ui/textures/skill_weapon_cq/weapon_cq-01_kx08" gives "weapon_cq-01_kx08").</summary>
    internal static Dictionary<int, string> ReadSkillIcons(string path)
    {
        var icons = new Dictionary<int, string>();
        if (!File.Exists(path)) return icons;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                var row = entry.Value;
                if (!int.TryParse(entry.Name, out var id) || !row.TryGetProperty("Icon", out var icon) || icon.ValueKind != JsonValueKind.String) continue;
                var text = icon.GetString() ?? "";
                var name = text[(text.LastIndexOf('/') + 1)..];
                if (name.Length > 0) icons[id] = name;
            }
        }
        catch (Exception)
        {
        }

        return icons;
    }

    static (Dictionary<int, string> Names, Dictionary<int, ImagineInfo> Imagines) ReadSkills(string path)
    {
        var names = new Dictionary<int, string>();
        var imagines = new Dictionary<int, ImagineInfo>();
        if (!File.Exists(path)) return (names, imagines);
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("Id", out var idElement)) continue;
                var id = idElement.GetInt32();
                var name = entry.Value.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                if (name.Length > 0) names[id] = name;
                if (!entry.Value.TryGetProperty("SlotPositionId", out var slots)) continue;
                var slot = 0;
                foreach (var item in slots.EnumerateArray())
                {
                    if (item.GetInt32() is 7 or 8) slot = item.GetInt32();
                }

                if (slot == 0) continue;
                var icon = entry.Value.TryGetProperty("Icon", out var iconElement) ? iconElement.GetString() ?? "" : "";
                var slash = icon.LastIndexOf('/');
                if (slash >= 0) icon = icon[(slash + 1)..];
                imagines[id] = new ImagineInfo(name, slot, icon);
            }
        }
        catch (Exception)
        {
        }

        return (names, imagines);
    }

    static void ReadSkillLevels(CodedInputStream reader, Fighter fighter)
    {
        fighter.Levels.Clear();
        while (!reader.IsAtEnd)
        {
            reader.ReadLength();
            var info = new SkillLevelInfo();
            reader.ReadMessage(info);
            if (info.SkillId != 0) fighter.Levels.Add((info.SkillId, info.RemodelLevel));
        }
    }

    /// <summary>The item table: id, name, quality and description of every item (for loot lines and item links in the chat).</summary>
    internal static Dictionary<int, ItemEntry> ReadItems(string path)
    {
        var map = new Dictionary<int, ItemEntry>();
        if (!File.Exists(path)) return map;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var id)) continue;
                var row = entry.Value;
                var name = row.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() ?? "" : "";
                if (name.Length == 0) continue;
                var quality = row.TryGetProperty("Quality", out var qualityElement) && qualityElement.TryGetInt32(out var q) ? q : 0;
                var description = row.TryGetProperty("Description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String ? descriptionElement.GetString() ?? "" : "";
                var icon = row.TryGetProperty("Icon", out var iconElement) && iconElement.ValueKind == JsonValueKind.String ? iconElement.GetString() ?? "" : "";
                map[id] = new ItemEntry(id, name, quality, description, icon);
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    static (Dictionary<int, BuffMeta> Meta, HashSet<int> Cuisine, HashSet<int> Syrup) ReadBuffMeta(string path)
    {
        var map = new Dictionary<int, BuffMeta>();
        var cuisine = new HashSet<int>();
        var syrup = new HashSet<int>();
        if (!File.Exists(path)) return (map, cuisine, syrup);
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("Id", out var idElement) || !idElement.TryGetInt32(out var id) || id == 0)
                    continue;
                var name = entry.Value.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                var description = entry.Value.TryGetProperty("Desc", out var descElement) ? descElement.GetString() ?? "" : "";
                var design = entry.Value.TryGetProperty("NameDesign", out var designElement) && designElement.ValueKind == JsonValueKind.String ? designElement.GetString() ?? "" : "";
                var skill = entry.Value.TryGetProperty("SkillId", out var skillElement) && skillElement.TryGetInt32(out var skillId) ? skillId : 0;
                var ability = entry.Value.TryGetProperty("BuffAbilityType", out var abilityElement) && abilityElement.TryGetInt32(out var abilityType) ? abilityType : 0;
                if (ability == 101) cuisine.Add(id);
                else if (ability == 102) syrup.Add(id);
                var duration = 0.0;
                if (entry.Value.TryGetProperty("DestroyParam", out var destroy) && destroy.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in destroy.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Array) continue;
                        var numbers = row.EnumerateArray().Select(item => item.TryGetDouble(out var value) ? value : 0).ToArray();
                        if (numbers.Length >= 2 && numbers[0] == 0 && numbers[1] > duration)
                            duration = numbers[1];
                    }
                }

                map[id] = new BuffMeta(name, skill, duration, description, design);
            }
        }
        catch (Exception)
        {
        }

        return (map, cuisine, syrup);
    }

    static Dictionary<int, double> ReadCooldowns(string path)
    {
        var map = new Dictionary<int, double>();
        if (!File.Exists(path)) return map;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("SkillId", out var skillElement) || !skillElement.TryGetInt32(out var skill) || skill == 0)
                    continue;
                if (!entry.Value.TryGetProperty("PVECoolTime", out var coolElement) || !coolElement.TryGetDouble(out var cool) || cool <= 0)
                    continue;
                if (!map.TryGetValue(skill, out var current) || cool > current)
                    map[skill] = cool;
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    static Dictionary<int, string> ReadNames(string path)
    {
        var map = new Dictionary<int, string>();
        if (!File.Exists(path)) return map;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("Id", out var id) || !entry.Value.TryGetProperty("Name", out var name)) continue;
                var text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text)) map[id.GetInt32()] = text;
            }
        }
        catch (Exception)
        {
        }

        return map;
    }

    static bool IsPlayer(long uuid) => ((uuid >> 6) & 31) == (long)EEntityType.EntChar;

    static bool IsMonster(long uuid) => ((uuid >> 6) & 31) == (long)EEntityType.EntMonster;

    static HashSet<int> ReadBosses(string path)
    {
        var bosses = new HashSet<int>();
        if (!File.Exists(path)) return bosses;
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("MonsterType", out var type) || !type.TryGetInt32(out var kind) || kind != 2)
                    continue;
                if (entry.Value.TryGetProperty("Id", out var idElement) && idElement.TryGetInt32(out var id) && id > 0)
                    bosses.Add(id);
                else if (int.TryParse(entry.Name, out var key) && key > 0)
                    bosses.Add(key);
            }
        }
        catch (Exception)
        {
        }

        return bosses;
    }

}
