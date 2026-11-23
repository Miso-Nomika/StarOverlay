using System.Reflection;
using System.Text.Json;

namespace StarOverlay.Framework;

public sealed record PluginPanel(string PluginId, string Id, string Title, Func<string> Text);

/// <summary>A row "label  [-] value [+]" inside the window of a plugin: the player steps a value down or up (<paramref name="Step"/> gets -1 or +1).</summary>
public sealed record PluginStepper(string PluginId, string Id, string Label, Func<string> Value, Action<int> Step);

public sealed record PluginMeter(string PluginId, string Id, Func<IReadOnlyList<MeterRowView>> Rows, Func<int>? RowHeight = null, string? Empty = null, bool Combat = true, Func<int>? TextSize = null);

public sealed record PluginChat(string PluginId, string Id, Func<IReadOnlyList<ChatLineView>> Lines, string Empty, Func<int>? TextSize = null, Func<int>? LineGap = null);

public sealed record PluginFrameButton(string PluginId, int Slot, string Glyph, bool OpensSettings, Action? Press, bool OpensPrompt = false);

public sealed record PluginEncounterLog(
    string PluginId,
    string Id,
    Func<IReadOnlyList<EncounterView>> Records,
    Func<string> Folder,
    Func<string, bool>? Delete = null,
    Func<IReadOnlyList<EncounterHead>>? Heads = null,
    Func<string, EncounterView?>? Open = null);

public sealed record PluginIcons(string PluginId, string Id, Func<IReadOnlyList<IconView>> Icons, Func<int>? Size = null, Func<bool>? Vertical = null, Action<string>? Click = null, Action<string>? Remove = null, Func<int>? Gap = null, Func<int>? HeaderSize = null, Func<string>? Badge = null);

public sealed record PluginEditor(
    string PluginId,
    string Id,
    string Title,
    Func<IReadOnlyList<EditRowView>> Rows,
    Func<IReadOnlyList<EditCandidateView>> Candidates,
    Action<EditOp> Changed);

public sealed record PluginLoadError(string DllPath, string Message);

public sealed record PluginSetting(
    string PluginId,
    string Key,
    string Label,
    string Kind,
    string DefaultValue,
    Func<string?> Read,
    Action<string> Write,
    Action? Press = null,
    int Minimum = 0,
    int Maximum = 0,
    IReadOnlyList<string>? Choices = null,
    int Step = 0);

public sealed class PluginHost : IDisposable
{
    private readonly List<RunningPlugin> _running = [];
    private readonly List<PluginPanel> _panels = [];
    private readonly List<string> _logs = [];
    private readonly List<PluginSetting> _settings = [];
    private readonly List<PluginMeter> _meters = [];
    private readonly List<PluginChat> _chats = [];
    private readonly List<PluginFrameButton> _frames = [];
    private readonly List<PluginEncounterLog> _encounters = [];
    private readonly List<PluginIcons> _icons = [];
    private readonly List<PluginStepper> _steppers = [];
    private readonly List<PluginEditor> _editors = [];
    private readonly List<PluginLoadError> _errors = [];

    public IReadOnlyList<PluginPanel> Panels => _panels;

    public IReadOnlyList<string> Logs => _logs;

    public IReadOnlyList<PluginLoadError> Errors => _errors;

    public IReadOnlyList<PluginSetting> Settings => _settings;

    public IReadOnlyList<PluginMeter> Meters => _meters;

    public IReadOnlyList<PluginChat> Chats => _chats;

    public IReadOnlyList<PluginIcons> Icons => _icons;

    public IReadOnlyList<PluginStepper> Steppers => _steppers;

    /// <summary>The player pressed the minus (-1) or the plus (+1) of a stepper row; false when the plugin has no such row or failed on it.</summary>
    public bool PressStep(string pluginId, string stepperId, int direction)
    {
        var stepper = _steppers.FirstOrDefault(s => s.PluginId == pluginId && s.Id == stepperId);
        if (stepper == null) return false;
        try
        {
            stepper.Step(direction < 0 ? -1 : 1);
            return true;
        }
        catch (Exception exception)
        {
            _logs.Add($"{pluginId}: step failed: {exception.Message}");
            return false;
        }
    }

    public IReadOnlyList<PluginEditor> Editors => _editors;

    /// <summary>The player pressed the delete button of a row of a plugin that offers one (<c>Remove</c> of its icons).</summary>
    public bool RemoveIcon(string pluginId, string iconKey)
    {
        var icons = _icons.FirstOrDefault(i => i.PluginId == pluginId && i.Remove != null);
        if (icons == null) return false;
        try
        {
            icons.Remove!(iconKey);
            return true;
        }
        catch (Exception exception)
        {
            _logs.Add($"{pluginId}: icon removal failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>The player pressed an icon of a plugin that listens to presses (<c>Click</c> of its icons); false when there is no such icon list or the plugin failed on it.</summary>
    public bool PressIcon(string pluginId, string iconKey)
    {
        var icons = _icons.FirstOrDefault(i => i.PluginId == pluginId && i.Click != null);
        if (icons == null) return false;
        try
        {
            icons.Click!(iconKey);
            return true;
        }
        catch (Exception exception)
        {
            _logs.Add($"{pluginId}: icon press failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Hands a user edit to the editor widget it came from; false when the plugin has no such editor or the plugin failed on it.</summary>
    public bool ApplyEdit(string pluginId, string editorId, EditOp op)
    {
        var editor = _editors.FirstOrDefault(e => e.PluginId == pluginId && e.Id == editorId);
        if (editor == null) return false;
        try
        {
            editor.Changed(op);
            return true;
        }
        catch (Exception exception)
        {
            _logs.Add($"{pluginId}: edit failed: {exception.Message}");
            return false;
        }
    }

    public IReadOnlyList<PluginFrameButton> FrameButtons => _frames;

    public const int FrameButtonSlots = 2;

    public IReadOnlyList<PluginEncounterLog> EncounterLogs => _encounters;

    /// <summary>Runs a plugin object that is already in memory (tests, embedding). It is unloaded with the host.</summary>
    public void Add(string gameDirectory, IPlugin plugin)
    {
        var context = new HostContext(gameDirectory, plugin.Info.Id, this, "");
        plugin.Load(context);
        _running.Add(new RunningPlugin(plugin, null));
    }

    public void LoadDirectory(string gameDirectory, string pluginsDirectory)
    {
        if (!Directory.Exists(pluginsDirectory)) return;

        foreach (var folder in Directory.GetDirectories(pluginsDirectory))
        {
            foreach (var dll in Directory.GetFiles(folder, "*.dll"))
                LoadDll(gameDirectory, dll);
        }
    }

    public void Dispose()
    {
        foreach (var running in _running)
        {
            try { running.Instance.Unload(); }
            catch { /* a plugin failure must not block the rest */ }
        }

        var contexts = _running.Select(running => running.Context).OfType<PluginLoadContext>().Distinct().ToArray();
        _running.Clear();
        _panels.Clear();
        _steppers.Clear();
        _settings.Clear();
        _meters.Clear();
        _chats.Clear();
        _frames.Clear();
        _logs.Clear();
        _encounters.Clear();
        foreach (var context in contexts)
        {
            try { context.Unload(); }
            catch { /* collectible unload is best-effort */ }
        }
    }

    private void LoadDll(string gameDirectory, string dllPath)
    {
        var loadContext = new PluginLoadContext(dllPath);
        Assembly assembly;
        try
        {
            assembly = loadContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(dllPath)));
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            _errors.Add(new PluginLoadError(dllPath, ex.Message));
            loadContext.Unload();
            return;
        }

        var plugins = PluginLoadContext.PluginTypes(assembly);
        if (plugins.Count == 0)
        {
            _errors.Add(new PluginLoadError(dllPath, "IPlugin을 구현한 클래스가 없습니다."));
            loadContext.Unload();
            return;
        }

        foreach (var type in plugins)
        {
            try
            {
                if (Activator.CreateInstance(type) is not IPlugin plugin)
                    continue;
                var context = new HostContext(gameDirectory, plugin.Info.Id, this, Path.GetDirectoryName(Path.GetFullPath(dllPath)) ?? "");
                plugin.Load(context);
                _running.Add(new RunningPlugin(plugin, loadContext));
            }
            catch (Exception ex)
            {
                _errors.Add(new PluginLoadError(dllPath, ex.Message));
            }
        }
    }

    private void AddPanel(PluginPanel panel)
    {
        if (_panels.Any(existing => existing.PluginId == panel.PluginId && existing.Id == panel.Id))
            return;
        _panels.Add(panel);
    }

    private void AddMeter(PluginMeter meter)
    {
        if (_meters.Any(existing => existing.PluginId == meter.PluginId && existing.Id == meter.Id))
            return;
        _meters.Add(meter);
    }

    private void AddIcons(PluginIcons icons)
    {
        if (_icons.Any(existing => existing.PluginId == icons.PluginId && existing.Id == icons.Id)) return;
        _icons.Add(icons);
    }

    private void AddEditor(PluginEditor editor)
    {
        if (_editors.Any(existing => existing.PluginId == editor.PluginId && existing.Id == editor.Id)) return;
        _editors.Add(editor);
    }

    private void AddChat(PluginChat chat)
    {
        if (_chats.Any(existing => existing.PluginId == chat.PluginId && existing.Id == chat.Id))
            return;
        _chats.Add(chat);
    }

    private void AddFrameButton(PluginFrameButton button)
    {
        if (button.Slot < 0 || button.Slot >= FrameButtonSlots) return;
        if (_frames.Any(existing => existing.PluginId == button.PluginId && existing.Slot == button.Slot))
            return;
        _frames.Add(button);
    }

    private void AddLog(PluginEncounterLog log)
    {
        if (_encounters.Any(existing => existing.PluginId == log.PluginId && existing.Id == log.Id))
            return;
        _encounters.Add(log);
    }

    private void AddSetting(PluginSetting setting)
    {
        if (_settings.Any(existing => existing.PluginId == setting.PluginId && existing.Key == setting.Key))
            return;
        _settings.Add(setting);
    }

    private sealed record RunningPlugin(IPlugin Instance, PluginLoadContext? Context);

    private sealed class HostContext : IPluginContext
    {
        public HostContext(string gameDirectory, string pluginId, PluginHost host, string pluginDirectory)
        {
            GameDirectory = gameDirectory;
            PluginDirectory = pluginDirectory;
            Log = new HostLog(host);
            Ui = new HostUi(pluginId, host);
            Settings = JsonPluginSettings.Open(gameDirectory, pluginId);
            Controls = new HostSettings(pluginId, host, Settings);
        }

        public string GameDirectory { get; }

        public string PluginDirectory { get; }

        public IPluginLog Log { get; }

        public IUiRegistry Ui { get; }

        public IPluginSettings Settings { get; }

        public ISettingRegistry Controls { get; }
    }

    private sealed class HostLog(PluginHost host) : IPluginLog
    {
        public void Info(string message) => host._logs.Add(message);
    }

    private sealed class HostUi(string pluginId, PluginHost host) : IUiRegistry
    {
        public void AddIcons(string id, Func<IReadOnlyList<IconView>> icons, Func<int>? size = null, Func<bool>? vertical = null, Action<string>? click = null, Action<string>? remove = null, Func<int>? gap = null, Func<int>? headerSize = null, Func<string>? badge = null) =>
            host.AddIcons(new PluginIcons(pluginId, id, icons, size, vertical, click, remove, gap, headerSize, badge));

        public void AddEditor(string id, string title, Func<IReadOnlyList<EditRowView>> rows, Func<IReadOnlyList<EditCandidateView>> candidates, Action<EditOp> changed) =>
            host.AddEditor(new PluginEditor(pluginId, id, title, rows, candidates, changed));

        public void AddStepper(string id, string label, Func<string> value, Action<int> step)
        {
            if (host._steppers.Any(existing => existing.PluginId == pluginId && existing.Id == id)) return;
            host._steppers.Add(new PluginStepper(pluginId, id, label, value, step));
        }

        public void AddLabel(string id, string title, Func<string> text) =>
            host.AddPanel(new PluginPanel(pluginId, id, title, text));

        public void AddMeter(string id, Func<IReadOnlyList<MeterRowView>> rows, Func<int>? rowHeight = null, Func<int>? textSize = null) =>
            host.AddMeter(new PluginMeter(pluginId, id, rows, rowHeight, TextSize: textSize));

        public void AddList(string id, Func<IReadOnlyList<MeterRowView>> rows, string empty, Func<int>? rowHeight = null) =>
            host.AddMeter(new PluginMeter(pluginId, id, rows, rowHeight, empty, false));

        public void AddChat(string id, Func<IReadOnlyList<ChatLineView>> lines, string empty, Func<int>? textSize = null, Func<int>? lineGap = null) =>
            host.AddChat(new PluginChat(pluginId, id, lines, empty, textSize, lineGap));

        public void AddEncounterLog(
            string id,
            Func<IReadOnlyList<EncounterView>> records,
            Func<string> folder,
            Func<string, bool>? delete = null,
            Func<IReadOnlyList<EncounterHead>>? heads = null,
            Func<string, EncounterView?>? open = null) =>
            host.AddLog(new PluginEncounterLog(pluginId, id, records, folder, delete, heads, open));
    }

    private sealed class HostSettings(string pluginId, PluginHost host, IPluginSettings settings) : ISettingRegistry
    {
        public void AddText(string key, string label, string defaultValue, int minimum = 0, int maximum = 0, int step = 0) =>
            host.AddSetting(new PluginSetting(pluginId, key, label, "text", defaultValue, () => settings.Get(key), value => settings.Set(key, value), Minimum: minimum, Maximum: maximum, Step: step));

        public void AddChoice(string key, string label, IReadOnlyList<string> choices, string defaultValue) =>
            host.AddSetting(new PluginSetting(pluginId, key, label, "text", defaultValue, () => settings.Get(key), value => settings.Set(key, value), Choices: choices));

        public void AddToggle(string key, string label, bool defaultValue) =>
            AddToggle(key, label, defaultValue, null);

        public void AddToggle(string key, string label, bool defaultValue, Action<bool>? changed) =>
            host.AddSetting(new PluginSetting(pluginId, key, label, "toggle", defaultValue ? "true" : "false", () => settings.Get(key), value =>
            {
                settings.Set(key, value);
                changed?.Invoke(value == "true");
            }));

        public void AddButton(string key, string label, Action press) =>
            host.AddSetting(new PluginSetting(pluginId, key, label, "button", "", () => null, _ => { }, press));

        public void UseFrameButton(int slot, string glyph, bool opensSettings = false, Action? press = null) =>
            host.AddFrameButton(new PluginFrameButton(pluginId, slot, glyph, opensSettings, press));

        public void UsePromptButton(int slot, string glyph) =>
            host.AddFrameButton(new PluginFrameButton(pluginId, slot, glyph, false, null, true));
    }
}

public sealed class JsonPluginSettings : IPluginSettings
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    private JsonPluginSettings(string path, Dictionary<string, string> values)
    {
        _path = path;
        _values = values;
    }

    public static JsonPluginSettings Open(string gameDirectory, string pluginId)
    {
        var path = Path.Combine(gameDirectory, "bpsr", "config", pluginId + ".json");
        Dictionary<string, string> values = [];
        if (File.Exists(path))
        {
            try
            {
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            }
            catch (JsonException)
            {
                values = [];
            }
        }

        return new JsonPluginSettings(path, values);
    }

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _values[key] = value;
        File.WriteAllText(_path, JsonSerializer.Serialize(_values));
    }
}
