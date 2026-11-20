using StarOverlay.Framework;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Game.Ingame;

/// <summary>Turns a plugin's fight records into the small lists the in-game settings window shows.</summary>
public static class RecordMapper
{
    public const int MaxHeads = 60;
    public const int MaxPlayers = 24;
    public const int MaxSkills = 40;
    public const int MaxSamples = 120;
    public const int MaxBuffs = 800;

    public static List<RecordHeadDto> Heads(PluginEncounterLog log)
    {
        IReadOnlyList<EncounterHead> heads;
        try
        {
            heads = log.Heads?.Invoke() ?? log.Records().Select(r => new EncounterHead(r.Id, r.Name, r.Started, r.Current)).ToList();
        }
        catch (Exception)
        {
            return [];
        }

        return heads.OrderByDescending(h => h.Started).Take(MaxHeads)
            .Select(h => new RecordHeadDto { Id = h.Id, Name = h.Name, Started = h.Started.ToString("MM-dd HH:mm"), Current = h.Current })
            .ToList();
    }

    /// <summary>The ids of the <paramref name="count"/> oldest records of the whole log (not just the newest ones the window lists).</summary>
    public static List<string> OldestIds(PluginEncounterLog log, int count)
    {
        try
        {
            var heads = log.Heads?.Invoke() ?? log.Records().Select(r => new EncounterHead(r.Id, r.Name, r.Started, r.Current)).ToList();
            return heads.Where(h => !h.Current).OrderBy(h => h.Started).Take(count).Select(h => h.Id).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static RecordDto? Open(PluginEncounterLog log, string id)
    {
        EncounterView? view;
        try
        {
            view = log.Open?.Invoke(id) ?? log.Records().FirstOrDefault(r => r.Id == id);
        }
        catch (Exception)
        {
            return null;
        }

        return view is null ? null : Map(view);
    }

    public static RecordDto Map(EncounterView view)
    {
        // the start is saved in local time and the end in UTC: compare them on the same clock
        var seconds = Math.Max(1.0, ((view.Ended ?? DateTime.Now).ToUniversalTime() - view.Started.ToUniversalTime()).TotalSeconds);
        var hits = view.Players.Where(p => p.FirstHitTicks > 0).Select(p => p.FirstHitTicks).ToList();
        var firstHit = hits.Count == 0 ? 0 : hits.Min();
        var firstTaken = false; // with an exact tie only the first in the list gets the flag
        var top = view.Players.Count == 0 ? 0 : view.Players.Max(p => p.TotalDamage);
        var total = view.TotalDamage > 0 ? view.TotalDamage : Math.Max(1L, view.Players.Sum(p => p.TotalDamage));
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        var players = view.Players.OrderByDescending(p => p.TotalDamage).Take(MaxPlayers).Select(p =>
        {
            foreach (var buff in p.Buffs)
                if (buff.Description.Length > 0 && !info.ContainsKey(buff.Name)) info[buff.Name] = buff.Description;
            return new RecordPlayerDto
            {
                CharId = p.CharId,
                Name = p.Name,
                Self = p.Self,
                Profession = p.Profession,
                FightPoint = p.FightPoint,
                Total = p.TotalDamage,
                PerSecond = p.Samples.Count == 0 ? p.TotalDamage / seconds : p.Samples[^1].PerSecond,
                Share = (double)p.TotalDamage / total,
                Bar = top <= 0 ? 0 : (double)p.TotalDamage / top,
                BarColor = ProfessionColors.Bar(p.ProfessionId),
                SeasonStrength = p.SeasonStrength,
                TextColor = MeterColors.Text(p.Self, p.Weapon),
                Imagines = (p.Imagines ?? []).Take(2).Select(i => new ImagineDto { Name = i.Name, Tier = i.Tier, Icon = i.Icon }).ToList(),
                FirstAttacker = p.FirstHitTicks > 0 && p.FirstHitTicks == firstHit && !firstTaken && (firstTaken = true),
                Cuisine = p.Cuisine,
                Syrup = p.Syrup,
                CuisineEffect = p.CuisineEffect,
                SyrupEffect = p.SyrupEffect,
                Samples = Thin(p.Samples),
                // newest first: the latest change is at the top (within one second an end comes before the start it follows)
                Buffs = p.Buffs.OrderByDescending(b => b.Second).ThenBy(b => b.Active).Take(MaxBuffs)
                    .Select(b => new RecordBuffDto { Second = b.Second, Name = b.Name, Active = b.Active, Id = b.Id, Duration = b.Duration, Cooldown = b.Cooldown, Stacks = b.Stacks }).ToList(),
                Skills = p.Skills.OrderByDescending(s => s.Damage).Take(MaxSkills)
                    .Select(s => new RecordSkillDto { Name = s.Name, Damage = s.Damage, Hits = s.Hits, Crits = s.Crits, Luckies = s.Luckies, Id = s.Id }).ToList(),
            };
        }).ToList();
        return new RecordDto
        {
            Id = view.Id,
            Name = view.Name,
            Started = view.Started.ToString("MM-dd HH:mm"),
            TotalDamage = view.TotalDamage,
            Seconds = seconds,
            Players = players,
            BuffInfo = info.Select(pair => new BuffInfoDto { Name = pair.Key, Description = pair.Value }).ToList(),
        };
    }

    /// <summary>At most <see cref="MaxSamples"/> points, keeping the highest value of each stretch so peaks stay visible.</summary>
    public static List<RecordSampleDto> Thin(IReadOnlyList<DamageSampleView> samples)
    {
        if (samples.Count <= MaxSamples)
            return samples.Select(s => new RecordSampleDto { Second = s.Second, PerSecond = s.PerSecond }).ToList();
        var result = new List<RecordSampleDto>(MaxSamples);
        for (var i = 0; i < MaxSamples; i++)
        {
            var from = (int)((long)i * samples.Count / MaxSamples);
            var to = Math.Max(from + 1, (int)((long)(i + 1) * samples.Count / MaxSamples));
            var best = samples[from];
            for (var j = from; j < to && j < samples.Count; j++)
                if (samples[j].PerSecond > best.PerSecond) best = samples[j];
            result.Add(new RecordSampleDto { Second = best.Second, PerSecond = best.PerSecond });
        }

        return result;
    }
}