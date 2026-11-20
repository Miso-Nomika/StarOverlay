using BPSR_ZDPSLib;

namespace StarOverlay.Game.Packets;

/// <summary>
/// Base for sources that deliver whole IPv4 packets (no link layer): the raw-socket helper pipe and recorded frames.
/// </summary>
public abstract class ExternalPacketSource : IPacketSource
{
    NetCap? _cap;

    public abstract string Name { get; }
    public abstract string Label { get; }

    public void Start(NetCap cap)
    {
        Open(); // may throw, nothing has been started yet
        cap.Init(new NetCapConfig { ExeNames = GameProcesses.Names, TrustSource = true });
        cap.StartExternal();
        _cap = cap;
        BeginPump();
    }

    public virtual void Stop()
    {
    }

    /// <summary>Acquire the resource (wait for the pipe client, open the file). Throws on failure.</summary>
    protected abstract void Open();

    /// <summary>Start the thread that calls <see cref="Feed"/>. Called after the capture engine is ready.</summary>
    protected abstract void BeginPump();

    protected void Feed(ReadOnlySpan<byte> ip, bool outbound, DateTime utcTime)
    {
        _cap?.ProcessIpPacket(ip, outbound, utcTime);
    }

    public virtual void Dispose() => Stop();
}
