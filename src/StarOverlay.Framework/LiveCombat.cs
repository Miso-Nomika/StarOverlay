namespace StarOverlay.Framework;

public sealed record LiveSkill(string Name, long Damage, int Hits, int Crits, int Id = 0, int Luckies = 0);

/// <param name="EndsAtMs">When the buff ends, in Unix ms of this PC's clock; 0 when it has no duration.</param>
public sealed record LiveBuff(int Id, string Name, double Duration = 0, double Cooldown = 0, int Stacks = 0, string Description = "", long EndsAtMs = 0);

public sealed record LiveImagine(string Name, int Tier, string Icon);

public sealed record LiveFighter(
    string Name,
    int ProfessionId,
    string Profession,
    long AbilityScore,
    int SeasonStrength,
    long Damage,
    bool Dead,
    bool Self,
    string Weapon,
    IReadOnlyList<LiveSkill> Skills,
    IReadOnlyList<LiveBuff> Buffs,
    IReadOnlyList<LiveImagine>? Imagines = null,
    long CharId = 0,
    bool Cuisine = false,
    bool Syrup = false,
    string CuisineEffect = "",
    string SyrupEffect = "");

public static class LiveCombat
{
    static LiveFighter[] _rows = [];
    static int _generation;
    static string _note = "";
    static string _place = "";
    static string _location = "";
    static int _recordable;
    static int _sceneId;
    static int _sceneChanges;
    static int _encounterEnds;
    static long _scheduledEndTicks;
    static int _combatEpoch;
    static int _combatArm;
    static long _startedTicks = DateTime.UtcNow.Ticks;

    public static int Generation => Volatile.Read(ref _generation);

    public static string Note => _note;

    public static string Place => _place;

    public static string Location => _location;

    public static bool Recordable => Volatile.Read(ref _recordable) != 0;

    public static int SceneChanges => Volatile.Read(ref _sceneChanges);

    public static int EncounterEnds => Volatile.Read(ref _encounterEnds);

    public static int CombatEpoch => Volatile.Read(ref _combatEpoch);

    public static int CombatArm => Volatile.Read(ref _combatArm);

    public static bool EndPending
    {
        get
        {
            var due = Volatile.Read(ref _scheduledEndTicks);
            return due != 0 && DateTime.UtcNow.Ticks < due;
        }
    }

    public static DateTime Started => new(Volatile.Read(ref _startedTicks), DateTimeKind.Utc);

    public static IReadOnlyList<LiveFighter> Rows => Volatile.Read(ref _rows) ?? [];

    public static void Publish(LiveFighter[] rows)
    {
        Volatile.Write(ref _rows, rows);
        Interlocked.Increment(ref _generation);
    }

    public static void SetNote(string note) => _note = note;

    public static void SetPlace(string place) => _place = place;

    public static void SetLocation(string location, bool recordable)
    {
        _location = location ?? "";
        Volatile.Write(ref _recordable, recordable ? 1 : 0);
    }

    public static void SetStarted(DateTime started) =>
        Volatile.Write(ref _startedTicks, started.ToUniversalTime().Ticks);

    public static void NoteScene(int sceneId)
    {
        if (sceneId <= 0) return;
        var previous = Interlocked.Exchange(ref _sceneId, sceneId);
        if (previous > 0 && previous != sceneId)
            Interlocked.Increment(ref _sceneChanges);
    }

    public static void NoteMap() => Interlocked.Increment(ref _sceneChanges);

    public static void NoteEncounterEnd() => Interlocked.Increment(ref _encounterEnds);

    public static void ScheduleEncounterEnd(TimeSpan delay)
    {
        var due = DateTime.UtcNow.Add(delay).Ticks;
        Volatile.Write(ref _scheduledEndTicks, due);
    }

    public static void CancelScheduledEnd() => Volatile.Write(ref _scheduledEndTicks, 0);

    public static bool TakeScheduledEnd()
    {
        var due = Volatile.Read(ref _scheduledEndTicks);
        if (due == 0 || DateTime.UtcNow.Ticks < due) return false;
        return Interlocked.CompareExchange(ref _scheduledEndTicks, 0, due) == due;
    }

    public static void NoteCombat() => Interlocked.Increment(ref _combatEpoch);

    public static void ArmCombat() => Interlocked.Increment(ref _combatArm);

    public static void Clear()
    {
        Volatile.Write(ref _rows, []);
        Interlocked.Exchange(ref _generation, 0);
        _note = "";
        _place = "";
        _location = "";
        _recordable = 0;
        _sceneId = 0;
        _sceneChanges = 0;
        _encounterEnds = 0;
        _scheduledEndTicks = 0;
        _combatEpoch = 0;
        _combatArm = 0;
        Volatile.Write(ref _startedTicks, DateTime.UtcNow.Ticks);
    }
}
