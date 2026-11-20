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
    sealed record ImagineInfo(string Name, int Slot, string Icon);

    sealed record BuffMeta(string Name, int SkillId, double Duration, string Description);

    sealed class ActiveBuff
    {
        public int Id;
        public int SkillId;
        public double Duration;
        public int Stacks;
        public long EndsAtMs; // on this PC's clock; 0: no duration
    }

    sealed class SkillAcc
    {
        public long Damage;
        public int Hits;
        public int Crits;
        public int Luckies;
    }

    sealed class Monster
    {
        public int Id;
        public bool SeenAlive;
        public bool Dead;
        public bool Closed;
        public long MaxHp;
        public int CombatState = -1;
        public int BattleShow = -1;

        /// <summary>The lowest share of its health the monster was seen at (1 = never hurt).</summary>
        public double Lowest = 1;
    }

    sealed class Fighter
    {
        public string Name = "";
        public int ProfessionId;
        public string Spec = "";
        public bool SpecFromSkill;
        public bool SpecFromGear;
        public bool SpecFromImagine;
        public bool DropSpec;
        public string[] Accessory = [];
        public (int Id, int Slot)[] Equips = [];
        public long FightPoint;
        public int Season;
        public long Damage;
        public bool Dead;
        public string Weapon = "";
        public bool EquipKnown;
        public float X;
        public float Z;
        public bool HasPos;
        public Dictionary<int, SkillAcc> Skills { get; } = [];
        public bool Cuisine;
        public bool Syrup;
        public string CuisineEffect = "";
        public string SyrupEffect = "";
        public Dictionary<int, ActiveBuff> Buffs { get; } = [];
        public List<(int Id, int Tier)> Levels { get; } = [];

        public LiveFighter ToLive(long uuid, bool self, Dictionary<int, string> skillNames, Dictionary<int, BuffMeta> buffs, Dictionary<int, double> cooldowns, Dictionary<int, ImagineInfo> imagines)
        {
            var skills = Skills
                .OrderByDescending(pair => pair.Value.Damage)
                .Select(pair => new LiveSkill(Label(skillNames, pair.Key, "스킬"), pair.Value.Damage, pair.Value.Hits, pair.Value.Crits, pair.Key, pair.Value.Luckies))
                .ToArray();
            var liveBuffs = Buffs.Values
                .Where(buff => buff.Id != 0)
                .GroupBy(buff => buff.Id)
                .Select(group => DescribeBuff(group.OrderByDescending(buff => buff.Stacks).ThenByDescending(buff => buff.Duration).First(), buffs, cooldowns))
                .ToArray();
            var equipped = Levels
                .Select(level => imagines.TryGetValue(level.Id, out var known) ? (known, level.Tier) : default)
                .Where(item => item.known != null)
                .OrderBy(item => item.known!.Slot)
                .Take(2)
                .Select(item => new LiveImagine(item.known!.Name, item.Tier, item.known.Icon))
                .ToArray();
            return new LiveFighter(
                Name.Length > 0 ? Name : "이름 없는 전투원",
                ProfessionId,
                Job(ProfessionId, Spec),
                FightPoint,
                Season,
                Damage,
                Dead,
                self,
                Weapon,
                skills,
                liveBuffs,
                equipped,
                uuid >> 16,
                Cuisine,
                Syrup,
                CuisineEffect,
                SyrupEffect);
        }

        static LiveBuff DescribeBuff(ActiveBuff buff, Dictionary<int, BuffMeta> buffs, Dictionary<int, double> cooldowns)
        {
            buffs.TryGetValue(buff.Id, out var meta);
            var name = meta != null && meta.Name.Length > 0 ? meta.Name : $"버프 {buff.Id}";
            var duration = buff.Duration > 0 ? buff.Duration : meta?.Duration ?? 0;
            var skill = buff.SkillId != 0 ? buff.SkillId : meta?.SkillId ?? 0;
            var cooldown = skill != 0 && cooldowns.TryGetValue(skill, out var seconds) ? seconds : 0;
            return new LiveBuff(buff.Id, name, duration, cooldown, Math.Max(buff.Stacks, 1), meta?.Description ?? "", buff.EndsAtMs);
        }

        static string Label(Dictionary<int, string> names, int id, string kind) =>
            names.TryGetValue(id, out var name) && name.Length > 0 ? name : $"{kind} {id}";

        static string Job(int professionId, string spec)
        {
            var id = professionId >= 10000 ? professionId / 10000 : professionId;
            var job = id switch
            {
                1 => "스톰블레이드",
                2 => "프로스트메이지",
                3 => "트윈스트라이커",
                4 => "윈드나이트",
                5 => "실반오라클",
                9 => "헤비가디언",
                11 => "디바인아처",
                12 => "실드나이트",
                13 => "비트퍼포머",
                _ => "",
            };
            return spec.Length > 0 ? spec : job;
        }
    }

}
