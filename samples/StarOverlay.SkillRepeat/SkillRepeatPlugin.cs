using System.Globalization;
using StarOverlay.Framework;

namespace StarOverlay.PreInput;

/// <summary>
/// "연속 입력": the window lists the skills the player has used, each with its picture and "켜짐"/"꺼짐"; pressing a row switches the skill on or off,
/// the "✕" of a row deletes it. A skill that is on keeps firing while its key is held (the in-game agent repeats the game's own "use this skill"
/// call; this plugin only keeps the choice). The repeat interval is stepped with the − and + of the row at the top of the same window: the plugin has no
/// settings window.
/// </summary>
public sealed class SkillRepeatPlugin : IPlugin
{
    private const int PollMs = 300;
    private const string IntervalKey = "interval";

    // the intervals the buttons step through (milliseconds)
    private static readonly int[] Intervals = [50, 75, 100, 125, 150, 200, 250, 300, 400, 500];

    private IPluginContext? _context;
    private RepeatModel? _model;
    private Timer? _poll;

    public PluginInfo Info { get; } = new("skill.repeat", "연속 입력", "0.4.8");

    public void Load(IPluginContext context)
    {
        _context = context;
        var library = Path.Combine(context.PluginDirectory, "assets", "library");
        var model = _model = new RepeatModel(key => context.Settings.Get(key), (key, value) => context.Settings.Set(key, value), library);
        context.Ui.AddLabel("skill.repeat.hint", "연속 입력", Hint);
        context.Ui.AddStepper("interval", "반복 간격", () => Interval() + "ms", Step);
        context.Ui.AddIcons("skill.repeat.skills", model.Rows, () => 36, () => true, model.Toggle, model.Remove);
        SkillRepeat.SetActive(true);
        Push();
        _poll = new Timer(_ => Push(), null, PollMs, PollMs); // a skill just used, a switch or a new interval reaches the agent within a third of a second
    }

    public void Unload()
    {
        _poll?.Dispose();
        _poll = null;
        SkillRepeat.Set([], Interval());
        SkillRepeat.SetActive(false); // the agent lets go of the game
    }

    private void Push()
    {
        if (_model == null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _model.Observe(SkillRepeat.RecentlyPressed(now, long.MaxValue).OrderBy(pair => pair.Seen).Select(pair => (pair.Skill, pair.Seen)));
        SkillRepeat.Set(_model.OnIds(), Interval()); // all forms of a switched-on skill
    }

    private int Interval() =>
        int.TryParse(_context?.Settings.Get(IntervalKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, SkillRepeat.MinInterval, SkillRepeat.MaxInterval)
            : SkillRepeat.DefaultInterval;

    /// <summary>The − (-1) or + (+1) of the interval row: the next smaller or larger interval of the list.</summary>
    private void Step(int direction)
    {
        var next = NextInterval(Interval(), direction);
        _context?.Settings.Set(IntervalKey, next.ToString(CultureInfo.InvariantCulture));
        Push();
    }

    internal static int NextInterval(int current, int direction) =>
        direction > 0 ? Intervals.FirstOrDefault(value => value > current, Intervals[^1]) : Intervals.LastOrDefault(value => value < current, Intervals[0]);

    private string Hint() => _model?.Seen().Count > 0 ? "" : "<color=#9ea9bb>스킬을 한 번 사용하면 아래에 나타납니다</color>";
}
