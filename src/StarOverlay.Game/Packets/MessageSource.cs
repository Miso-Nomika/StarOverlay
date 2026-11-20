using BPSR_ZDPSLib;

namespace StarOverlay.Game.Packets;

/// <summary>
/// Message level input: the game's own network layer already decoded the message (service, method, payload),
/// so TCP reassembly and decompression are skipped. Used by the in-game tap (T35) and by tests.
/// </summary>
public abstract class MessageSource : IPacketSource
{
    NetCap? _cap;

    public abstract string Name { get; }
    public abstract string Label { get; }

    public void Start(NetCap cap)
    {
        Open();
        cap.Init(new NetCapConfig { ExeNames = GameProcesses.Names, TrustSource = true });
        cap.StartExternal();
        _cap = cap;
        BeginPump();
    }

    public abstract void Stop();

    protected abstract void Open();
    protected abstract void BeginPump();

    /// <summary>Inbound = server notify, outbound = client call (for example accepting a party applicant).</summary>
    protected void Feed(ulong service, uint method, ReadOnlySpan<byte> payload, bool outbound, DateTime utcTime)
    {
        if (_cap == null) return;
        if (outbound) _cap.DispatchCall((uint)service, method, 0, payload, utcTime);
        else _cap.DispatchNotify(service, method, payload, utcTime);
    }

    public void Dispose() => Stop();
}

/// <summary>Messages pushed by code, no thread of its own.</summary>
public sealed class ManualMessageSource : MessageSource
{
    public override string Name => "message";
    public override string Label => "manual";

    protected override void Open()
    {
    }

    protected override void BeginPump()
    {
    }

    public override void Stop()
    {
    }

    public void Push(ulong service, uint method, ReadOnlySpan<byte> payload, bool outbound = false) =>
        Feed(service, method, payload, outbound, DateTime.UtcNow);
}
