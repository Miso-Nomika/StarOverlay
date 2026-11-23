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
    int TalentId = 0,
    int MasterScore = -1,
    bool MasterShown = true);

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

    /// <summary>
    /// A healer: a healing spec (치유, 협주), or one of the two healer classes (실반오라클, 비트퍼포머) whose spec is not known (shown as the class name). The
    /// other specs of those classes (심판, 음파) are not healers.
    /// </summary>
    public static bool IsHealer(int professionId, string profession) =>
        profession is "협주" or "치유" || (Normalize(professionId) is 5 or 13 && !IsSpec(profession));

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
    static readonly Dictionary<long, (int Score, bool Shown)> _masters = []; // the master scores read from the game (-1 = not read yet)
    static bool _active;
    static string _request = "";
    static int _serial;
    static string _failedApply = "";
    static long _failedUntilMs;

    /// <summary>Raised when the plugin is switched on or off, a master score is wanted, or the player accepted or rejected an application: the agent is told.</summary>
    public static event Action? Changed;

    /// <summary>The party join plugin is loaded (the agent reads and calls the game only then).</summary>
    public static bool Active
    {
        get { lock (Gate) return _active; }
    }

    /// <summary>
    /// A plugin that is loaded switches this on, one that is unloaded switches it off. The service builds the plugins of a new host before it lets the old host go
    /// (switching any plugin on or off does that), so the old and the new instance of the same plugin are both there for a moment: the plugin counts as on
    /// while at least one instance holds it.
    /// </summary>
    public static void SetActive(bool active)
    {
        lock (Gate)
        {
            _holders = Math.Max(0, _holders + (active ? 1 : -1));
            var now = _holders > 0;
            if (_active == now) return;
            _active = now;
            if (!now) _masters.Clear();
        }

        Changed?.Invoke();
    }

    static int _holders;

    /// <summary>The characters in the list whose master score has not been read yet.</summary>
    public static IReadOnlyList<long> Need
    {
        get
        {
            lock (Gate) return _applies.Values.SelectMany(people => people).Select(person => person.CharId).Where(id => id != 0 && !_masters.ContainsKey(id)).Distinct().ToArray();
        }
    }

    /// <summary>What the agent read from the game: "charId|score|shown" per line.</summary>
    public static void NoteMasters(IEnumerable<(long CharId, int Score, bool Shown)> scores)
    {
        lock (Gate)
        {
            foreach (var (charId, score, shown) in scores) _masters[charId] = (score, shown);
            if (ApplyKnown()) Publish();
        }
    }

    /// <summary>The newest request for the agent: "accept:id" or "reject:id"; <see cref="Serial"/> grows with every request.</summary>
    public static (string Request, int Serial) Pending
    {
        get { lock (Gate) return (_request, _serial); }
    }

    /// <summary>The player pressed accept or reject on an application: asks the agent to hand it to the game.</summary>
    public static void RequestDeal(string applyId, bool accept)
    {
        if (applyId.Length == 0) return;
        lock (Gate)
        {
            _request = (accept ? "accept:" : "reject:") + applyId;
            _serial++;
        }

        Changed?.Invoke();
    }

    /// <summary>The game took the answer (the application leaves the list) or refused it (the row says so for three seconds).</summary>
    public static void NoteDealt(string applyId, bool ok, long nowMs)
    {
        if (ok)
        {
            Remove(applyId);
            return;
        }

        lock (Gate)
        {
            _failedApply = applyId;
            _failedUntilMs = nowMs + 3000;
            Publish();
        }
    }

    /// <summary>The application whose answer the game refused just now (shown for three seconds), or empty.</summary>
    public static string RecentFailure(long nowMs)
    {
        lock (Gate) return nowMs < _failedUntilMs ? _failedApply : "";
    }

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

        if (Active) Changed?.Invoke(); // the agent learns whose master score is wanted
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
        // the saved spec is shown only for the same profession, from the current season on (an older season's gear and spec are history), and when
        // the applicant's fight power is not below the saved one (a lower power means the build was changed since)
        if (person.CharId != 0 && family != 0 && SpecBook.TryGet(person.CharId, out var saved, out _, out var savedFight)
            && SpecBook.SeasonOf(person.CharId) >= SpecBook.CurrentSeason
            && PartyJobs.ProfessionOf(saved) == family
            && person.FightPoint >= savedFight)
            profession = saved;
        else if (person.CharId != 0 && family != 0 && _seenById.TryGetValue(person.CharId, out var seen) && PartyJobs.ProfessionOf(seen) == family)
            profession = seen;
        if (profession.Length == 0) profession = person.Profession;
        var master = person.CharId != 0 && _masters.TryGetValue(person.CharId, out var read) ? read : (Score: -1, Shown: true);
        return profession == person.Profession && master.Score == person.MasterScore && master.Shown == person.MasterShown
            ? person
            : person with { Profession = profession, MasterScore = master.Score, MasterShown = master.Shown };
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
