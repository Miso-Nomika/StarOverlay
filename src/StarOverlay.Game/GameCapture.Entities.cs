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
    void OnAppear(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var message = SyncNearEntities.Parser.ParseFrom(payload);
            lock (_gate)
            {
                if (message.Appear != null)
                {
                    if (_freshRoster)
                    {
                        _freshRoster = false;
                        _awaitSelf = false;
                    }

                    foreach (var entity in message.Appear)
                    {
                        if (entity.Uuid == 0) continue;
                        if (IsPlayer(entity.Uuid))
                        {
                            if (!TrackPlayer(entity.Uuid)) continue;
                            var fighter = Take(entity.Uuid);
                            if (entity.Attrs?.Attrs != null) ReadAttrs(fighter, entity.Attrs.Attrs, entity.Uuid);
                            if (entity.BuffInfos != null) ReadBuffList(fighter, entity.BuffInfos.BuffInfos);
                            if (entity.BuffEffect != null) ReadBuffs(fighter, entity.BuffEffect);
                        }
                        else if (IsMonster(entity.Uuid))
                            ReadMonster(entity.Uuid, entity.Attrs?.Attrs);
                    }
                }

                if (message.Disappear != null)
                {
                    foreach (var gone in message.Disappear)
                    {
                        if (gone.Type == EDisappearType.EdisappearDead && IsMonster(gone.Uuid))
                            NoteMonsterDead(gone.Uuid, fromHit: false);
                    }
                }

                _seen = true;
                _appear++;
                RefreshImagineSpecs();
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnDelta(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var message = SyncNearDeltaInfo.Parser.ParseFrom(payload);
            if (message.DeltaInfos == null) return;
            lock (_gate)
            {
                foreach (var delta in message.DeltaInfos) ReadDelta(delta);
                _seen = true;
                _delta++;
                RefreshImagineSpecs();
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnMe(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var message = SyncToMeDeltaInfo.Parser.ParseFrom(payload);
            var mine = message.DeltaInfo;
            if (mine == null) return;
            lock (_gate)
            {
                if (mine.Uuid != 0) _self = mine.Uuid;
                AdmitSelf(mine.Uuid);
                if (mine.BaseDelta != null) ReadDelta(mine.BaseDelta);
                _seen = true;
                _delta++;
                RefreshImagineSpecs();
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void ReadDelta(AoiSyncDelta delta)
    {
        if (delta.Uuid != 0 && IsPlayer(delta.Uuid) && TrackPlayer(delta.Uuid))
        {
            var fighter = Take(delta.Uuid);
            if (delta.Attrs?.Attrs != null) ReadAttrs(fighter, delta.Attrs.Attrs, delta.Uuid);
            if (delta.BuffEffect != null) ReadBuffs(fighter, delta.BuffEffect);
        }
        else if (delta.Uuid != 0 && IsMonster(delta.Uuid) && delta.Attrs?.Attrs != null)
            ReadMonster(delta.Uuid, delta.Attrs.Attrs);

        var damages = delta.SkillEffects?.Damages;
        if (damages == null) return;
        foreach (var hit in damages) ReadHit(hit, delta.Uuid);
    }

    void ReadHit(SyncDamageInfo hit, long target)
    {
        if (hit.OwnerId == 0) return;
        var attacker = hit.TopSummonerId != 0 ? hit.TopSummonerId : hit.AttackerUuid;
        if (attacker == 0 || !IsPlayer(attacker) || !TrackPlayer(attacker)) return;
        var fighter = Take(attacker);
        NoteProfession(fighter, hit.OwnerId);
        if (hit.IsMiss || hit.Type == EDamageType.Heal) return;
        var amount = hit.Value != 0 ? hit.Value : hit.LuckyValue;
        if (amount < 0) amount = hit.HpLessenValue > 0 ? hit.HpLessenValue : 0;
        if (amount <= 0) return;
        fighter.Damage += amount;
        if (!fighter.Skills.TryGetValue(hit.OwnerId, out var skill))
        {
            skill = new SkillAcc();
            fighter.Skills[hit.OwnerId] = skill;
        }

        skill.Damage += amount;
        skill.Hits++;
        _hits++;
        NoteHit(attacker);
        if (hit.IsCrit || (hit.TypeFlag & 1) == 1) skill.Crits++;
        if ((hit.TypeFlag & 4) == 4) skill.Luckies++;
        if (hit.IsDead && target != 0)
        {
            if (IsPlayer(target)) Take(target).Dead = true;
            else if (IsMonster(target)) NoteMonsterDead(target, fromHit: true);
        }
    }

    void ReadAttrs(Fighter fighter, IEnumerable<Attr> attrs, long uuid = 0)
    {
        foreach (var attr in attrs)
        {
            if ((EAttrType)attr.Id == EAttrType.AttrCombatState)
            {
                var state = attr.RawData == null || attr.RawData.Length == 0
                    ? 0
                    : new CodedInputStream(attr.RawData.ToByteArray()).ReadInt32();
                NoteCombatant(uuid, state > 0);
                continue;
            }

            if (attr.Id == 0 || attr.RawData == null || attr.RawData.Length == 0) continue;
            var reader = new CodedInputStream(attr.RawData.ToByteArray());
            switch ((EAttrType)attr.Id)
            {
                case EAttrType.AttrName:
                    var name = reader.ReadString().Trim();
                    if (name.Length > 0) fighter.Name = name;
                    break;
                case EAttrType.AttrProfessionId:
                    fighter.ProfessionId = reader.ReadInt32();
                    break;
                case EAttrType.AttrFightPoint:
                    fighter.FightPoint = SpecBook.AcceptFightPoint(reader.ReadInt32(), fighter.FightPoint); // 10만 or more (some dungeons) is ignored
                    break;
                case EAttrType.AttrSeasonStrength:
                    fighter.Season = reader.ReadInt32();
                    break;
                case EAttrType.AttrHp:
                    var hp = reader.ReadInt64();
                    fighter.Dead = hp <= 0;
                    break;
                case EAttrType.AttrEquipData:
                    ReadEquip(reader, fighter);
                    break;
                case EAttrType.AttrSkillLevelIdList:
                    ReadSkillLevels(reader, fighter);
                    break;
                case EAttrType.AttrPos:
                    var pos = Vec3.Parser.ParseFrom(reader);
                    fighter.X = pos.X;
                    fighter.Z = pos.Z;
                    fighter.HasPos = true;
                    break;
            }
        }

        TryGearSpec(fighter);
    }

    void NoteHit(long attacker)
    {
        var arm = LiveCombat.CombatArm;
        if (arm != _combatArm)
        {
            _combatArm = arm;
            _fighting.Clear();
        }

        NoteCombatant(attacker, true);
    }

    void NoteCombatant(long uuid, bool fighting)
    {
        if (uuid == 0 || !IsPlayer(uuid)) return;
        if (fighting)
        {
            if (_fighting.Add(uuid) && _fighting.Count == 1)
                LiveCombat.NoteCombat();
            return;
        }

        _fighting.Remove(uuid);
    }

    void ReadMonster(long uuid, IEnumerable<Attr>? attrs)
    {
        if (attrs == null) return;
        foreach (var attr in attrs)
        {
            if (attr.RawData == null || attr.RawData.Length == 0) continue;
            var reader = new CodedInputStream(attr.RawData.ToByteArray());
            switch ((EAttrType)attr.Id)
            {
                case EAttrType.AttrId:
                    NoteMonsterId(uuid, reader.ReadInt32());
                    break;
                case EAttrType.AttrState:
                    NoteMonsterState(uuid, reader.ReadInt32());
                    break;
                case EAttrType.AttrMaxHp:
                    NoteMonsterMaxHp(uuid, reader.ReadInt64());
                    break;
                case EAttrType.AttrCombatState:
                case EAttrType.AttrInBattleShow:
                    NoteBossFlag(uuid, (EAttrType)attr.Id, reader.ReadInt32());
                    break;
                case EAttrType.AttrHp:
                    NoteMonsterHp(uuid, reader.ReadInt64());
                    break;
            }
        }
    }

    void NoteMonsterId(long uuid, int id)
    {
        if (id <= 0) return;
        var monster = TakeMonster(uuid);
        var first = monster.Id != id;
        monster.Id = id;
        if (first && _bosses.Contains(id))
        {
            foreach (var pair in _monsters)
            {
                var other = pair.Value;
                if (pair.Key == uuid || other.Id != id || other.Dead || other.Lowest >= 0.9) continue;
                other.Lowest = 1;
                CloseFightByWipe($"a new boss {id} appeared while the earlier one was hurt and alive");
                break;
            }
        }

        if (monster.Dead) FinishBoss(monster);
    }

    void NoteMonsterState(long uuid, int state)
    {
        var monster = TakeMonster(uuid);
        if (state == (int)EActorState.ActorStateDead)
        {
            if (!monster.SeenAlive) return;
            monster.Dead = true;
            FinishBoss(monster);
            return;
        }

        // born again after being hurt: the boss was reset
        if (state == (int)EActorState.ActorStateBorn && monster.Lowest < 0.9 && monster.Id > 0 && _bosses.Contains(monster.Id))
        {
            monster.Lowest = 1;
            CloseFightByWipe($"boss {monster.Id} was born again");
        }

        monster.SeenAlive = true;
        if (monster.Dead)
        {
            monster.Dead = false;
            monster.Closed = false;
        }
    }

    // What the game says about a boss being in a fight (the boss bar at the top of the screen): only written down for now, to see
    // in a real wipe which of these changes when the bar goes away
    void NoteBossFlag(long uuid, EAttrType kind, int value)
    {
        var monster = TakeMonster(uuid);
        if (monster.Id <= 0 || !_bosses.Contains(monster.Id)) return;
        ref var field = ref (kind == EAttrType.AttrCombatState ? ref monster.CombatState : ref monster.BattleShow);
        if (field == value) return;
        field = value;
    }

    void NoteMonsterMaxHp(long uuid, long maxHp)
    {
        if (maxHp > 0) TakeMonster(uuid).MaxHp = maxHp;
    }

    // A boss that was hurt and is suddenly back at full health (or is born again) means the party wiped and the boss reset:
    // the same signs the original ZDPS uses, taken from the boss's own packets instead of waiting for a dungeon-end signal.
    void NoteBossHealth(Monster monster, long hp)
    {
        if (monster.MaxHp <= 0 || monster.Id <= 0 || !_bosses.Contains(monster.Id)) return;
        var share = (double)hp / monster.MaxHp;
        if (share < 0.97)
        {
            monster.Lowest = Math.Min(monster.Lowest, share);
            return;
        }

        if (share >= 0.995 && monster.Lowest < 0.9)
        {
            var was = monster.Lowest;
            monster.Lowest = 1;
            CloseFightByWipe($"boss {monster.Id} is back at full health (it had been down to {was:P0})");
        }
    }

    DateTime _lastWipeClose;

    /// <summary>How long after a dungeon end, a boss kill or a boss reset (a wipe) the meter is cleared (the players can still read the result).</summary>
    internal static readonly TimeSpan EncounterEndDelay = TimeSpan.FromSeconds(3);

    /// <summary>Ends the fight now (zero delay) or after <paramref name="delay"/>.</summary>
    internal static void EndEncounter(TimeSpan delay, EncounterEndKind kind = EncounterEndKind.None)
    {
        if (delay <= TimeSpan.Zero) LiveCombat.NoteEncounterEnd(kind);
        else LiveCombat.ScheduleEncounterEnd(delay, kind);
    }

    /// <summary>
    /// The boss was reset (the party wiped): close the fight so the meter starts from zero, three seconds later (once per wipe, only when there was a fight).
    /// A wipe in a section of ordinary monsters does not count: the party revives there and goes on fighting.
    /// </summary>
    void CloseFightByWipe(string why)
    {
        if ((DateTime.UtcNow - _lastWipeClose).TotalSeconds < 5) return;
        if (!LiveCombat.Encounters || !_fighters.Values.Any(fighter => fighter.Damage > 0)) return;
        _lastWipeClose = DateTime.UtcNow;
        EndEncounter(EncounterEndDelay, EncounterEndKind.Wipe);
    }

    void NoteMonsterHp(long uuid, long hp)
    {
        var monster = TakeMonster(uuid);
        if (hp > 0) NoteBossHealth(monster, hp);
        if (hp > 0)
        {
            monster.SeenAlive = true;
            if (monster.Dead)
            {
                monster.Dead = false;
                monster.Closed = false;
            }

            return;
        }

        if (!monster.SeenAlive) return;
        monster.Dead = true;
        FinishBoss(monster);
    }

    void NoteMonsterDead(long uuid, bool fromHit)
    {
        var monster = TakeMonster(uuid);
        if (fromHit) monster.SeenAlive = true;
        if (!monster.SeenAlive) return;
        monster.Dead = true;
        FinishBoss(monster);
    }

    void FinishBoss(Monster monster)
    {
        if (monster.Closed || !monster.Dead || !monster.SeenAlive || monster.Id <= 0) return;
        if (!_bosses.Contains(monster.Id)) return;
        if (!LiveCombat.Encounters) return;
        if (!_fighters.Values.Any(fighter => fighter.Damage > 0)) return;
        monster.Closed = true;
        LiveCombat.ScheduleEncounterEnd(TimeSpan.FromSeconds(3), EncounterEndKind.BossKill);
    }

    Monster TakeMonster(long uuid)
    {
        if (!_monsters.TryGetValue(uuid, out var monster))
        {
            monster = new Monster();
            _monsters[uuid] = monster;
        }

        return monster;
    }

}
