namespace StarOverlay.Ingame.Protocol;

/// <summary>
/// Messages between the Host service (pipe server) and the in-game agent (pipe client). One JSON object per line (UTF-8).
/// The service owns all state; the agent only draws what it is told and reports what the user pressed.
/// </summary>
public static class MessageType
{
    /// <summary>Both directions. Service: protocol version. Agent: version, game hash, Unity version.</summary>
    public const string Hello = "hello";

    /// <summary>Service to agent: full UI snapshot.</summary>
    public const string Ui = "ui";

    /// <summary>Service to agent: where plugin assets live.</summary>
    public const string Assets = "assets";

    /// <summary>Agent to service: the user pressed, toggled, moved ... something.</summary>
    public const string Cmd = "cmd";

    /// <summary>Agent to service: something failed in the game (agent turned a part off).</summary>
    public const string Err = "err";

    /// <summary>Agent to service: one game network message seen inside the game (T35).</summary>
    public const string Msg = "msg";

    /// <summary>Service to agent: a chat line gained romaji or a translation (T23).</summary>
    public const string Gloss = "gloss";

    /// <summary>Service to agent: the fight record list of one window (answer to the records command).</summary>
    public const string Records = "records";

    /// <summary>Service to agent: one opened fight record (answer to the record-open command).</summary>
    public const string Record = "record";

    /// <summary>Service to agent: save the game's tables as JSON into the folder in <c>Root</c> (when they are not there for this game version yet).</summary>
    public const string Tables = "tables";

    /// <summary>Service to agent: the skills to keep firing while their key is held and the repeat interval (an empty list stops the repeating).</summary>
    public const string Repeat = "repeat";

    /// <summary>Service to agent: the quick preset plugin is on or off, and the preset the player asked to switch to (or a refresh).</summary>
    public const string Preset = "preset";

    /// <summary>Service to agent: the party join plugin is on or off, whose master scores the agent is to read, and the application the player accepted or rejected.</summary>
    public const string Party = "party";


    /// <summary>Agent to service: the tables are saved (<c>Written</c> true: new files, read them again) or could not be (<c>Error</c>).</summary>
    public const string TablesDone = "tables-done";
}

public static class CommandAction
{
    public const string Press = "press";        // key = setting key or "frame:<slot>"
    public const string Toggle = "toggle";      // key = setting key, value = on, hidden (header hidden) or off (true/false also work)
    public const string Move = "move";          // x, y
    public const string Resize = "resize";      // w, h
    public const string Pin = "pin";            // value = true/false
    public const string Opacity = "opacity";    // value = 20..100
    public const string Collapse = "collapse";  // value = true/false
    public const string Plugin = "plugin";      // window "root": key = plugin id, value = true/false
    public const string Settings = "settings";  // open the service's settings / record window for the window's plugin
    public const string SkillPressed = "skill-pressed"; // agent: key = the game skill id that was just pressed (candidates of the "연속 입력" plugin)
    public const string Prompt = "prompt";      // open the translation prompt window of the service
    public const string Set = "set";            // key, value: numbers (clamped to the setting range), choices, or the default value
    public const string Records = "records";    // ask for the window's fight record list
    public const string RecordOpen = "record-open";     // key = record id
    public const string RecordDelete = "record-delete"; // key = record id, several ids separated by commas, or "oldest:N"
    public const string Copy = "copy";                  // chat: value = text for the clipboard
    public const string Channels = "channels";          // chat window: value = bit mask of the shown channels
    public const string Edit = "edit";                  // editor widget: window = plugin id, key = "<widget id>:<row key>", value = add, delete, up, down, field:<name> or set:<field>=<value>
    public const string Presets = "presets";            // agent: window "quick.preset", value = the presets read from the game, one per line: "kind|id|name|state" (state: current, ready, empty, locked)
    public const string PresetFailed = "preset-failed"; // agent: window "quick.preset", key = "kind:id" that could not be switched to, value = changed (unsaved changes in the game) or failed
    public const string PartyScores = "party-scores";   // agent: window "party.join", value = master scores read from the game, one per line: "charId|score|shown" (shown 0 = the player keeps it private)
    public const string PartyDealt = "party-dealt";     // agent: window "party.join", key = application id, value = ok (handed to the game) or failed
    public const string SettingsShown = "settings-shown"; // value = true/false: a settings window of this plugin is open in the game (the service may then read the graphics card)
}

public sealed class Envelope
{
    public string T { get; set; } = "";

    public int V { get; set; }

    public string? Agent { get; set; }

    public string? GameHash { get; set; }

    public string? Unity { get; set; }

    public string? Error { get; set; }

    public string? Root { get; set; }

    /// <summary>With <see cref="MessageType.TablesDone"/>: true when new table files were written.</summary>
    public bool? Written { get; set; }

    public int Rev { get; set; }

    public List<WindowDto>? Windows { get; set; }

    public CommandDto? Cmd { get; set; }

    public NetMessageDto? Msg { get; set; }

    public GlossDto? Gloss { get; set; }

    /// <summary>Which window a records / record answer belongs to.</summary>
    public string? Window { get; set; }

    public List<RecordHeadDto>? Records { get; set; }

    public RecordDto? Record { get; set; }

    /// <summary>With <see cref="MessageType.Repeat"/>.</summary>
    public RepeatDto? Repeat { get; set; }

    /// <summary>With <see cref="MessageType.Preset"/>.</summary>
    public PresetDto? Preset { get; set; }

    /// <summary>With <see cref="MessageType.Party"/>.</summary>
    public PartyDto? Party { get; set; }

}

/// <summary>The quick preset plugin: <see cref="Active"/>, and the newest request ("kind:id" or "refresh") with a number that grows on every request.</summary>
public sealed class PresetDto
{
    public bool Active { get; set; }

    public string Request { get; set; } = "";

    public int Serial { get; set; }

    /// <summary>The service has presets to show. False while it has none (it was just started, or the plugin was switched off and on): the agent then reads and sends them again.</summary>
    public bool HaveItems { get; set; }
}

/// <summary>The party join plugin: <see cref="Active"/>, the characters whose master score is still unknown (<see cref="Need"/>), and the newest accept or reject ("accept:id" or "reject:id") with a number that grows on every request.</summary>
public sealed class PartyDto
{
    public bool Active { get; set; }

    public List<long> Need { get; set; } = [];

    public string Request { get; set; } = "";

    public int Serial { get; set; }
}

/// <summary>The skills to repeat while held (game skill ids) and the interval between two repeats in milliseconds.</summary>
public sealed class RepeatDto
{
    public const int DefaultInterval = 100;

    /// <summary>The plugin is loaded: the agent watches the skill keys. False: it does nothing.</summary>
    public bool Active { get; set; }

    public List<int> Skills { get; set; } = [];

    public int IntervalMs { get; set; } = DefaultInterval;
}

/// <summary>Normalized (0..1) rectangle on the 1920x1080 reference canvas, plus window state.</summary>
public sealed class LayoutDto
{
    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; }

    public double H { get; set; }

    public int Opacity { get; set; } = 90;

    public bool Pinned { get; set; }

    public bool Collapsed { get; set; }

    /// <summary>The title and the buttons of the window are hidden (only its content is drawn, and clicks go through to the game).</summary>
    public bool HeaderHidden { get; set; }

    /// <summary>The window is not drawn at all (state "숨김" of the system window): the plugin stays loaded and keeps working.</summary>
    public bool WindowHidden { get; set; }

    /// <summary>Chat windows: which channels are shown, one bit each (local, world, guild, whisper, party, newbie). All on = 63.</summary>
    public int Channels { get; set; } = 63;

    public LayoutDto Copy() => new() { X = X, Y = Y, W = W, H = H, Opacity = Opacity, Pinned = Pinned, Collapsed = Collapsed, HeaderHidden = HeaderHidden, WindowHidden = WindowHidden, Channels = Channels };
}

public sealed class WindowDto
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public LayoutDto Layout { get; set; } = new();

    public List<WidgetDto> Widgets { get; set; } = [];

    public List<FrameButtonDto> FrameButtons { get; set; } = [];

    public List<SettingDto> Settings { get; set; } = [];

    /// <summary>The plugin keeps fight records (the in-game settings window then has a records tab).</summary>
    public bool HasRecords { get; set; }

    /// <summary>Drawn without header, background and buttons: only the icons and one small corner handle (drag = move, click = settings).</summary>
    public bool Bare { get; set; }

    /// <summary>Where this plugin's in-game settings window goes (stored like any window layout).</summary>
    public LayoutDto SettingsLayout { get; set; } = new();
}

public static class WidgetKind
{
    public const string Label = "label";
    public const string Meter = "meter";
    public const string List = "list";
    public const string Chat = "chat";
    public const string Icons = "icons";
    public const string Editor = "editor";
    public const string Stepper = "stepper"; // Title = the label, Text = the value; the buttons send Press "step:<id>:-1" / "step:<id>:1"
}

public sealed class WidgetDto
{
    public string Kind { get; set; } = "";

    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Text { get; set; } = "";

    public string Empty { get; set; } = "";

    public int RowHeight { get; set; }

    public int TextSize { get; set; }

    public int LineGap { get; set; }

    /// <summary>Footer of a combat meter: the current place (map, channel, coordinates) and the fight clock.</summary>
    public string Place { get; set; } = "";

    public string Elapsed { get; set; } = "";

    public List<RowDto> Rows { get; set; } = [];

    public List<ChatLineDto> Lines { get; set; } = [];

    /// <summary>The world chat channel the game is on (0 = not known yet); only its world lines are sent.</summary>
    public int WorldChannel { get; set; }

    /// <summary>Icons widget: the diameter of one icon in reference pixels (0 = default).</summary>
    public int IconSize { get; set; }

    /// <summary>Icons widget: the icons stand in a column (one per row) instead of a row.</summary>
    public bool IconsVertical { get; set; }

    /// <summary>Icons widget: drawn as a pressable list (name and on/off at the end of each row); a press sends "icon:&lt;key&gt;".</summary>
    public bool IconsClickable { get; set; }

    /// <summary>Icons widget: every row has a delete button; a press sends "icon-remove:&lt;key&gt;".</summary>
    public bool IconsRemovable { get; set; }

    /// <summary>Icons widget (pressable list): the space between two rows in reference pixels (-1 = default).</summary>
    public int IconGap { get; set; } = -1;

    /// <summary>Icons widget (list in columns): the text size of the column titles (0 = default).</summary>
    public int HeaderSize { get; set; }

    /// <summary>Icons widget (list in columns): a short text at the right end of the title row.</summary>
    public string Badge { get; set; } = "";

    public List<IconDto> Icons { get; set; } = [];

    public List<EditRowDto> EditRows { get; set; } = [];

    public List<CandidateDto> Candidates { get; set; } = [];
}

/// <summary>One round icon. No remaining seconds are sent: the agent counts from EndsAtMs (the PC clock of the service) every frame, so a running buff does not change the snapshot.</summary>
public sealed class IconDto
{
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Image { get; set; } = "";

    /// <summary>Text in the middle of the icon (shown when no time is counted).</summary>
    public string Text { get; set; } = "";

    /// <summary>Text in the lower right corner (the stacks).</summary>
    public string Corner { get; set; } = "";

    public long EndsAtMs { get; set; }

    public long TotalMs { get; set; }

    public bool ShowTime { get; set; }

    /// <summary>none, ring (empties with the time) or ring-reverse (fills with the time).</summary>
    public string Gauge { get; set; } = "none";

    public int Color { get; set; }

    /// <summary>A second, outer ring (the other time of a combined icon): when it ends and its whole length, like <see cref="EndsAtMs"/>; <see cref="OuterColor"/> is 0 when the icon has no outer ring.</summary>
    public long OuterEndsAtMs { get; set; }

    public long OuterTotalMs { get; set; }

    public int OuterColor { get; set; }

    public bool Dim { get; set; }

    /// <summary>How opaque the whole icon is (percent); the time and the stacks have their own.</summary>
    public int Alpha { get; set; } = 100;

    public int TimeAlpha { get; set; } = 100;

    public int StacksAlpha { get; set; } = 100;

    /// <summary>The text at the end of a row of a pressable list (rich text); empty: the usual "켜짐"/"꺼짐" by <see cref="Dim"/>.</summary>
    public string State { get; set; } = "";

    /// <summary>The column of a pressable list the row stands in (rich text, shown above the column); empty: one column.</summary>
    public string Group { get; set; } = "";
}

public sealed class EditFieldDto
{
    public string Name { get; set; } = "";

    public string Label { get; set; } = "";

    public string Value { get; set; } = "";

    public bool Toggle { get; set; }
}

public sealed class EditRowDto
{
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Image { get; set; } = "";

    public List<EditFieldDto> Fields { get; set; } = [];
}

public sealed class CandidateDto
{
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string Image { get; set; } = "";

    public string Detail { get; set; } = "";
}

public sealed class RowDto
{
    public string Rank { get; set; } = "";

    public string Name { get; set; } = "";

    public string Value { get; set; } = "";

    public double Bar { get; set; }

    public bool Dead { get; set; }

    public int BarColor { get; set; }

    public int TextColor { get; set; }

    public int IconColor { get; set; }

    public int ProfessionId { get; set; }

    public bool Cuisine { get; set; }

    public bool Syrup { get; set; }

    /// <summary>What the cuisine / syrup buff does (shown when the cursor rests on its icon).</summary>
    public string CuisineEffect { get; set; } = "";

    public string SyrupEffect { get; set; } = "";

    public List<ImagineDto> Imagines { get; set; } = [];

    /// <summary>Buttons at the right end of the row; a press sends "row:&lt;key&gt;".</summary>
    public List<RowActionDto> Actions { get; set; } = [];
}

public sealed class RowActionDto
{
    public string Label { get; set; } = "";

    public string Key { get; set; } = "";

    public int Color { get; set; }

    /// <summary>"check" or "cross": drawn on the button instead of the label; empty = the label is the button's text.</summary>
    public string Icon { get; set; } = "";
}

public sealed class ImagineDto
{
    public string Name { get; set; } = "";

    public int Tier { get; set; }

    public string Icon { get; set; } = "";
}

public sealed class ChatLineDto
{
    public long Id { get; set; }

    public string Channel { get; set; } = "";

    public string Name { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>Romaji line for Japanese text (empty when none).</summary>
    public string Roman { get; set; } = "";

    /// <summary>Korean translation (empty when none yet).</summary>
    public string Translation { get; set; } = "";

    /// <summary>The world channel number of a world line (0 = not a world line or not known).</summary>
    public int World { get; set; }

    /// <summary>When it was said, "HH:mm" (local time).</summary>
    public string Time { get; set; } = "";

    /// <summary>Items linked in this message (clicking one opens its card).</summary>
    public List<ChatLinkDto> Links { get; set; } = [];

    /// <summary>Said by the player themself: the window shows these lines on the right.</summary>
    public bool Self { get; set; }

    /// <summary>The sender's character id (0 = not known): the game's own card of that character is opened with it.</summary>
    public long CharId { get; set; }

    /// <summary>The game's id of the message inside its channel (0 = not known): the game's own data of the message (an item link's full data) is found with it.</summary>
    public long MsgId { get; set; }
}

public sealed class ChatLinkDto
{
    public int ItemId { get; set; }

    public string Name { get; set; } = "";

    public int Quality { get; set; }

    /// <summary>The text of this link inside <see cref="ChatLineDto.Text"/> ("[이름]"): the window turns it into the clickable part.</summary>
    public string Token { get; set; } = "";
}

public sealed class FrameButtonDto
{
    public int Slot { get; set; }

    public string Glyph { get; set; } = "";

    public bool OpensSettings { get; set; }

    public bool OpensPrompt { get; set; }
}

public sealed class SettingDto
{
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>toggle, button, or text (shown read-only in game, edited in the service's settings window).</summary>
    public string Kind { get; set; } = "";

    public string Value { get; set; } = "";

    /// <summary>Range for number settings (Max &gt; Min), edited with - / + in game.</summary>
    public int Min { get; set; }

    public int Max { get; set; }

    /// <summary>How far one press of - / + moves a number setting (0 = a twentieth of the range).</summary>
    public int Step { get; set; }

    /// <summary>Fixed choices (for example cpu / gpu); the in-game button cycles through them.</summary>
    public List<string> Choices { get; set; } = [];

    /// <summary>Long free text (shown as a short preview; edited outside the game or reset to the default).</summary>
    public bool Multiline { get; set; }

    public string Default { get; set; } = "";

    /// <summary>For the translation tool row (kind "tool"): a downloaded model exists that the delete button can remove.</summary>
    public bool CanDelete { get; set; }

    /// <summary>For the translation tool row: something is missing (no model, or no translation program) or a download is running, so the download button is shown.</summary>
    public bool CanDownload { get; set; }

    /// <summary>A grey line under the setting (for the translation model: the graphics memory it wants).</summary>
    public string Hint { get; set; } = "";
}

public sealed class CommandDto
{
    public string Window { get; set; } = "";

    public string Action { get; set; } = "";

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double W { get; set; }

    public double H { get; set; }
}

/// <summary>A decoded game message the agent saw in the game's own network layer (T35). Payload is base64.</summary>
public sealed class NetMessageDto
{
    public ulong Service { get; set; }

    public uint Method { get; set; }

    public bool Outbound { get; set; }

    public string Payload { get; set; } = "";
}

public sealed class GlossDto
{
    public long Id { get; set; }

    public string Channel { get; set; } = "";

    public string Name { get; set; } = "";

    public string Text { get; set; } = "";

    public string Roman { get; set; } = "";

    public string Translation { get; set; } = "";
}

public sealed class RecordHeadDto
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Started { get; set; } = "";

    public bool Current { get; set; }

    /// <summary>Why the fight ended ("보스 처치", "직접 초기화"...), empty while it is going on.</summary>
    public string EndReason { get; set; } = "";
}

public sealed class RecordDto
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Started { get; set; } = "";

    public string EndReason { get; set; } = "";

    public long TotalDamage { get; set; }

    public double Seconds { get; set; }

    public List<RecordPlayerDto> Players { get; set; } = [];

    /// <summary>What each buff does, by buff name (sent once per record; the buff events only carry the name).</summary>
    public List<BuffInfoDto> BuffInfo { get; set; } = [];
}

public sealed class BuffInfoDto
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";
}

public sealed class RecordSampleDto
{
    public int Second { get; set; }

    public double PerSecond { get; set; }
}

public sealed class RecordBuffDto
{
    public int Second { get; set; }

    public string Name { get; set; } = "";

    public bool Active { get; set; }

    public int Id { get; set; }

    public double Duration { get; set; }

    public double Cooldown { get; set; }

    public int Stacks { get; set; }
}

public sealed class RecordPlayerDto
{
    public long CharId { get; set; }

    /// <summary>The game's id of the message inside its channel (0 = not known): the game's own data of the message (an item link's full data) is found with it.</summary>
    public long MsgId { get; set; }

    public string Name { get; set; } = "";

    public bool Self { get; set; }

    public string Profession { get; set; } = "";

    public long FightPoint { get; set; }

    public long Total { get; set; }

    public double PerSecond { get; set; }

    public double Share { get; set; }

    /// <summary>Bar length against the first place (1 = the one who did the most), like the meter rows.</summary>
    public double Bar { get; set; }

    /// <summary>Bar colour of the job (0xRRGGBB), the same as in the meter.</summary>
    public int BarColor { get; set; }

    /// <summary>Season strength shown after the power (power+season), like the meter.</summary>
    public int SeasonStrength { get; set; }

    /// <summary>True for the one who attacked first in that fight (marked with a flag).</summary>
    public bool FirstAttacker { get; set; }

    /// <summary>The imagines worn in that fight (at most two), shown like in the meter.</summary>
    public List<ImagineDto> Imagines { get; set; } = [];

    /// <summary>Name colour like the meter (0xRRGGBB): self green, Far Sea light blue, the Far Sea light red; 0 = default.</summary>
    public int TextColor { get; set; }

    public bool Cuisine { get; set; }

    public bool Syrup { get; set; }

    /// <summary>What the cuisine / syrup buff of that fight did (shown when the cursor rests on its icon).</summary>
    public string CuisineEffect { get; set; } = "";

    public string SyrupEffect { get; set; } = "";

    /// <summary>Damage per second over the fight, thinned out to at most RecordMapper.MaxSamples points.</summary>
    public List<RecordSampleDto> Samples { get; set; } = [];

    /// <summary>Every buff start and end of this player, in the order they happened.</summary>
    public List<RecordBuffDto> Buffs { get; set; } = [];

    public List<RecordSkillDto> Skills { get; set; } = [];
}

public sealed class RecordSkillDto
{
    public string Name { get; set; } = "";

    public long Damage { get; set; }

    public int Hits { get; set; }

    public int Crits { get; set; }

    public int Luckies { get; set; }

    public int Id { get; set; }
}
