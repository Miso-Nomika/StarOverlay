using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using StarOverlay.Framework;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Game.Ingame;

/// <summary>
/// Pipe server for the in-game agent. Sends a full UI snapshot when something changed (at most 10 per second),
/// applies the agent's commands, and forwards game messages the agent saw in the game's own network layer.
/// Only the current Windows user can open the pipe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IngameService : IDisposable
{
    public const int TickMilliseconds = 100;

    readonly IIngameHooks _hooks;
    readonly string _pipeName;
    readonly LayoutStore _layouts;
    readonly UiSnapshotBuilder _builder;
    readonly object _write = new();
    readonly object _state = new();
    readonly CancellationTokenSource _stop = new();
    NamedPipeServerStream? _server;
    StreamWriter? _writer;
    string _lastWindows = "";
    int _rev;
    string _lastTickError = "";
    volatile bool _connected;
    volatile bool _dirty;
    Thread? _accept;
    Thread? _ticker;

    public IngameService(IIngameHooks hooks, string pipeName, string layoutPath, string? assetsRoot = null)
    {
        _hooks = hooks;
        _pipeName = pipeName;
        _layouts = new LayoutStore(layoutPath);
        _builder = new UiSnapshotBuilder(hooks, _layouts);
        AssetsRoot = assetsRoot;
        SkillRepeat.Changed += SendRepeat;
    }


    /// <summary>Tells the agent which skills to repeat while held (also right after it connects).</summary>
    void SendRepeat() =>
        Send(new Envelope { T = MessageType.Repeat, Repeat = new RepeatDto { Active = SkillRepeat.Active, Skills = SkillRepeat.Skills.ToList(), IntervalMs = SkillRepeat.IntervalMs } });

    public string? AssetsRoot { get; }

    /// <summary>Where the agent saves the game's tables; null: the service does not ask for them.</summary>
    public string? TablesRoot { get; init; }

    /// <summary>The agent saved new table files (raised on the pipe thread).</summary>
    public event Action? TablesWritten;

    public bool Connected => _connected;

    public int SnapshotsSent { get; private set; }

    public int ConnectionsAccepted { get; private set; }

    public string AgentGameHash { get; private set; } = "";

    public string AgentUnity { get; private set; } = "";

    public string AgentErrors { get; private set; } = "";

    /// <summary>Raised on the reader thread for each game message the agent forwarded (T35).</summary>
    public event Action<NetMessageDto>? NetMessage;

    /// <summary>Raised when a client connected (before the first snapshot).</summary>
    public event Action? AgentConnected;

    public event Action? AgentDisconnected;

    public static string DefaultPipeName() => Protocol.PipeName(WindowsIdentity.GetCurrent().User?.Value ?? "unknown");

    public void Start()
    {
        _accept = new Thread(AcceptLoop) { IsBackground = true, Name = "ingame-accept" };
        _accept.Start();
        _ticker = new Thread(TickLoop) { IsBackground = true, Name = "ingame-ticker" };
        _ticker.Start();
    }

    public void Dispose()
    {
        SkillRepeat.Changed -= SendRepeat;
        _stop.Cancel();
        CloseClient();
        try
        {
            _server?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The access rules of a pipe created with this user (tests check that only this user is allowed).</summary>
    public static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return security;
    }

    void AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // real buffers matter: with size 0 a write waits for the other side to read, and when both sides
                // write first (hello) and read second they block each other forever
                _server = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, CreateSecurity());
                _server.WaitForConnectionAsync(_stop.Token).Wait(_stop.Token);
                Serve(_server);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested) Thread.Sleep(500);
            }
            finally
            {
                CloseClient();
                try
                {
                    _server?.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    void Serve(NamedPipeServerStream server)
    {
        var reader = new StreamReader(server, new UTF8Encoding(false));
        lock (_write)
        {
            _writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        ConnectionsAccepted++;
        AgentConnected?.Invoke();
        Send(new Envelope { T = MessageType.Hello, V = Protocol.Version });
        if (AssetsRoot is not null) Send(new Envelope { T = MessageType.Assets, Root = AssetsRoot });
        if (TablesRoot is not null) Send(new Envelope { T = MessageType.Tables, Root = TablesRoot });
        SendRepeat(); // a reconnecting agent learns the chosen skills again
        lock (_state) _lastWindows = ""; // the new client gets everything again
        _connected = true;
        _dirty = true;

        string? line;
        while (!_stop.IsCancellationRequested && (line = reader.ReadLine()) is not null)
        {
            var message = ProtocolJson.Read(line);
            if (message is null)
            {
                continue;
            }

            try
            {
                Handle(message);
            }
            catch (Exception ex)
            {
                AgentErrors = "명령 처리 오류: " + ex.Message;
            }
        }
    }

    void CloseClient()
    {
        var was = _connected;
        _connected = false;
        lock (_write)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception)
            {
            }

            _writer = null;
        }

        if (was)
        {
            lock (_settingsShown)
            {
                _settingsShown.Clear();
                GpuMemory.Watching = false; // nobody looks at a settings window any more
            }

            AgentDisconnected?.Invoke();
        }
    }

    void TickLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            Thread.Sleep(TickMilliseconds);
            if (!_connected) continue;
            try
            {
                PushSnapshot();
            }
            catch (Exception ex)
            {
                // a failing plugin callback or a closed pipe: try again next tick (logged once per kind)
                if (_lastTickError != ex.Message)
                {
                    _lastTickError = ex.Message;
                }
            }
        }
    }

    /// <summary>Builds the snapshot and sends it when it differs from the last one sent. Public for tests.</summary>
    public bool PushSnapshot()
    {
        var windows = _builder.Build();
        var json = ProtocolJson.WriteWindows(windows);
        lock (_state)
        {
            if (!_dirty && json == _lastWindows) return false;
            _lastWindows = json;
            _dirty = false;
            _rev++;
        }

        Send(new Envelope { T = MessageType.Ui, V = Protocol.Version, Rev = _rev, Windows = windows });
        SnapshotsSent++;
        return true;
    }

    /// <summary>The snapshot as it would be sent now (tests).</summary>
    public List<WindowDto> CurrentWindows() => _builder.Build();

    public void Send(Envelope message)
    {
        var line = ProtocolJson.Write(message);
        lock (_write)
        {
            _writer?.WriteLine(line);
        }
    }

    /// <summary>Applies one message from the agent. Public so tests can drive it without a pipe.</summary>
    public void Handle(Envelope message)
    {
        switch (message.T)
        {
            case MessageType.Hello:
                AgentGameHash = message.GameHash ?? "";
                AgentUnity = message.Unity ?? "";
                if (message.V != Protocol.Version)
                {
                    AgentErrors = $"프로토콜 버전이 다릅니다(에이전트 {message.V}, 서비스 {Protocol.Version}).";
                    Send(new Envelope { T = MessageType.Err, Error = AgentErrors });
                    CloseClient();
                }

                break;
            case MessageType.Err:
                AgentErrors = message.Error ?? "";
                break;
            case MessageType.TablesDone:
                if (message.Written == true) TablesWritten?.Invoke();
                break;
            case MessageType.Msg when message.Msg is not null:
                NetMessage?.Invoke(message.Msg);
                break;
            case MessageType.Cmd when message.Cmd is { Action: CommandAction.SkillPressed } pressed:
                if (int.TryParse(pressed.Key, out var skill)) SkillRepeat.NotePressed(skill, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                _dirty = true; // the candidate list of the plugin changed
                break;
            case MessageType.Cmd when message.Cmd is not null:
                Apply(message.Cmd);
                _dirty = true;
                break;
        }
    }

    static LayoutDto Edited(LayoutDto layout, Action<LayoutDto> change)
    {
        var copy = layout.Copy();
        change(copy);
        return copy;
    }

    void ApplyEdit(CommandDto cmd)
    {
        var split = cmd.Key.IndexOf(':');
        if (split <= 0) return;
        var colon = cmd.Value.IndexOf(':');
        var prefixed = colon > 0 && cmd.Value[..colon] is "field" or "set";
        var action = prefixed ? cmd.Value[..colon] : cmd.Value;
        var value = prefixed ? cmd.Value[(colon + 1)..] : "";
        if (_hooks.Host.ApplyEdit(cmd.Window, cmd.Key[..split], new EditOp(action, cmd.Key[(split + 1)..], value))) _dirty = true;
    }

    void Apply(CommandDto cmd)
    {
        var windows = _builder.Build();
        var current = cmd.Window.EndsWith(UiSnapshotBuilder.SettingsSuffix, StringComparison.Ordinal)
            ? windows.FirstOrDefault(w => w.Id + UiSnapshotBuilder.SettingsSuffix == cmd.Window)?.SettingsLayout
            : windows.FirstOrDefault(w => w.Id == cmd.Window)?.Layout;
        current ??= new LayoutDto { X = 0.05, Y = 0.1, W = 0.2, H = 0.2 };
        switch (cmd.Action)
        {
            case CommandAction.Move:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => { c.X = cmd.X; c.Y = cmd.Y; }));
                break;
            case CommandAction.Resize:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => { c.W = cmd.W; c.H = cmd.H; }));
                break;
            case CommandAction.Pin:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => c.Pinned = cmd.Value == "true"));
                break;
            case CommandAction.Collapse:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => c.Collapsed = cmd.Value == "true"));
                break;
            case CommandAction.Opacity:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => { if (int.TryParse(cmd.Value, out var o)) c.Opacity = o; }));
                break;
            case CommandAction.Channels:
                _layouts.Update(cmd.Window, current, l => Edited(l, c => { if (int.TryParse(cmd.Value, out var mask)) c.Channels = mask & 63; }));
                break;            case CommandAction.Edit:
                ApplyEdit(cmd);
                break;
            case CommandAction.Toggle:
                Setting(cmd)?.Write(cmd.Value == "true" ? "true" : "false");
                break;
            case CommandAction.Set:
                ApplySet(cmd);
                break;
            case CommandAction.SettingsShown:
                lock (_settingsShown)
                {
                    if (cmd.Value == "true") _settingsShown.Add(cmd.Window);
                    else _settingsShown.Remove(cmd.Window);
                    GpuMemory.Watching = _settingsShown.Count > 0; // the card is read only while a settings window is open
                }

                break;
            case CommandAction.Records:
                SendRecords(cmd.Window);
                break;
            case CommandAction.RecordOpen:
                SendRecord(cmd.Window, cmd.Key);
                break;
            case CommandAction.RecordDelete:
                DeleteRecords(cmd);
                break;
            case CommandAction.Press when cmd.Key.StartsWith("step:", StringComparison.Ordinal):
                var cut = cmd.Key.LastIndexOf(':');
                if (cut > "step:".Length && int.TryParse(cmd.Key[(cut + 1)..], out var direction))
                    _hooks.Host.PressStep(cmd.Window, cmd.Key["step:".Length..cut], direction);
                break;
            case CommandAction.Press when cmd.Key.StartsWith("icon-remove:", StringComparison.Ordinal):
                _hooks.Host.RemoveIcon(cmd.Window, cmd.Key["icon-remove:".Length..]);
                break;
            case CommandAction.Press when cmd.Key.StartsWith("icon:", StringComparison.Ordinal):
                _hooks.Host.PressIcon(cmd.Window, cmd.Key["icon:".Length..]);
                break;
            case CommandAction.Press when cmd.Key.StartsWith("frame:", StringComparison.Ordinal):
                PressFrame(cmd);
                break;
            case CommandAction.Press:
                Setting(cmd)?.Press?.Invoke();
                break;
            case CommandAction.Plugin when cmd.Window == UiSnapshotBuilder.RootWindowId:
                var state = cmd.Value is "true" ? "on" : cmd.Value is "false" ? "off" : cmd.Value;
                if (state is not ("on" or "hidden" or "invisible" or "off")) break;
                _hooks.SetPluginEnabled(cmd.Key, state != "off");
                if (state != "off")
                {
                    var plugin = windows.FirstOrDefault(w => w.Id == cmd.Key)?.Layout ?? new LayoutDto { X = 0.05, Y = 0.1, W = 0.2, H = 0.2 };
                    _layouts.Update(cmd.Key, plugin, l => Edited(l, c =>
                    {
                        c.HeaderHidden = state == "hidden";
                        c.WindowHidden = state == "invisible";
                    }));
                }

                break;
            case CommandAction.Settings:
                _hooks.OpenSettings(cmd.Window);
                break;
            case CommandAction.Copy:
                if (cmd.Value.Length > 0) _hooks.CopyText(cmd.Value.Length > 4000 ? cmd.Value[..4000] : cmd.Value);
                break;
            case CommandAction.Prompt:
                _hooks.OpenPrompt();
                break;
        }
    }

    /// <summary>Writes a setting from the game. Only values the in-game controls can produce are accepted.</summary>
    void ApplySet(CommandDto cmd)
    {
        var setting = Setting(cmd);
        if (setting is null || setting.Kind != "text") return;
        var dto = new UiSnapshotBuilder(_hooks, _layouts).Build().FirstOrDefault(w => w.Id == cmd.Window)?.Settings.FirstOrDefault(s => s.Key == cmd.Key);
        if (dto is null) return;
        if (dto.Choices.Count > 0)
        {
            if (dto.Choices.Contains(cmd.Value)) setting.Write(cmd.Value);
            if (cmd.Key == "translate-device" && dto.Choices.Contains(cmd.Value)) TranslationDevice.Apply(cmd.Value); // the running translator follows at once
            if (cmd.Key == "translate-model" && dto.Choices.Contains(cmd.Value)) TranslationModel.Apply(cmd.Value);
        }
        else if (dto.Max > dto.Min)
        {
            if (int.TryParse(cmd.Value, out var number)) setting.Write(Math.Clamp(number, dto.Min, dto.Max).ToString());
        }
    }

    /// <summary>Key = one id, several ids separated by commas, or "oldest:N" for the N oldest records of the whole log.</summary>
    void DeleteRecords(CommandDto cmd)
    {
        var log = _hooks.Host.EncounterLogs.FirstOrDefault(l => l.PluginId == cmd.Window);
        if (log?.Delete is null) return;
        IEnumerable<string> ids = cmd.Key.StartsWith("oldest:", StringComparison.Ordinal)
            ? int.TryParse(cmd.Key.AsSpan("oldest:".Length), out var count) && count > 0 ? RecordMapper.OldestIds(log, Math.Min(count, 1000)) : new List<string>()
            : cmd.Key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var any = false;
        foreach (var id in ids)
            if (log.Delete(id)) any = true;
        if (any) SendRecords(cmd.Window);
    }

    readonly HashSet<string> _settingsShown = new();

    void SendRecords(string window)
    {
        var log = _hooks.Host.EncounterLogs.FirstOrDefault(l => l.PluginId == window);
        Send(new Envelope { T = MessageType.Records, Window = window, Records = log is null ? [] : RecordMapper.Heads(log) });
    }

    void SendRecord(string window, string id)
    {
        var log = _hooks.Host.EncounterLogs.FirstOrDefault(l => l.PluginId == window);
        Send(new Envelope { T = MessageType.Record, Window = window, Record = log is null ? null : RecordMapper.Open(log, id) });
    }

    PluginSetting? Setting(CommandDto cmd) =>
        _hooks.Host.Settings.FirstOrDefault(s => s.PluginId == cmd.Window && s.Key == cmd.Key);

    void PressFrame(CommandDto cmd)
    {
        if (!int.TryParse(cmd.Key.AsSpan("frame:".Length), out var slot)) return;
        var button = _hooks.Host.FrameButtons.FirstOrDefault(b => b.PluginId == cmd.Window && b.Slot == slot);
        if (button is null) return;
        if (button.OpensSettings) _hooks.OpenSettings(cmd.Window);
        else if (button.OpensPrompt) _hooks.OpenPrompt();
        else button.Press?.Invoke();
    }
}
