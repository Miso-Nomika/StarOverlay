namespace StarOverlay.Framework;

public static class SpecBook
{
    static readonly object Gate = new();
    static readonly Dictionary<long, Entry> _specs = [];

    /// <summary>Some dungeons report a fight power of 100,000 or more (141006 and the like). It is not the character's own, so it is ignored.</summary>
    public const long FightPointLimit = 100_000;

    /// <summary>The fight power to keep: the incoming one, or <paramref name="current"/> when the incoming one is at or over the limit.</summary>
    public static long AcceptFightPoint(long incoming, long current) => incoming >= FightPointLimit ? current : incoming;

    /// <summary>The season of the data we collect now. Characters saved before seasons existed count as season 3.</summary>
    public const int CurrentSeason = 4;

    /// <summary>The season of a saved character (0 when it is not known).</summary>
    public static int SeasonOf(long charId)
    {
        lock (Gate) return _specs.TryGetValue(charId, out var current) ? current.Season : 0;
    }

    /// <summary>
    /// Remembers what is known about a character. A newer <paramref name="season"/> replaces an older one whatever the fight power or the
    /// confirmation say (the old fight power and gear belong to the old season); an older one never replaces a newer one.
    /// </summary>
    public static bool Remember(long charId, string spec, bool check = false, long fightPoint = 0, bool? farSea = null, int season = CurrentSeason)
    {
        if (charId == 0 || PartyJobs.ProfessionOf(spec) == 0) return false;
        fightPoint = AcceptFightPoint(fightPoint, 0); // the character is remembered, the inflated number is not
        lock (Gate)
        {
            if (_specs.TryGetValue(charId, out var current))
            {
                if (season < current.Season) return false;
                if (season > current.Season)
                {
                    _specs[charId] = new Entry(spec, check, fightPoint, farSea ?? false, season);
                    return true;
                }

                var sameProfession = PartyJobs.ProfessionOf(current.Spec) == PartyJobs.ProfessionOf(spec);
                var strongerOther = !sameProfession && fightPoint > current.FightPoint;
                if (current.Check && !check && !strongerOther)
                {
                    var kept = current;
                    if (sameProfession && fightPoint > current.FightPoint)
                        kept = kept with { FightPoint = fightPoint };
                    if (farSea is bool sea)
                        kept = kept with { FarSea = sea };
                    if (kept == current) return false;
                    _specs[charId] = kept;
                    return true;
                }

                var nextFight = fightPoint > 0 ? fightPoint : current.FightPoint;
                var nextSea = farSea ?? current.FarSea;
                if (current.Spec == spec && current.Check == check && current.FightPoint == nextFight && current.FarSea == nextSea) return false;
                _specs[charId] = new Entry(spec, check, nextFight, nextSea, season);
                return true;
            }

            _specs[charId] = new Entry(spec, check, fightPoint, farSea ?? false, season);
            return true;
        }
    }

    public static bool Forget(long charId)
    {
        if (charId == 0) return false;
        lock (Gate)
        {
            if (!_specs.TryGetValue(charId, out var current) || current.Check) return false;
            return _specs.Remove(charId);
        }
    }

    public static bool IsChecked(long charId, out string spec)
    {
        if (!TryGet(charId, out spec, out var check, out _)) return false;
        if (check) return true;
        spec = "";
        return false;
    }

    public static bool TryGet(long charId, out string spec)
    {
        var found = TryGet(charId, out spec, out _, out _);
        return found;
    }

    public static bool TryGet(long charId, out string spec, out bool check) =>
        TryGet(charId, out spec, out check, out _);

    public static bool TryGet(long charId, out string spec, out bool check, out long fightPoint) =>
        TryGet(charId, out spec, out check, out fightPoint, out _);

    public static bool TryGet(long charId, out string spec, out bool check, out long fightPoint, out bool farSea)
    {
        lock (Gate)
        {
            if (_specs.TryGetValue(charId, out var current))
            {
                spec = current.Spec;
                check = current.Check;
                fightPoint = current.FightPoint;
                farSea = current.FarSea;
                return true;
            }
        }

        spec = "";
        check = false;
        fightPoint = 0;
        farSea = false;
        return false;
    }

    public static void Clear()
    {
        lock (Gate) _specs.Clear();
    }

    readonly record struct Entry(string Spec, bool Check, long FightPoint, bool FarSea, int Season);
}
