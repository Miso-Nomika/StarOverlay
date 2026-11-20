using System.Security.Cryptography;
using System.Text;
using StarOverlay.Core.Mods;
using StarOverlay.Framework;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Game.Ingame;

/// <summary>What the service needs from its surroundings (plugin host, plugin folders, windows it may open).</summary>
public interface IIngameHooks
{
    PluginHost Host { get; }

    /// <summary>All plugin folders (enabled and disabled) for the "system" window.</summary>
    IReadOnlyList<PluginFolder> Plugins();

    void SetPluginEnabled(string pluginId, bool enabled);

    void OpenSettings(string pluginId);

    void OpenPrompt();

    /// <summary>Puts text on the Windows clipboard (a chat message the user chose to copy).</summary>
    void CopyText(string text);

    /// <summary>Romaji and Korean translation for a Japanese chat line (empty strings when none).</summary>
    (string Roman, string Translation) Gloss(ChatLineView line);
}

/// <summary>
/// Turns the plugin host's registrations (labels, meters, chats, buttons, settings) into the snapshot the agent draws.
/// The plugin API is unchanged: plugins register into <see cref="PluginHost"/> exactly as before.
/// </summary>
public sealed class UiSnapshotBuilder(IIngameHooks hooks, LayoutStore layouts)
{
    public const string RootWindowId = "root";

    /// <summary>The in-game settings window of a plugin is stored under "&lt;plugin id&gt;:settings".</summary>
    public const string SettingsSuffix = ":settings";
    public const int MaxChatLines = 60;
    const string TranslationModelKey = "translate-model";
    const string ToolDeleteKey = "translate-delete";
    const string ToolDownloadKey = "translate-download";

    public List<WindowDto> Build()
    {
        var host = hooks.Host;
        var windows = new List<WindowDto>();
        windows.Add(Root(windows.Count));

        var ids = new List<string>();
        void Note(string id)
        {
            if (!ids.Contains(id)) ids.Add(id);
        }

        foreach (var panel in host.Panels) Note(panel.PluginId);
        foreach (var meter in host.Meters) Note(meter.PluginId);
        foreach (var chat in host.Chats) Note(chat.PluginId);
        foreach (var icons in host.Icons) Note(icons.PluginId);
        foreach (var editor in host.Editors) Note(editor.PluginId);
        foreach (var button in host.FrameButtons) Note(button.PluginId);
        foreach (var setting in host.Settings) Note(setting.PluginId);

        foreach (var id in ids)
        {
            var window = new WindowDto { Id = id, Title = DisplayName(id) };
            foreach (var panel in host.Panels.Where(p => p.PluginId == id))
            {
                window.Widgets.Add(new WidgetDto { Kind = WidgetKind.Label, Id = panel.Id, Title = panel.Title, Text = Safe(panel.Text) });
                if (window.Title == DisplayName(id) && panel.Title.Length > 0) window.Title = panel.Title;
            }

            foreach (var meter in host.Meters.Where(m => m.PluginId == id))
            {
                var widget = new WidgetDto
                {
                    Kind = meter.Combat ? WidgetKind.Meter : WidgetKind.List,
                    Id = meter.Id,
                    Empty = meter.Empty ?? "",
                    RowHeight = Safe(meter.RowHeight, 0),
                    TextSize = Safe(meter.TextSize, 0),
                };
                if (meter.Combat)
                {
                    widget.Place = LiveCombat.Place.Length == 0 ? "위치 확인 중" : LiveCombat.Place;
                    widget.Elapsed = FormatElapsed(LiveCombat.Started);
                }

                foreach (var row in Safe(meter.Rows)) widget.Rows.Add(ToRow(row));
                window.Widgets.Add(widget);
            }

            foreach (var chat in host.Chats.Where(c => c.PluginId == id))
            {
                var widget = new WidgetDto
                {
                    Kind = WidgetKind.Chat,
                    Id = chat.Id,
                    Empty = chat.Empty,
                    TextSize = Safe(chat.TextSize, 0),
                    LineGap = Safe(chat.LineGap, 0),
                };
                var world = LiveChat.WorldChannel;
                widget.WorldChannel = world;
                var lines = VisibleChat(Safe(chat.Lines), world);
                foreach (var line in lines.Skip(Math.Max(0, lines.Count - MaxChatLines)))
                {
                    var (roman, translation) = SafeGloss(line);
                    widget.Lines.Add(ToChatLine(line, roman, translation));
                }

                window.Widgets.Add(widget);
            }

            foreach (var icons in host.Icons.Where(i => i.PluginId == id))
            {
                var widget = new WidgetDto { Kind = WidgetKind.Icons, Id = icons.Id, IconSize = Safe(icons.Size, 0), IconsVertical = SafeFlag(icons.Vertical) };
                foreach (var icon in Safe(icons.Icons))
                    widget.Icons.Add(new IconDto
                    {
                        Key = icon.Key,
                        Name = icon.Name,
                        Image = icon.Image,
                        Text = icon.Center,
                        Corner = icon.Corner,
                        EndsAtMs = icon.EndsAtMs,
                        TotalMs = icon.TotalMs,
                        ShowTime = icon.ShowTime,
                        Gauge = icon.Gauge,
                        Color = icon.Color,
                        Dim = icon.Dim,
                        Alpha = icon.Alpha,
                        TimeAlpha = icon.TimeAlpha,
                        StacksAlpha = icon.StacksAlpha,
                    });
                window.Widgets.Add(widget);
            }

            foreach (var editor in host.Editors.Where(e => e.PluginId == id))
            {
                var widget = new WidgetDto { Kind = WidgetKind.Editor, Id = editor.Id, Title = editor.Title };
                foreach (var row in Safe(editor.Rows))
                    widget.EditRows.Add(new EditRowDto
                    {
                        Key = row.Key,
                        Name = row.Name,
                        Image = row.Image,
                        Fields = row.Fields.Select(f => new EditFieldDto { Name = f.Name, Label = f.Label, Value = f.Value, Toggle = f.Toggle }).ToList(),
                    });
                foreach (var candidate in Safe(editor.Candidates))
                    widget.Candidates.Add(new CandidateDto { Key = candidate.Key, Name = candidate.Name, Image = candidate.Image, Detail = candidate.Detail });
                window.Widgets.Add(widget);
            }

            foreach (var button in host.FrameButtons.Where(b => b.PluginId == id).OrderBy(b => b.Slot))
                window.FrameButtons.Add(new FrameButtonDto { Slot = button.Slot, Glyph = button.Glyph, OpensSettings = button.OpensSettings, OpensPrompt = button.OpensPrompt });

            // the translation tool is one row: its status, the download button and the delete button belong together
            foreach (var setting in host.Settings.Where(s => s.PluginId == id && s.Key != ToolDeleteKey))
                window.Settings.Add(ToSetting(setting));

            window.HasRecords = host.EncounterLogs.Any(log => log.PluginId == id);
            window.SettingsLayout = layouts.Get(id + SettingsSuffix, window.HasRecords
                ? new LayoutDto { X = 0.17, Y = 0.07, W = 0.66, H = 0.85 } // fight records need room for the table, graph, skills and buffs
                : new LayoutDto { X = 0.3, Y = 0.12, W = 0.4, H = 0.72 });

            window.Layout = layouts.Get(id, windows.Count, DefaultWidth(window), DefaultHeight(window));
            // a window that only shows icons (the editor widget lives in its settings window) is drawn bare, with only a small handle, once its
            // top tab is hidden; while it is on, it keeps its top tab so it can be found when no icon is there
            window.Bare = window.Layout.HeaderHidden && window.Widgets.Any(w => w.Kind == WidgetKind.Icons) && window.Widgets.All(w => w.Kind is WidgetKind.Icons or WidgetKind.Editor);
            windows.Add(window);
        }

        return windows;
    }

    static string FormatElapsed(DateTime started)
    {
        var span = DateTime.UtcNow - started;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    /// <summary>Korean names of the built-in plugins for window titles; other plugins show their id.</summary>
    public static string DisplayName(string pluginId) => pluginId switch
    {
        "party.join" => "파티가입",
        "dps.meter" => "미터기",
        "chat" => "채팅",
        "buff.tracker" => "버프 트래커",
        _ => pluginId,
    };

    WindowDto Root(int index)
    {
        var window = new WindowDto { Id = RootWindowId, Title = "시스템" };
        foreach (var plugin in hooks.Plugins().OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            window.Settings.Add(new SettingDto { Key = plugin.Id, Label = DisplayName(plugin.Id), Kind = "plugin", Value = !plugin.Enabled ? "off" : layouts.Get(plugin.Id, new LayoutDto()).HeaderHidden ? "hidden" : "on" });
        window.Layout = layouts.Get(RootWindowId, index, 0.2, 0.04 + 0.032 * Math.Max(1, window.Settings.Count));
        return window;
    }

    // the translator is optional; its failure must not cost the whole chat window
    (string, string) SafeGloss(ChatLineView line)
    {
        try
        {
            var (roman, translation) = hooks.Gloss(line);
            return (roman ?? "", translation ?? "");
        }
        catch (Exception)
        {
            return ("", "");
        }
    }

    static double DefaultWidth(WindowDto window) =>
        window.Widgets.Any(w => w.Kind == WidgetKind.Chat) ? 0.26 : window.Widgets.Any(w => w.Kind is WidgetKind.Meter or WidgetKind.List) ? 0.22 : 0.18;

    static double DefaultHeight(WindowDto window) =>
        window.Widgets.Any(w => w.Kind == WidgetKind.Chat) ? 0.34 : window.Widgets.Any(w => w.Kind is WidgetKind.Meter or WidgetKind.List) ? 0.3 : 0.12;

    static RowDto ToRow(MeterRowView row) => new()
    {
        Rank = row.Rank,
        Name = row.Name,
        Value = row.Value,
        Bar = Math.Clamp(row.Bar, 0, 100) / 100.0, // the plugin gives a percent of the top row; the window draws a 0..1 fill
        Dead = row.Dead,
        BarColor = row.BarColor,
        TextColor = row.TextColor,
        IconColor = row.IconColor,
        ProfessionId = row.ProfessionId,
        Cuisine = row.Cuisine,
        Syrup = row.Syrup,
        CuisineEffect = row.CuisineEffect ?? "",
        SyrupEffect = row.SyrupEffect ?? "",
        Imagines = row.Imagines.Select(i => new ImagineDto { Name = i.Name, Tier = i.Tier, Icon = i.Icon }).ToList(),
    };

    static SettingDto ToSetting(PluginSetting setting)
    {
        var dto = new SettingDto { Key = setting.Key, Label = setting.Label, Kind = setting.Kind, Value = SettingValue(setting), Default = setting.DefaultValue };
        if (setting.Key == ToolDownloadKey)
        {
            dto.Kind = "tool";
            dto.Label = "번역 도구";
            dto.Value = TranslationModel.StatusText(); // live: the download progress
            dto.Default = ToolDeleteKey; // the key of the delete button of the row
            dto.CanDelete = TranslationModel.Models().Count > 0 && !TranslationModel.Downloading;
            dto.CanDownload = TranslationModel.Downloading || TranslationModel.Models().Count == 0 || TranslationModel.LlamaPath(false) is null;
            return dto;
        }

        if (setting.Kind != "text") return dto;
        if (setting.Choices is { Count: > 0 })
        {
            dto.Choices = setting.Choices.ToList();
            if (!dto.Choices.Contains(dto.Value)) dto.Value = setting.DefaultValue;
        }
        else if (setting.Key == "translate-device")
        {
            dto.Choices = [TranslationDevice.Cpu, TranslationDevice.Gpu];
        }
        else if (setting.Key == TranslationModelKey)
        {
            // the model files found on this PC; the one that will run is shown ("없음" while there is none, nothing to pick then)
            dto.Choices = TranslationModel.Models().ToList();
            dto.Value = dto.Choices.Count == 0 ? "없음" : TranslationModel.EffectiveName();
            dto.Hint = TranslationModel.GpuHintForModel();
        }
        else if (setting.Maximum > setting.Minimum)
        {
            dto.Min = setting.Minimum;
            dto.Max = setting.Maximum;
            dto.Step = setting.Step;
        }
        else if (setting.Key == "translate-prompt" || dto.Value.Length > 60)
        {
            dto.Multiline = true;
            if (dto.Value.Length > 4000) dto.Value = dto.Value[..4000] + "…";
        }

        return dto;
    }

    static string SettingValue(PluginSetting setting)
    {
        try
        {
            return setting.Kind == "button" ? "" : setting.Read() ?? setting.DefaultValue;
        }
        catch (Exception)
        {
            return setting.DefaultValue;
        }
    }

    /// <summary>The lines the chat window shows: the world lines of another world channel than the one the game is on are left out.</summary>
    public static IReadOnlyList<ChatLineView> VisibleChat(IReadOnlyList<ChatLineView> lines, int worldChannel) =>
        worldChannel <= 0
            ? lines
            : lines.Where(line => !(line.Channel == LiveChat.WorldChannelName && line.World > 0 && line.World != worldChannel)).ToList();

    static string IconName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    public static ChatLineDto ToChatLine(ChatLineView line, string roman, string translation) => new()
    {
        Id = LineId(line),
        Channel = line.Channel,
        Name = line.Name,
        Text = ChatItems.Expand(line),
        Roman = roman,
        Translation = translation,
        World = line.World,
        Time = line.At.ToString("HH:mm"),
        Links = (line.Links ?? []).Select(link =>
        {
            var item = ItemBook.Find(link.ItemId);
            return new ChatLinkDto
            {
                ItemId = link.ItemId,
                Name = ItemBook.NameOf(link.ItemId),
                Quality = item?.Quality ?? 0,
                Icon = IconName(item?.Icon),
                Token = "[" + ItemBook.NameOf(link.ItemId) + "]",
            };
        }).ToList(),
    };

    /// <summary>A stable id for a chat line so the agent can tell new lines from old ones.</summary>
    public static long LineId(ChatLineView line)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(line.At.Ticks + "|" + line.Channel + "|" + line.Name + "|" + line.Text));
        return BitConverter.ToInt64(bytes, 0) & long.MaxValue;
    }

    // plugin callbacks can throw; one broken plugin must not stop the whole snapshot
    static string Safe(Func<string> text)
    {
        try
        {
            return text() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    static bool SafeFlag(Func<bool>? value)
    {
        try
        {
            return value?.Invoke() ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static int Safe(Func<int>? value, int fallback)
    {
        try
        {
            return value?.Invoke() ?? fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    static IReadOnlyList<T> Safe<T>(Func<IReadOnlyList<T>> items)
    {
        try
        {
            return items() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
