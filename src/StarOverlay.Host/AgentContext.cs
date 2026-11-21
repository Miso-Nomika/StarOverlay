using System.Diagnostics;
using StarOverlay.Core.Mods;
using StarOverlay.Framework;
using StarOverlay.Game;
using StarOverlay.Game.Ingame;
using StarOverlay.Game.Packets;

namespace StarOverlay.Host;

/// <summary>
/// "--agent" mode: no windows of its own. Collects game data, runs the plugins and serves the in-game agent over a pipe.
/// The only window it ever opens is the small translation prompt input, when the user asks for it in game.
/// It ends by itself when the agent is gone and the game is not running.
/// </summary>
internal sealed class AgentContext : ApplicationContext, IIngameHooks
{
    private const int IdleSecondsBeforeExit = 3;

    private readonly string _home;
    private readonly Form _owner;
    private readonly GameCapture _capture;
    private readonly IngameService _service;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private PromptWindow? _prompt;
    private PluginHost _host = new();
    private DateTime _idleSince = DateTime.UtcNow;

    public AgentContext(string install, string home)
    {
        _home = home;
        _owner = new HiddenOwner
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            Size = new Size(1, 1),
            Opacity = 0,
        };
        _ = _owner.Handle; // a window handle for BeginInvoke; never shown, so it cannot appear in Alt+Tab
        _host.LoadDirectory(home, ModLayout.PluginsDirectory(home));

        StarOverlay.Core.Capture.CaptureSetup.StartHelper();
        _capture = new GameCapture(home, [new PipePacketSource()]);
        _capture.Start();

        var config = Path.Combine(ModLayout.Root(home), "config");
        _service = new IngameService(this, IngameService.DefaultPipeName(), Path.Combine(config, "ingame-layout.json"), ModLayout.PluginsDirectory(home))
        {
            TablesRoot = Path.Combine(home, "tables"), // the same folder GameCapture reads first
        };
        _service.TablesWritten += () => Task.Run(_capture.OnTablesWritten);
        _service.Start();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        UpdateCheck.Start(); // once per game start: the service lives as long as the game does
    }

    public PluginHost Host => _host;

    public IReadOnlyList<PluginFolder> Plugins() => PluginFolders.List(_home);

    public void SetPluginEnabled(string pluginId, bool enabled) =>
        _owner.BeginInvoke(() =>
        {
            if (!PluginFolders.TrySetEnabled(_home, pluginId, enabled, out _)) return;
            var old = _host;
            var next = new PluginHost();
            next.LoadDirectory(_home, ModLayout.PluginsDirectory(_home));
            _host = next;
            old.Dispose();
        });

    // settings and the translation prompt are drawn inside the game by the agent; the service opens no window of its own
    public void OpenSettings(string pluginId)
    {
    }

    // the long translation prompt is typed in a small window of the service (in-game text input and the IME do not mix);
    // it opens only when the user presses the in-game "입력하기" button
    public void OpenPrompt() =>
        _owner.BeginInvoke(() =>
        {
            var setting = _host.Settings.FirstOrDefault(item => item.Key == "translate-prompt");
            if (setting is null) return;
            JapaneseGloss.Listen();
            QwenTranslator.Listen();
            if (_prompt is null || _prompt.IsDisposed)
            {
                var device = _host.Settings.FirstOrDefault(item => item.Key == "translate-device");
                _prompt = new PromptWindow(_owner, setting, device);
            }

            _prompt.BringToTop();
        });

    public void CopyText(string text) =>
        _owner.BeginInvoke(() =>
        {
            try
            {
                Clipboard.SetText(text); // the window thread is the one the clipboard needs
            }
            catch (Exception)
            {
                // the clipboard can be busy for a moment; the user just presses the button again
            }
        });

    public (string Roman, string Translation) Gloss(ChatLineView line)
    {
        var (romaji, korean) = JapaneseGloss.Parts(line.Text, line.At);
        return (romaji, korean);
    }

    private void Tick()
    {
        if (_service.Connected || Process.GetProcessesByName("StarASIA").Length > 0)
        {
            _idleSince = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _idleSince).TotalSeconds >= IdleSecondsBeforeExit) ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        QwenTranslator.Stop();
        _service.Dispose();
        _capture.Dispose();
        _host.Dispose();
        _owner.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>The service's invisible message window: a tool window is left out of Alt+Tab and the taskbar.</summary>
internal sealed class HiddenOwner : Form
{
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
}
