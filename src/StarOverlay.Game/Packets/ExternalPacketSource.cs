using BPSR_ZDPSLib;

namespace StarOverlay.Game.Packets;

/// <summary>
/// Base for sources that deliver whole IPv4 packets (no link layer): the raw-socket helper pipe and recorded frames.
/// </summary>
public abstract class ExternalPacketSource : IPacketSource
{
    NetCap? _cap;
    PacketRecorder? _recorder;

    public abstract string Name { get; }
    public abstract string Label { get; }

    /// <summary>Raised for every packet before it is handed to the capture engine (used by the recorder).</summary>
    public event Action<ReadOnlyMemory<byte>, bool, DateTime>? PacketReceived;

    public void Start(NetCap cap)
    {
        Open(); // may throw, nothing has been started yet
        cap.Init(new NetCapConfig { ExeNames = GameProcesses.Names, TrustSource = true });
        cap.StartExternal();
        _cap = cap;
        _recorder = PacketRecorder.TryStart(this);
        BeginPump();
    }

    public virtual void Stop()
    {
        _recorder?.Dispose();
        _recorder = null;
    }

    /// <summary>Acquire the resource (wait for the pipe client, open the file). Throws on failure.</summary>
    protected abstract void Open();

    /// <summary>Start the thread that calls <see cref="Feed"/>. Called after the capture engine is ready.</summary>
    protected abstract void BeginPump();

    protected void Feed(ReadOnlySpan<byte> ip, bool outbound, DateTime utcTime)
    {
        var handler = PacketReceived;
        if (handler != null) handler(ip.ToArray(), outbound, utcTime);
        _cap?.ProcessIpPacket(ip, outbound, utcTime);
    }

    public virtual void Dispose() => Stop();
}
