namespace StarOverlay.Game.Packets;

/// <summary>Feeds recorded frames (written with PacketFrames, or the older T36 dump) as fast as possible. Used by tests.</summary>
public sealed class ReplayPacketSource : ExternalPacketSource
{
    readonly string _path;
    readonly bool _legacy;
    FileStream? _file;
    Thread? _thread;
    volatile bool _stop;
    readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReplayPacketSource(string path, bool legacyFormat = false)
    {
        _path = path;
        _legacy = legacyFormat;
    }

    public override string Name => "replay";
    public override string Label => Path.GetFileName(_path);

    /// <summary>Completes when the last frame has been handed to the engine (the engine may still be parsing).</summary>
    public Task Completed => _done.Task;

    public int Frames { get; private set; }
    public int InboundFrames { get; private set; }
    public int OutboundFrames { get; private set; }

    protected override void Open() =>
        _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);

    protected override void BeginPump()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "replay" };
        _thread.Start();
    }

    void Pump()
    {
        try
        {
            var buffer = new byte[PacketFrames.MaxIpLength];
            var clock = DateTime.UtcNow;
            while (!_stop)
            {
                bool outbound;
                DateTime time;
                int length;
                if (_legacy)
                {
                    if (!PacketFrames.TryReadLegacy(_file!, buffer, out outbound, out length)) break;
                    time = clock.AddMilliseconds(Frames);
                }
                else if (!PacketFrames.TryRead(_file!, buffer, out outbound, out time, out length)) break;

                Frames++;
                if (outbound) OutboundFrames++;
                else InboundFrames++;
                Feed(buffer.AsSpan(0, length), outbound, time);
            }

            _done.TrySetResult();
        }
        catch (Exception ex)
        {
            _done.TrySetException(ex);
        }
    }

    public override void Stop()
    {
        _stop = true;
        base.Stop();
        _file?.Dispose();
        _file = null;
    }
}
