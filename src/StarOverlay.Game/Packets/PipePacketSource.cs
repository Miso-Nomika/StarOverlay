using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace StarOverlay.Game.Packets;

/// <summary>
/// Receives frames from the raw-socket helper (StarOverlay.Capture.exe) over a named pipe. The pipe only accepts
/// the current Windows user. The helper is the client and reconnects on its own; this side is the server.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PipePacketSource : ExternalPacketSource
{
    readonly string _pipeName;
    readonly TimeSpan _connectTimeout;
    NamedPipeServerStream? _server;
    Thread? _thread;
    volatile bool _stop;

    readonly bool _requireHelperProcess;

    public PipePacketSource(string? pipeName, TimeSpan? connectTimeout = null, bool requireHelperProcess = true)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        _requireHelperProcess = requireHelperProcess;
    }

    public override string Name => "pipe";
    public override string Label => _pipeName;

    /// <summary>Per-user and per-game pipe name: two Windows accounts never share a pipe, and two clients each get their own.</summary>
    public static string DefaultPipeName(string gameFolder)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        return CapturePipe.Name("StarOverlay.Capture." + sid, gameFolder);
    }

    NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    protected override void Open()
    {
        // No helper process, no point waiting: fall through to the next source right away.
        if (_requireHelperProcess && System.Diagnostics.Process.GetProcessesByName("StarOverlay.Capture").Length == 0)
            throw new InvalidOperationException("수신 도우미(StarOverlay.Capture)가 실행 중이 아닙니다.");

        _server = CreateServer();
        var wait = _server.WaitForConnectionAsync();
        if (!wait.Wait(_connectTimeout))
        {
            _server.Dispose();
            _server = null;
            throw new TimeoutException($"수신 도우미(Capture)가 {(int)_connectTimeout.TotalSeconds}초 안에 연결되지 않았습니다.");
        }
    }

    protected override void BeginPump()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "capture-pipe" };
        _thread.Start();
    }

    void Pump()
    {
        var buffer = new byte[PacketFrames.MaxIpLength];
        while (!_stop)
        {
            try
            {
                var server = _server!;
                while (!_stop && PacketFrames.TryRead(server, buffer, out var outbound, out var time, out var length))
                    Feed(buffer.AsSpan(0, length), outbound, time);
            }
            catch (Exception)
            {
                // broken frame or pipe closed: wait for the helper to come back
            }

            if (_stop) break;
            try
            {
                _server?.Dispose();
                _server = CreateServer();
                _server.WaitForConnectionAsync().Wait();
            }
            catch (Exception)
            {
                if (_stop) break;
                Thread.Sleep(1000);
            }
        }
    }

    public override void Stop()
    {
        _stop = true;
        base.Stop();
        try
        {
            _server?.Dispose();
        }
        catch (Exception)
        {
        }

        _server = null;
    }
}
