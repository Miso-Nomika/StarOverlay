namespace StarOverlay.Framework;

/// <summary>One preset of the game: <paramref name="Kind"/> is "mind" (the season ability set, "심상") or "char" (the character plan, "캐릭터"); <paramref name="State"/> is
/// "current", "ready" (can be switched to), "empty" (nothing saved in it) or "locked" (not unlocked yet); <paramref name="Icon"/> is the picture of the preset: "profession:&lt;id&gt;" (the class of a character plan) or the address of a sprite of the game (the main node of a season ability set), empty when it has none.</summary>
public sealed record PresetItem(string Kind, int Id, string Name, string State, string Icon = "")
{
    public string Key => Kind + ":" + Id;
}

/// <summary>
/// The presets of the game and the switch the player asked for (plugin "빠른 프리셋"). The in-game agent reads the presets from the game and sends them;
/// the service sends back the switch the player pressed. While the plugin is not loaded (<see cref="Active"/> false) the agent does nothing at all.
/// </summary>
public static class QuickPreset
{
    private static readonly object Gate = new();
    private static PresetItem[] _items = [];
    private static bool _active;
    private static string _request = "";
    private static int _serial;
    private static string _failedKey = "";
    private static long _failedUntilMs;
    private static string _failedWhy = "";

    /// <summary>Raised when the plugin is switched on or off, a switch was asked for, or a refresh was asked for.</summary>
    public static event Action? Changed;

    public static bool Active
    {
        get { lock (Gate) return _active; }
    }

    /// <summary>
    /// A plugin that is loaded switches this on, one that is unloaded switches it off. The service builds the plugins of a new host before it lets the old host go
    /// (switching any plugin on or off does that), so the old and the new instance of the same plugin are both there for a moment: the plugin counts as on
    /// while at least one instance holds it.
    /// </summary>
    public static void SetActive(bool active)
    {
        lock (Gate)
        {
            _holders = Math.Max(0, _holders + (active ? 1 : -1));
            var now = _holders > 0;
            if (_active == now) return;
            _active = now;
            if (!now) _items = [];
        }

        Changed?.Invoke();
    }

    private static int _holders;

    public static IReadOnlyList<PresetItem> Items
    {
        get { lock (Gate) return _items; }
    }

    /// <summary>The newest request for the agent: "kind:id" to switch, "refresh" to read the presets again; <see cref="Serial"/> grows with every request so the same one can be asked twice.</summary>
    public static (string Request, int Serial) Pending
    {
        get { lock (Gate) return (_request, _serial); }
    }

    /// <summary>What the agent read from the game (it sends the list when something changed).</summary>
    public static void SetItems(IEnumerable<PresetItem> items)
    {
        var next = items.ToArray();
        lock (Gate)
        {
            if (_items.SequenceEqual(next)) return;
            _items = next;
        }
    }

    /// <summary>The player pressed a preset of the list: asks the agent to switch to it. Only a preset that is "ready" is switched to.</summary>
    public static bool RequestSwitch(string key)
    {
        lock (Gate)
        {
            var item = _items.FirstOrDefault(i => i.Key == key);
            if (item is not { State: "ready" }) return false;
            _request = key;
            _serial++;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Asks the agent to read the presets again.</summary>
    public static void RequestRefresh()
    {
        lock (Gate)
        {
            _request = "refresh";
            _serial++;
        }

        Changed?.Invoke();
    }

    /// <summary>The agent could not switch to <paramref name="key"/> (<paramref name="why"/>: "changed" = the game has changes that are not saved, "failed" = the game refused).</summary>
    public static void NoteFailed(string key, string why, long nowMs)
    {
        lock (Gate)
        {
            _failedKey = key;
            _failedWhy = why;
            _failedUntilMs = nowMs + 3000;
        }
    }

    /// <summary>The preset whose switch just failed (the row shows why for three seconds), or empty.</summary>
    public static (string Key, string Why) RecentFailure(long nowMs)
    {
        lock (Gate) return nowMs < _failedUntilMs ? (_failedKey, _failedWhy) : ("", "");
    }

    /// <summary>For tests: forget everything.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _items = [];
            _active = false;
            _holders = 0;
            _request = "";
            _serial = 0;
            _failedKey = "";
            _failedWhy = "";
            _failedUntilMs = 0;
        }
    }
}
