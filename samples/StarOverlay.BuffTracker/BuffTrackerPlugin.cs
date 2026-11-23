using System.Globalization;
using StarOverlay.Framework;

namespace StarOverlay.BuffTracker;

/// <summary>
/// Shows the stacks and the remaining time of buffs the user picked (by uid) as round icons with a ring gauge.
/// The window only shows the icons; adding, changing and deleting is done in the "편집" tab of its settings window.
/// </summary>
public sealed class BuffTrackerPlugin : IPlugin
{
    const int SampleMs = 500;

    TrackerModel? _model;
    Timer? _sampler;

    public PluginInfo Info { get; } = new("buff.tracker", "버프 트래커", "0.4.10");

    public void Load(IPluginContext context)
    {
        var folder = Path.Combine(context.PluginDirectory, "assets", "custom"); // an absolute path: the agent opens the pictures by it
        try
        {
            Directory.CreateDirectory(folder); // the user drops own pictures here
        }
        catch (IOException)
        {
        }

        var model = _model = new TrackerModel(() => context.Settings.Get(TrackerModel.ItemsKey), json => context.Settings.Set(TrackerModel.ItemsKey, json), folder, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            PresentOpacity = () => Number(context, "present-opacity", 100),
            AbsentOpacity = () => Number(context, "absent-opacity", 40),
            TimeOpacity = () => Number(context, "time-opacity", 100),
            StacksOpacity = () => Number(context, "stacks-opacity", 100),
        };
        context.Controls.UseFrameButton(0, "⚙️", opensSettings: true);
        context.Controls.AddText("icon-size", "아이콘 크기", "48", 24, 96);
        context.Controls.AddText("present-opacity", "버프가 있을 때 투명도", "100", 0, 100, step: 10);
        context.Controls.AddText("absent-opacity", "버프가 없을 때 투명도", "40", 0, 100, step: 10);
        context.Controls.AddText("time-opacity", "시간 투명도", "100", 0, 100, step: 10);
        context.Controls.AddText("stacks-opacity", "중첩 투명도", "100", 0, 100, step: 10);
        context.Controls.AddChoice("layout", "배치", ["가로", "세로"], "가로");
        context.Ui.AddIcons("buff.tracker.icons", () => model.Icons(Own()), () => IconSize(context), () => context.Settings.Get("layout") == "세로");
        context.Ui.AddEditor("buff.tracker.items", "추적 중인 버프", model.Rows, model.Candidates, model.Apply);
        _sampler = new Timer(_ => model.Observe(Own()), null, 0, SampleMs);
    }

    public void Unload()
    {
        _sampler?.Dispose();
        _sampler = null;
    }

    static IReadOnlyList<LiveBuff> Own() => LiveCombat.Rows.FirstOrDefault(f => f.Self)?.Buffs ?? [];

    static int Number(IPluginContext context, string key, int fallback) =>
        int.TryParse(context.Settings.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 100) : fallback;

    static int IconSize(IPluginContext context)
    {
        return int.TryParse(context.Settings.Get("icon-size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 24, 96) : 48;
    }
}
