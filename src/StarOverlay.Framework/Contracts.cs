namespace StarOverlay.Framework;

public static class FrameworkBuild
{
    public static string Version { get; } =
        typeof(FrameworkBuild).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

public sealed record PluginInfo(string Id, string Name, string Version);

public interface IPlugin
{
    PluginInfo Info { get; }

    void Load(IPluginContext context);

    void Unload();
}

public interface IPluginContext
{
    string GameDirectory { get; }

    /// <summary>The folder the plugin was loaded from (its assets are next to the dll); empty for a plugin run from memory.</summary>
    string PluginDirectory { get; }

    IPluginLog Log { get; }

    IUiRegistry Ui { get; }

    IPluginSettings Settings { get; }

    ISettingRegistry Controls { get; }
}

public interface IPluginLog
{
    void Info(string message);
}

public sealed record MeterImagineView(string Name, int Tier, string Icon);

public sealed record MeterRowView(
    string Rank,
    string Name,
    int ProfessionId,
    string Value,
    double Bar,
    bool Dead,
    int BarColor,
    IReadOnlyList<MeterImagineView> Imagines,
    int TextColor = 0,
    int IconColor = 0,
    bool Cuisine = false,
    bool Syrup = false,
    string CuisineEffect = "",
    string SyrupEffect = "");

public sealed record DamageSampleView(int Second, long Damage, double PerSecond);

public sealed record SkillDamageView(string Name, long Damage, int Hits, int Crits, int Id = 0, int Luckies = 0);

public sealed record BuffMarkView(int Second, string Name, bool Active, int Id = 0, double Duration = 0, double Cooldown = 0, int Stacks = 0, string Description = "");

public sealed record EncounterPlayerView(
    string Name,
    bool Self,
    long TotalDamage,
    IReadOnlyList<DamageSampleView> Samples,
    IReadOnlyList<SkillDamageView> Skills,
    IReadOnlyList<BuffMarkView> Buffs,
    long CharId = 0,
    string Profession = "",
    long FightPoint = 0,
    bool Cuisine = false,
    bool Syrup = false,
    int ProfessionId = 0,
    string CuisineEffect = "",
    string SyrupEffect = "",
    string Weapon = "",
    int SeasonStrength = 0,
    IReadOnlyList<MeterImagineView>? Imagines = null,
    long FirstHitTicks = 0);

public sealed record EncounterHead(string Id, string Name, DateTime Started, bool Current, string EndReason = "");

public sealed record EncounterView(
    string Id,
    string Name,
    DateTime Started,
    DateTime? Ended,
    bool Current,
    long TotalDamage,
    IReadOnlyList<EncounterPlayerView> Players,
    string EndReason = "");

/// <summary>One round icon. The window counts the remaining time itself from <paramref name="EndsAtMs"/> (this PC's clock), so the plugin gives no remaining seconds.</summary>
public sealed record IconView(
    string Key,
    string Name,
    string Image,
    string Center,
    string Corner,
    long EndsAtMs,
    long TotalMs,
    bool ShowTime,
    string Gauge,
    int Color,
    bool Dim,
    int Alpha = 100,
    int TimeAlpha = 100,
    int StacksAlpha = 100);

public sealed record EditFieldView(string Name, string Label, string Value, bool Toggle);

public sealed record EditRowView(string Key, string Name, string Image, IReadOnlyList<EditFieldView> Fields);

public sealed record EditCandidateView(string Key, string Name, string Image, string Detail);

/// <summary>What the user did in an editor widget: Action = add, delete, up, down or field; Key = the row; Value = the field name (field) or empty.</summary>
public sealed record EditOp(string Action, string Key, string Value);

public interface IUiRegistry
{
    void AddIcons(string id, Func<IReadOnlyList<IconView>> icons, Func<int>? size = null, Func<bool>? vertical = null);

    void AddEditor(
        string id,
        string title,
        Func<IReadOnlyList<EditRowView>> rows,
        Func<IReadOnlyList<EditCandidateView>> candidates,
        Action<EditOp> changed);

    void AddLabel(string id, string title, Func<string> text);

    void AddMeter(string id, Func<IReadOnlyList<MeterRowView>> rows, Func<int>? rowHeight = null, Func<int>? textSize = null);

    void AddList(string id, Func<IReadOnlyList<MeterRowView>> rows, string empty);

    void AddChat(string id, Func<IReadOnlyList<ChatLineView>> lines, string empty, Func<int>? textSize = null, Func<int>? lineGap = null);

    void AddEncounterLog(
        string id,
        Func<IReadOnlyList<EncounterView>> records,
        Func<string> folder,
        Func<string, bool>? delete = null,
        Func<IReadOnlyList<EncounterHead>>? heads = null,
        Func<string, EncounterView?>? open = null);
}

public interface IPluginSettings
{
    string? Get(string key);

    void Set(string key, string value);
}

public interface ISettingRegistry
{
    /// <summary>A text, or a number when <paramref name="maximum"/> is above <paramref name="minimum"/>: edited with - / + that move it by <paramref name="step"/> (0 = a twentieth of the range).</summary>
    void AddText(string key, string label, string defaultValue, int minimum = 0, int maximum = 0, int step = 0);

    void AddToggle(string key, string label, bool defaultValue);

    /// <summary>A setting with a few fixed values, picked from a list in the settings window.</summary>
    void AddChoice(string key, string label, IReadOnlyList<string> choices, string defaultValue);

    void AddToggle(string key, string label, bool defaultValue, Action<bool>? changed);

    void AddButton(string key, string label, Action press);

    void UseFrameButton(int slot, string glyph, bool opensSettings = false, Action? press = null);

    void UsePromptButton(int slot, string glyph);
}
