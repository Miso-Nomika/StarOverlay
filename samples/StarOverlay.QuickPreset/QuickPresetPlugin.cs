using StarOverlay.Framework;

namespace StarOverlay.PresetSwitch;

/// <summary>
/// "빠른 프리셋": the window lists the game's presets in two columns side by side, "심상" (the season ability sets) on the left and "캐릭터" (the character
/// plans) on the right, with the current one marked; pressing a row switches to that preset at once, like the game's own preset screens but without opening them. Presets that are locked or have
/// nothing saved are listed but cannot be pressed. The in-game agent reads the presets and makes the switch (it calls the game's own functions); this plugin
/// only shows the list (with a line telling whether the character is in combat, when the game refuses a switch) and passes the press on. While the plugin is off the agent does nothing.
/// </summary>
public sealed class QuickPresetPlugin : IPlugin
{
    private const string Mind = "mind";
    private const string Char = "char";

    public PluginInfo Info { get; } = new("quick.preset", "빠른 프리셋", "0.4.10");

    public void Load(IPluginContext context)
    {
        context.Controls.UseFrameButton(0, "\U0001F504", press: QuickPreset.RequestRefresh); // read the presets of the game again
        context.Controls.UseFrameButton(1, "\u2699\uFE0F", opensSettings: true);
        context.Controls.AddText("title-size", "심상·캐릭터 글자 크기", "14", 8, 30);
        context.Controls.AddText("row-gap", "프리셋 줄 간격", "6", 0, 24);
        context.Ui.AddLabel("quick.preset.hint", "빠른 프리셋", Hint);
        context.Ui.AddIcons("quick.preset.list", () => Rows(Mind).Concat(Rows(Char)).ToList(), () => 28, () => true, Press, // one list, two columns by group
            gap: () => Number(context, "row-gap", 6, 0, 24), headerSize: () => Number(context, "title-size", 14, 8, 30),
            badge: () => QuickPreset.Items.Count > 0 ? CombatBadge(LiveCombat.SelfInCombat) : "");
        QuickPreset.SetActive(true);
    }

    public void Unload() => QuickPreset.SetActive(false); // the agent lets go of the game

    private static string Hint() => QuickPreset.Items.Count > 0 ? "" : "<color=#9ea9bb>게임에 접속하면 프리셋이 나타납니다</color>";

    private static int Number(IPluginContext context, string key, int fallback, int minimum, int maximum) =>
        int.TryParse(context.Settings.Get(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, minimum, maximum) : fallback;

    /// <summary>At the right end of the title row: a green dot with "비전투", or in combat a red dot with "전투 중" (presets cannot be switched in combat).</summary>
    internal static string CombatBadge(bool inCombat) =>
        inCombat ? "<color=#ff8a8a>● 전투 중</color>" : "<color=#7be39a>● 비전투</color>";


    private static void Press(string key) => QuickPreset.RequestSwitch(key);

    internal static IReadOnlyList<IconView> Rows(string kind)
    {
        var failure = QuickPreset.RecentFailure(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return QuickPreset.Items
            .Where(item => item.Kind == kind)
            .OrderBy(item => item.Id)
            .Select(item => new IconView(item.Key, Quiet(item), Picture(item), "", "", 0, 0, false, "none", 0, item.State != "current", Alpha: item.State is "locked" or "empty" ? 35 : 100, State: StateText(item, failure), Group: item.Kind == Mind ? "<color=#ffd98a>심상</color>" : "<color=#ffd98a>캐릭터</color>"))
            .ToList();
    }

    /// <summary>The picture of a row: the class of a character plan ("profession:&lt;id&gt;", the window asks the game for its class picture), or the main node of a season ability set (a sprite of the game's own files).</summary>
    internal static string Picture(PresetItem item) =>
        item.Icon.StartsWith("profession:", StringComparison.Ordinal) ? item.Icon : item.Icon.Length > 0 ? "game:" + item.Icon : "";

    /// <summary>The name of a row: grayed when the preset is locked or has nothing saved (such a row cannot be pressed).</summary>
    internal static string Quiet(PresetItem item) => item.State is "locked" or "empty" ? "<color=#7d8696>" + item.Name + "</color>" : item.Name;

    /// <summary>The text at the end of a row: only why the last switch failed (three seconds). The current preset has the bar at the left of its row, locked and empty ones are grayed.</summary>
    internal static string StateText(PresetItem item, (string Key, string Why) failure) =>
        item.Key == failure.Key && failure.Key.Length > 0
            ? "<color=#ff8a8a>" + (failure.Why == "changed" ? "미저장" : "실패") + "</color>"
            : "";
}
