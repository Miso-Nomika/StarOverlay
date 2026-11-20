namespace StarOverlay.Framework;

public sealed record PartyApplicant(
    string ApplyId,
    long CharId,
    string Name,
    int ProfessionId,
    string Profession,
    long FightPoint,
    int SeasonStrength,
    DateTime At,
    int ApplyType = 0,
    int TalentId = 0);

public static class PartyJobs
{
    public static string Name(int professionId, int talentId)
    {
        var spec = Spec(talentId);
        if (spec.Length == 0) spec = Spec(professionId);
        if (spec.Length == 0 && talentId is 1 or 2) spec = Slot(professionId, talentId);
        return spec.Length > 0 ? spec : Base(professionId);
    }

    public static bool IsSpec(string name) => SpecNames.Contains(name);

    static string Slot(int professionId, int slot)
    {
        var profession = professionId >= 10000 ? professionId / 10000 : professionId;
        return (profession, slot) switch
        {
            (1, 1) => "발도",
            (1, 2) => "월광",
            (2, 1) => "얼창",
            (2, 2) => "레이",
            (3, 1) => "무상",
            (3, 2) => "적홍",
            (4, 1) => "질풍",
            (4, 2) => "난무",
            (5, 1) => "심판",
            (5, 2) => "치유",
            (9, 1) => "암방",
            (9, 2) => "가드",
            (11, 1) => "늑대활",
            (11, 2) => "매활",
            (12, 1) => "신방",
            (12, 2) => "광휘",
            (13, 1) => "음파",
            (13, 2) => "협주",
            _ => "",
        };
    }

    static readonly HashSet<string> SpecNames =
    [
        "발도", "월광", "얼창", "레이", "무상", "적홍", "질풍", "난무",
        "심판", "치유", "암방", "가드", "늑대활", "매활", "신방", "광휘", "음파", "협주",
    ];

    public static string Spec(int talentId) => talentId switch
    {
        101 or 10001 => "발도",
        102 or 10002 => "월광",
        104 or 20001 => "얼창",
        105 or 20002 => "레이",
        124 or 30001 => "무상",
        125 or 30002 => "적홍",
        107 or 40001 => "질풍",
        108 or 40002 => "난무",
        110 or 50001 => "심판",
        111 or 50002 => "치유",
        113 or 90001 => "암방",
        114 or 90002 => "가드",
        116 or 110001 => "늑대활",
        117 or 110002 => "매활",
        122 or 120001 => "신방",
        123 or 120002 => "광휘",
        119 or 130001 => "음파",
        120 or 130002 => "협주",
        _ => "",
    };

    public static int ProfessionOf(string spec) => spec switch
    {
        "발도" or "월광" => 1,
        "얼창" or "레이" => 2,
        "무상" or "적홍" => 3,
        "질풍" or "난무" => 4,
        "심판" or "치유" => 5,
        "암방" or "가드" => 9,
        "늑대활" or "매활" => 11,
        "신방" or "광휘" => 12,
        "음파" or "협주" => 13,
        _ => 0,
    };

    public static int Normalize(int professionId) => professionId >= 10000 ? professionId / 10000 : professionId;

    public static string Base(int professionId)
    {
        var id = professionId >= 10000 ? professionId / 10000 : professionId;
        return id switch
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
    }
}

public static class LiveParty
{
    static readonly object Gate = new();
    static readonly Dictionary<string, PartyApplicant[]> _applies = [];
    static readonly Dictionary<long, string> _seenById = [];
    static PartyApplicant[] _rows = [];
    static int _generation;

    public static int Generation => Volatile.Read(ref _generation);

    public static IReadOnlyList<PartyApplicant> Rows => Volatile.Read(ref _rows) ?? [];

    public static void Replace(string applyId, PartyApplicant[] people)
    {
        if (applyId.Length == 0) return;
        lock (Gate)
        {
            if (people.Length == 0) _applies.Remove(applyId);
            else _applies[applyId] = people.Select(Decorate).ToArray();
            Publish();
        }
    }

    public static void Forget(long charId)
    {
        if (charId == 0) return;
        lock (Gate)
        {
            _seenById.Remove(charId);
            if (ApplyKnown()) Publish();
        }
    }

    public static void NoteSeen(long charId, string spec)
    {
        if (charId == 0 || !PartyJobs.IsSpec(spec)) return;
        lock (Gate)
        {
            _seenById[charId] = spec;
            if (ApplyKnown()) Publish();
        }
    }

    public static void Remove(string applyId)
    {
        if (applyId.Length == 0) return;
        lock (Gate)
        {
            var removed = _applies.Remove(applyId);
            foreach (var key in _applies.Keys.ToArray())
            {
                if (!_applies[key].Any(person => person.ApplyId == applyId)) continue;
                _applies.Remove(key);
                removed = true;
            }

            if (removed) Publish();
        }
    }

    public static void RemoveType(int applyType)
    {
        lock (Gate)
        {
            var removed = false;
            foreach (var key in _applies.Keys.ToArray())
            {
                if (!_applies[key].Any(person => person.ApplyType == applyType)) continue;
                _applies.Remove(key);
                removed = true;
            }

            if (removed) Publish();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _applies.Clear();
            Publish();
        }
    }

    static bool ApplyKnown()
    {
        var changed = false;
        foreach (var key in _applies.Keys.ToArray())
        {
            var next = _applies[key].Select(person =>
            {
                var updated = Decorate(person);
                if (updated != person) changed = true;
                return updated;
            }).ToArray();
            _applies[key] = next;
        }

        return changed;
    }

    static PartyApplicant Decorate(PartyApplicant person)
    {
        var profession = PartyJobs.Base(person.ProfessionId);
        var family = PartyJobs.Normalize(person.ProfessionId);
        if (person.CharId != 0 && family != 0 && SpecBook.TryGet(person.CharId, out var saved) && PartyJobs.ProfessionOf(saved) == family)
            profession = saved;
        else if (person.CharId != 0 && family != 0 && _seenById.TryGetValue(person.CharId, out var seen) && PartyJobs.ProfessionOf(seen) == family)
            profession = seen;
        if (profession.Length == 0) profession = person.Profession;
        return profession == person.Profession ? person : person with { Profession = profession };
    }

    static void Publish()
    {
        var rows = _applies.Values
            .SelectMany(people => people)
            .OrderByDescending(person => person.At)
            .ThenBy(person => person.Name, StringComparer.Ordinal)
            .ToArray();
        Volatile.Write(ref _rows, rows);
        Interlocked.Increment(ref _generation);
    }
}
