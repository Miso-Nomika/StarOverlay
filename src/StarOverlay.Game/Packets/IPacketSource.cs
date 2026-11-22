using BPSR_ZDPSLib;

namespace StarOverlay.Game.Packets;

public static class GameProcesses
{
    /// <summary>The game's process names (without .exe): the official client, the Steam version (StarASIA_STEAM.exe) and older names.</summary>
    public static readonly string[] Names = ["StarASIA", "StarASIA_STEAM", "BPSR", "BPSR_STEAM"];
}

/// <summary>Where game packets come from (raw-socket helper pipe, recorded frames).</summary>
public interface IPacketSource : IDisposable
{
    /// <summary>Short id written to capture.txt: pipe, replay.</summary>
    string Name { get; }

    /// <summary>Human readable detail (device name, pipe name, file).</summary>
    string Label { get; }

    /// <summary>
    /// Opens the source and connects it to the capture engine. Throws when the source is not usable,
    /// before touching <paramref name="cap"/> where possible, so the next source can be tried.
    /// </summary>
    void Start(NetCap cap);

    void Stop();
}
