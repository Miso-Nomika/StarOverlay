using System.Text.Json;

namespace StarOverlay.Framework;

/// <summary>
/// Knows whether a newer release exists. The service asks once when the game starts; when a newer version is already
/// known from an earlier check, nobody is asked again. A failed check is silent.
/// </summary>
public static class LiveUpdate
{
    /// <summary>The page of the newest release (the in-game notice opens it).</summary>
    public const string ReleasePage = "https://github.com/Miso-Nomika/StarOverlay/releases/latest";

    private static readonly TimeSpan Throttle = TimeSpan.FromHours(1);
    private static string _latest = "";
    private static string _current = "";

    /// <summary>The newer version (like "0.4.5"), or empty when there is none or it is unknown.</summary>
    public static string Latest => Volatile.Read(ref _latest);

    /// <summary>The version that is running (like "0.4.4"), known once the check has started.</summary>
    public static string Current => Volatile.Read(ref _current);

    public static bool Available => Latest.Length > 0;

    public static void Reset()
    {
        Volatile.Write(ref _latest, "");
        Volatile.Write(ref _current, "");
    }

    /// <summary>Reads "v0.4.5" / "0.4.5" into a version; null when it is not one.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().TrimStart('v', 'V');
        var cut = t.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? v : null;
    }

    public static bool IsNewer(string? latest, Version current)
    {
        var v = Parse(latest);
        return v is not null && v > current;
    }

    /// <summary>
    /// Runs the one check. <paramref name="fetch"/> returns the latest release tag, or null when it cannot be found.
    /// Returns true when GitHub (the fetch) was asked.
    /// </summary>
    public static async Task<bool> CheckAsync(Func<Task<string?>> fetch, string stateFile, Version current, DateTime nowUtc)
    {
        Volatile.Write(ref _current, current.ToString(3));
        var state = Load(stateFile);
        if (IsNewer(state.Latest, current))
        {
            Volatile.Write(ref _latest, state.Latest); // already known: no call
            return false;
        }

        if (state.CheckedAt is { } at && nowUtc - at < Throttle && nowUtc >= at) return false; // a restart of the service right after a check
        string? tag = null;
        try
        {
            tag = await fetch();
        }
        catch (Exception)
        {
        }

        var found = Parse(tag) is null ? "" : tag!.Trim().TrimStart('v', 'V');
        if (found.Length > 0) Save(stateFile, new State { CheckedAt = nowUtc, Latest = found });
        else Save(stateFile, new State { CheckedAt = nowUtc, Latest = state.Latest });
        if (IsNewer(found, current)) Volatile.Write(ref _latest, found);
        return true;
    }

    private static State Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<State>(File.ReadAllText(file)) ?? new State() : new State();
        }
        catch (Exception)
        {
            return new State();
        }
    }

    private static void Save(string file, State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(state));
        }
        catch (Exception)
        {
        }
    }

    private sealed class State
    {
        public DateTime? CheckedAt { get; set; }
        public string Latest { get; set; } = "";
    }
}
