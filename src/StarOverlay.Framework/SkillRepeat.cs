namespace StarOverlay.Framework;

/// <summary>
/// The skills the player chose to keep firing while their key is held (plugin "연속 입력"), and the skills pressed lately (the candidates
/// to choose from). The service sends the choice to the in-game agent, which does the repeating.
/// </summary>
public static class SkillRepeat
{
    public const int MinInterval = 50;
    public const int MaxInterval = 500;
    public const int DefaultInterval = 100;

    private static readonly object Gate = new();
    private static int[] _skills = [];
    private static int _interval = DefaultInterval;
    private static bool _active;
    private static readonly Dictionary<int, long> Pressed = [];

    /// <summary>Raised when the chosen skills or the interval changed (also with an empty list: the agent stops at once).</summary>
    public static event Action? Changed;

    /// <summary>The plugin is loaded: the agent watches the skill keys (and reports the skills pressed). Off: it does nothing at all.</summary>
    public static bool Active
    {
        get { lock (Gate) return _active; }
    }

    public static void SetActive(bool active)
    {
        lock (Gate)
        {
            if (_active == active) return;
            _active = active;
        }

        Changed?.Invoke();
    }

    public static IReadOnlyList<int> Skills
    {
        get { lock (Gate) return _skills; }
    }

    public static int IntervalMs
    {
        get { lock (Gate) return _interval; }
    }

    /// <summary>Sets the chosen skills (ids above zero, each once) and the repeat interval (kept between 50 and 500 ms).</summary>
    public static void Set(IEnumerable<int> skillIds, int intervalMs)
    {
        var skills = skillIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();
        var interval = Math.Clamp(intervalMs, MinInterval, MaxInterval);
        lock (Gate)
        {
            if (_skills.SequenceEqual(skills) && _interval == interval) return;
            _skills = skills;
            _interval = interval;
        }

        Changed?.Invoke();
    }

    /// <summary>The agent saw this skill pressed.</summary>
    public static void NotePressed(int skillId, long nowMs)
    {
        if (skillId <= 0) return;
        lock (Gate) Pressed[skillId] = nowMs;
    }

    /// <summary>The skills pressed within the last <paramref name="maxAgeMs"/> ms, the latest first.</summary>
    public static IReadOnlyList<(int Skill, long Seen)> RecentlyPressed(long nowMs, long maxAgeMs)
    {
        lock (Gate)
        {
            return Pressed.Where(pair => nowMs - pair.Value <= maxAgeMs).OrderByDescending(pair => pair.Value).Select(pair => (pair.Key, pair.Value)).ToArray();
        }
    }

    /// <summary>For tests: forget everything.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _skills = [];
            _interval = DefaultInterval;
            _active = false;
            Pressed.Clear();
        }
    }
}
