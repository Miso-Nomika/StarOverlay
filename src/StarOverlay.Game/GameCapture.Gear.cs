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
    /// <summary>Whether the worn gear is the real far sea (the part of the table "far" and the light red name).</summary>
    static bool IsFarSea(string weapon) =>
        weapon.Equals(FarSeaGear.RealWeapon, StringComparison.OrdinalIgnoreCase);

    static void ReadEquip(CodedInputStream reader, Fighter fighter)
    {
        var equips = new List<(int Id, int Slot)>(12);
        while (!reader.IsAtEnd)
        {
            reader.ReadLength();
            var equip = new EquipNine();
            reader.ReadMessage(equip);
            equips.Add((equip.EquipID, (int)equip.Slot));
        }

        fighter.Equips = equips.ToArray();
        ApplyEquip(fighter);
    }

    /// <summary>
    /// Reads the job and the accessory options out of what a person wears. Kept apart from reading the packet so it can run again when the
    /// game's equipment tables arrive after the person was seen (the options are worked out from those tables).
    /// </summary>
    static void ApplyEquip(Fighter fighter)
    {
        var high = false;
        var low = false;
        var profession = 0;
        var weaponJob = 0;
        var stats = new List<string>(8);
        foreach (var (equipId, slot) in fighter.Equips)
        {
            var farSea = FarSeaGear.WeaponOf(equipId);
            if (farSea == FarSeaGear.RealWeapon) high = true;
            else if (farSea == FarSeaGear.OldWeapon) low = true;
            if (AccessoryGear.TryOptions(equipId, out var first, out var second))
            {
                stats.Add(first);
                stats.Add(second);
            }

            if (!AccessoryGear.TryProfession(equipId, out var job)) continue;
            if (slot == 200) weaponJob = job;
            else if (profession == 0) profession = job;
        }

        fighter.Weapon = high ? FarSeaGear.RealWeapon : low ? FarSeaGear.OldWeapon : "";
        fighter.EquipKnown = true;
        if (fighter.ProfessionId == 0)
            fighter.ProfessionId = weaponJob != 0 ? weaponJob : profession;
        fighter.Accessory = stats.ToArray();
    }

    /// <summary>The equipment tables arrived: everybody seen so far is judged again from what they wear.</summary>
    void RecomputeEquip()
    {
        lock (_gate)
        {
            foreach (var fighter in _fighters.Values)
            {
                if (fighter.Equips.Length == 0) continue;
                ApplyEquip(fighter);
                TryGearSpec(fighter);
            }

            RefreshImagineSpecs();
        }
    }

    static void TryGearSpec(Fighter fighter)
    {
        if (fighter.SpecFromSkill || fighter.ProfessionId == 0) return;
        if (fighter.Accessory.Length == 0) return;
        var spec = GearSpec.Choose(fighter.ProfessionId, fighter.Accessory, out var tied);
        if (spec.Length > 0)
        {
            fighter.Spec = spec;
            fighter.SpecFromGear = true;
            fighter.SpecFromImagine = false;
            fighter.DropSpec = false;
            return;
        }

        if (!tied || fighter.SpecFromSkill || !ImagineSpec.IsTank(fighter.ProfessionId)) return;
        fighter.Spec = "";
        fighter.SpecFromGear = false;
        fighter.SpecFromImagine = false;
        fighter.DropSpec = true;
    }

    void RefreshImagineSpecs()
    {
        var changed = false;
        foreach (var pair in _fighters)
        {
            var fighter = pair.Value;
            if (!fighter.SpecFromGear || fighter.Spec.Length == 0 || ImagineSpec.IsTank(fighter.ProfessionId)) continue;
            var imagines = EquippedImagines(fighter);
            if (imagines.Length == 0) continue;
            if (_imagineCatalog.Note(pair.Key >> 16, fighter.Spec, imagines)) changed = true;
        }

        if (changed) SaveImagineCatalog();
        var seen = _imagineCatalog.Counts();

        foreach (var fighter in _fighters.Values)
        {
            if (fighter.SpecFromSkill || fighter.SpecFromGear) continue;
            if (ImagineSpec.IsTank(fighter.ProfessionId))
            {
                if (!fighter.SpecFromImagine) continue;
                fighter.Spec = "";
                fighter.SpecFromImagine = false;
                continue;
            }

            var spec = ImagineSpec.Choose(fighter.ProfessionId, EquippedImagines(fighter), seen);
            if (spec.Length > 0)
            {
                fighter.Spec = spec;
                fighter.SpecFromImagine = true;
                if (fighter.ProfessionId == 0)
                    fighter.ProfessionId = ImagineSpec.Profession(spec);
                continue;
            }

            if (!fighter.SpecFromImagine) continue;
            fighter.Spec = "";
            fighter.SpecFromImagine = false;
        }
    }

    int[] EquippedImagines(Fighter fighter)
    {
        var ids = new List<int>();
        foreach (var level in fighter.Levels)
        {
            if (_imagines.ContainsKey(level.Id)) ids.Add(level.Id);
        }

        return ids.Distinct().ToArray();
    }

    readonly BuffClock _buffClock = new();

    /// <summary>Now in Unix ms of this PC's clock (tests replace it).</summary>
    internal Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Remembers when the buff ends on this PC's clock, from the creation time and the duration the game sent (ms).</summary>
    void NoteEnd(ActiveBuff buff, long createdMs, long durationMs)
    {
        var now = NowMs();
        _buffClock.Observe(createdMs, now);
        buff.EndsAtMs = _buffClock.EndsAt(createdMs, durationMs / 1000.0, now);
    }

    void ReadBuffs(Fighter fighter, BuffEffectSync sync)
    {
        foreach (var effect in sync.BuffEffects)
        {
            if (effect.Type == EBuffEventType.BuffEventRemove)
            {
                fighter.Buffs.Remove(effect.BuffUuid);
                continue;
            }

            if (!fighter.Buffs.TryGetValue(effect.BuffUuid, out var buff))
                buff = new ActiveBuff();
            foreach (var logic in effect.LogicEffect)
            {
                if (logic.RawData == null || logic.RawData.Length == 0) continue;
                if (logic.EffectType == EBuffEffectLogicPbType.BuffEffectAddBuff)
                {
                    var info = BuffInfo.Parser.ParseFrom(logic.RawData);
                    if (info.BaseId != 0) buff.Id = info.BaseId;
                    if (info.Duration > 0) buff.Duration = info.Duration / 1000.0;
                    NoteEnd(buff, info.CreateTime, info.Duration);
                    if (info.Layer > 0) buff.Stacks = info.Layer;
                    else if (buff.Stacks <= 0) buff.Stacks = 1;
                    var skill = info.FightSourceInfo?.SourceConfigId ?? 0;
                    if (skill != 0) buff.SkillId = skill;
                }
                else if (logic.EffectType == EBuffEffectLogicPbType.BuffEffectBuffChange)
                {
                    var change = BuffChange.Parser.ParseFrom(logic.RawData);
                    if (change.Duration > 0)
                    {
                        buff.Duration = change.Duration / 1000.0;
                        NoteEnd(buff, change.CreateTime, change.Duration);
                    }

                    if (change.Layer > 0) buff.Stacks = change.Layer;
                }
            }

            if (buff.Id != 0) fighter.Buffs[effect.BuffUuid] = buff;
        }

        NoteMeals(fighter);
    }

    void ReadBuffList(Fighter fighter, IEnumerable<BuffInfo> infos)
    {
        var present = new HashSet<int>();
        foreach (var info in infos)
        {
            if (info.BaseId == 0) continue;
            var key = info.BuffUuid != 0 ? info.BuffUuid : info.BaseId;
            present.Add(key);
            if (!fighter.Buffs.TryGetValue(key, out var buff))
                buff = new ActiveBuff();
            buff.Id = info.BaseId;
            if (info.Duration > 0) buff.Duration = info.Duration / 1000.0;
            NoteEnd(buff, info.CreateTime, info.Duration);
            if (info.Layer > 0) buff.Stacks = info.Layer;
            else if (buff.Stacks <= 0) buff.Stacks = 1;
            var skill = info.FightSourceInfo?.SourceConfigId ?? 0;
            if (skill != 0) buff.SkillId = skill;
            fighter.Buffs[key] = buff;
        }

        foreach (var key in fighter.Buffs.Keys.ToArray())
        {
            var id = fighter.Buffs[key].Id;
            if (!_cuisine.Contains(id) && !_syrup.Contains(id)) continue;
            if (!present.Contains(key)) fighter.Buffs.Remove(key);
        }

        NoteMeals(fighter);
    }

    void NoteMeals(Fighter fighter)
    {
        var cuisine = new List<string>();
        var syrup = new List<string>();
        var seenCuisine = new HashSet<int>();
        var seenSyrup = new HashSet<int>();
        foreach (var buff in fighter.Buffs.Values)
        {
            if (_cuisine.Contains(buff.Id) && seenCuisine.Add(buff.Id))
                cuisine.Add(MealEffect(buff.Id));
            if (_syrup.Contains(buff.Id) && seenSyrup.Add(buff.Id))
                syrup.Add(MealEffect(buff.Id));
        }

        fighter.Cuisine = cuisine.Count > 0;
        fighter.Syrup = syrup.Count > 0;
        fighter.CuisineEffect = JoinEffects(cuisine);
        fighter.SyrupEffect = JoinEffects(syrup);
    }

    string MealEffect(int id)
    {
        if (!_buffMeta.TryGetValue(id, out var meta)) return "";
        return meta.Description.Length > 0 ? meta.Description : meta.Name;
    }

    static string JoinEffects(List<string> effects) =>
        string.Join("\n", effects.Where(effect => effect.Length > 0).Distinct());

    static string ImagineFile() =>
        Path.Combine(AppStorage.Root(), "imagine-spec.json");

    void OpenSpecStore()
    {
        try
        {
            var path = Path.Combine(AppStorage.Root(), "spec.db");
            _specStore = new SpecStore(path);
            _specStore.Load();
        }
        catch (Exception)
        {
            _specStore?.Dispose();
            _specStore = null;
        }
    }

    void LoadImagineCatalog()
    {
        try
        {
            using (var seed = typeof(GameCapture).Assembly.GetManifestResourceStream("StarOverlay.Game.Data.ImagineSeed.json"))
            {
                if (seed != null) _imagineCatalog.UseSeed(new StreamReader(seed, Encoding.UTF8).ReadToEnd());
            }

            var path = ImagineFile();
            if (!File.Exists(path)) return;
            _imagineCatalog.Restore(File.ReadAllText(path));
        }
        catch (Exception)
        {
        }
    }

    void SaveImagineCatalog()
    {
        try
        {
            var path = ImagineFile();
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var temp = path + ".tmp";
            File.WriteAllText(temp, _imagineCatalog.ToJson(), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception)
        {
        }
    }

    static void NoteProfession(Fighter fighter, int skillId)
    {
        var (profession, spec) = SpecFromSkill(skillId);
        if (spec.Length > 0)
        {
            fighter.Spec = spec;
            fighter.SpecFromSkill = true;
            if (profession != 0) fighter.ProfessionId = profession;
            return;
        }

        profession = ProfessionFromSkill(skillId);
        if (profession != 0 && fighter.ProfessionId == 0)
            fighter.ProfessionId = profession;
    }

    static (int Profession, string Spec) SpecFromSkill(int skillId) => skillId switch
    {
        1714 or 1734 => (1, "발도"),
        1715 or 1738 or 179906 => (1, "월광"),
        120901 or 120902 => (2, "얼창"),
        1241 => (2, "레이"),
        160102 or 2208181 or 2208172 => (3, "무상"),
        1606 or 1621 or 1622 or 35104 => (3, "적홍"),
        1405 or 1418 => (4, "질풍"),
        1419 => (4, "난무"),
        1518 or 1541 or 21402 => (5, "심판"),
        20301 => (5, "치유"),
        1941 or 2201240 => (9, "암방"),
        1930 or 1931 or 1934 or 1935 => (9, "가드"),
        2292 or 1700820 or 1700825 or 1700827 => (11, "늑대활"),
        220112 or 2203622 or 220106 => (11, "매활"),
        2405 or 2411 or 2206401 => (12, "신방"),
        2406 or 55412 or 55417 => (12, "광휘"),
        2321 or 2335 => (13, "음파"),
        2301 or 2336 or 2361 or 55302 => (13, "협주"),
        _ => (0, ""),
    };

    static int ProfessionFromSkill(int skillId) => skillId switch
    {
        1701 or 1705 or 1713 or 1714 or 1715 or 1716 or 1717 or 1718 or 1719 or 1720 or 1724 or 1728 or 1730 or 1731 => 1,
        1201 or 1210 or 1211 or 1239 or 1240 or 1241 or 1242 or 1243 or 1244 or 1245 or 1246 or 1248 => 2,
        1601 or 1605 or 1606 or 1607 or 1608 or 1609 or 1610 or 1611 or 1612 or 1613 or 1614 or 1615 or 1616 or 1617 or 1618 or 1621 => 3,
        1401 or 1410 or 1418 or 1419 or 1420 or 1421 or 1422 or 1423 or 1424 or 1425 or 1426 or 1430 or 1431 => 4,
        1501 or 1507 or 1509 or 1518 or 1519 or 1520 or 1521 or 1522 or 1523 or 1524 or 1527 or 1528 or 1529 or 1531 => 5,
        1901 or 1907 or 1917 or 1922 or 1923 or 1924 or 1925 or 1926 or 1927 or 1930 or 1932 or 1936 or 1937 or 1938 or 1940 => 9,
        2201 or 2209 or 2220 or 2222 or 2230 or 2231 or 2232 or 2233 or 2234 or 2235 or 2237 or 2238 => 11,
        2401 or 2405 or 2406 or 2407 or 2408 or 2409 or 2410 or 2412 or 2414 or 2415 or 2419 or 2420 or 2421 => 12,
        2301 or 2306 or 2307 or 2308 or 2309 or 2310 or 2311 or 2312 or 2313 or 2314 or 2315 or 2316 or 2321 or 2335 or 2336 => 13,
        _ => 0,
    };

}
