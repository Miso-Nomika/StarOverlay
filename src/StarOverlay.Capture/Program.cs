using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using StarOverlay.Game.Packets;

namespace StarOverlay.Capture;

/// <summary>
/// Raw-socket helper: receives the game's TCP packets without Npcap and sends them to the Host through a named pipe.
/// It needs an administrator token (SIO_RCVALL). It accepts no commands; it ends when the pipe closes or the game exits.
/// Usage: StarOverlay.Capture --pipe &lt;name&gt; [--pid &lt;game pid&gt;] [--wait &lt;seconds&gt;]
/// </summary>
internal static class Program
{
    const int ExitOk = 0;
    const int ExitUsage = 2;
    const int ExitNoGame = 3;
    const int ExitNoPipe = 4;
    const int ExitNoAccess = 5;
    const int ExitFailed = 6;

    static readonly string[] GameNames = ["StarASIA", "StarASIA_STEAM", "BPSR", "BPSR_STEAM"]; // the Steam version's exe is StarASIA_STEAM.exe

    static int Main(string[] args)
    {
        string? pipeName = null;
        int pid = 0, waitSeconds = 60;
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--pipe": pipeName = args[i + 1]; break;
                case "--pid": int.TryParse(args[i + 1], out pid); break;
                case "--wait": int.TryParse(args[i + 1], out waitSeconds); break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName)) return ExitUsage;
        try
        {
            return Run(pipeName, pid, waitSeconds);
        }
        catch (Exception ex)
        {
            return ExitFailed;
        }
    }

    const int MaxPendingPerFlow = 4096; // a map entry sends a lot at once (every player nearby): losing its start breaks the whole stream
    const int MaxPendingFlows = 256;
    const int RecheckMilliseconds = 100;
    const double PendingSeconds = 5;

    static int Run(string pipeName, int pid, int waitSeconds)
    {
        var game = WaitForGame(pid, waitSeconds);
        if (game == null)
        {
            return ExitNoGame;
        }

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
        if (!ConnectPipe(pipe, waitSeconds))
        {
            return ExitNoPipe;
        }

        var output = new BufferedStream(pipe, 64 * 1024);
        var tables = new TcpTable();
        var inbound = new HashSet<ulong>();
        var outbound = new HashSet<ulong>();
        // unknown connections: their packets are kept per connection, so one busy foreign download cannot push a new game connection out
        var pending = new Dictionary<ulong, (Queue<(DateTime At, bool Out, byte[] Ip)> Packets, DateTime Last)>();
        var lastFlush = System.Diagnostics.Stopwatch.StartNew();
        long sentIn = 0, sentOut = 0, skipped = 0;

        // Packets are written at once and pushed to the pipe within a few milliseconds. Flushing only when the
        // network is idle (the old behaviour) held game data back for seconds on a busy PC.
        void WriteGame(bool isOut, DateTime at, ReadOnlySpan<byte> ip)
        {
            PacketFrames.Write(output, isOut, at, ip);
            if (lastFlush.ElapsedMilliseconds >= 5)
            {
                output.Flush();
                lastFlush.Restart();
            }
        }

        // Packets seen before their connection showed up in the table: send those that belong to the game now (in arrival order).
        void ReleasePending(DateTime now)
        {
            List<ulong>? done = null;
            foreach (var (key, flow) in pending)
            {
                if (inbound.Contains(key) || outbound.Contains(key))
                {
                    foreach (var (at, isOut, ip) in flow.Packets)
                    {
                        WriteGame(isOut, at, ip);
                        if (isOut) sentOut++;
                        else sentIn++;
                    }

                    (done ??= []).Add(key);
                }
                else if ((now - flow.Last).TotalSeconds > PendingSeconds)
                {
                    (done ??= []).Add(key);
                }
            }

            if (done != null)
                foreach (var key in done) pending.Remove(key);
        }
        Socket? socket = null;
        uint localIp = 0;
        var buffer = new byte[PacketFrames.MaxIpLength];
        var nextRefresh = DateTime.MinValue;
        var nextReport = DateTime.UtcNow.AddSeconds(30);

        try
        {
            while (true)
            {
                var now = DateTime.UtcNow;
                if (now >= nextRefresh)
                {
                    nextRefresh = now.AddSeconds(1);
                    if (game.HasExited)
                    {
                        return ExitOk;
                    }

                    var rows = tables.Read(game.Id);
                    var wanted = BestLocalIp(rows);
                    if (wanted != 0 && wanted != localIp)
                    {
                        socket?.Dispose();
                        socket = OpenRawSocket(wanted);
                        if (socket == null) return ExitNoAccess;
                        localIp = wanted;
                    }

                    inbound.Clear();
                    outbound.Clear();
                    foreach (var row in rows)
                    {
                        if (row.LocalAddr != localIp) continue;
                        // key: local port | remote ip | remote port, same for both directions
                        var key = ((ulong)row.LocalPort << 48) | ((ulong)row.RemoteAddr << 16) | row.RemotePort;
                        inbound.Add(key);
                        outbound.Add(key);
                    }

                    ReleasePending(now);

                    if (now >= nextReport)
                    {
                        nextReport = now.AddSeconds(30);
                    }
                }

                if (socket == null)
                {
                    Thread.Sleep(200);
                    continue;
                }

                if (!socket.Poll(100_000, SelectMode.SelectRead))
                {
                    output.Flush();
                    continue;
                }

                var length = socket.Receive(buffer);
                if (length < 40 || (buffer[0] >> 4) != 4 || buffer[9] != 6) continue;
                var ihl = (buffer[0] & 15) * 4;
                if (length < ihl + 20) continue;
                var src = BitConverter.ToUInt32(buffer, 12);
                var dst = BitConverter.ToUInt32(buffer, 16);
                var srcPort = (ushort)((buffer[ihl] << 8) | buffer[ihl + 1]);
                var dstPort = (ushort)((buffer[ihl + 2] << 8) | buffer[ihl + 3]);

                bool isOut;
                if (src == localIp && outbound.Contains(((ulong)srcPort << 48) | ((ulong)dst << 16) | dstPort)) isOut = true;
                else if (dst == localIp && inbound.Contains(((ulong)dstPort << 48) | ((ulong)src << 16) | srcPort)) isOut = false;
                else
                {
                    // Maybe the first packets of a connection the game just opened (a map change opens a new one):
                    // keep them briefly and look at the connection table again at once instead of dropping them.
                    skipped++;
                    if ((src == localIp || dst == localIp) && length <= 2048)
                    {
                        var outgoing = src == localIp;
                        var key = outgoing ? ((ulong)srcPort << 48) | ((ulong)dst << 16) | dstPort : ((ulong)dstPort << 48) | ((ulong)src << 16) | srcPort;
                        if (!pending.TryGetValue(key, out var flow))
                        {
                            if (pending.Count >= MaxPendingFlows)
                            {
                                var oldest = pending.OrderBy(pair => pair.Value.Last).First().Key;
                                pending.Remove(oldest);
                            }

                            flow = (new Queue<(DateTime At, bool Out, byte[] Ip)>(), now);
                        }

                        if (flow.Packets.Count >= MaxPendingPerFlow) flow.Packets.Dequeue();
                        flow.Packets.Enqueue((now, outgoing, buffer.AsSpan(0, length).ToArray()));
                        pending[key] = (flow.Packets, now);
                        if (nextRefresh > now.AddMilliseconds(RecheckMilliseconds)) nextRefresh = now.AddMilliseconds(RecheckMilliseconds);
                    }

                    continue;
                }

                WriteGame(isOut, DateTime.UtcNow, buffer.AsSpan(0, length));
                if (isOut) sentOut++;
                else sentIn++;
            }
        }
        catch (IOException)
        {
            return ExitOk;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    static Process? WaitForGame(int pid, int waitSeconds)
    {
        var until = DateTime.UtcNow.AddSeconds(Math.Max(waitSeconds, 5));
        do
        {
            try
            {
                if (pid != 0) return Process.GetProcessById(pid);
                foreach (var name in GameNames)
                {
                    var found = Process.GetProcessesByName(name);
                    if (found.Length > 0) return found[0];
                }
            }
            catch (ArgumentException)
            {
            }

            Thread.Sleep(1000);
        } while (DateTime.UtcNow < until);

        return null;
    }

    static bool ConnectPipe(NamedPipeClientStream pipe, int waitSeconds)
    {
        var until = DateTime.UtcNow.AddSeconds(Math.Max(waitSeconds, 5));
        while (DateTime.UtcNow < until)
        {
            try
            {
                pipe.Connect(1000);
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    static Socket? OpenRawSocket(uint localIp)
    {
        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            socket.Bind(new IPEndPoint(new IPAddress(BitConverter.GetBytes(localIp)), 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            socket.ReceiveBufferSize = 4 * 1024 * 1024;
            socket.IOControl(IOControlCode.ReceiveAll, [1, 0, 0, 0], [0, 0, 0, 0]);
            return socket;
        }
        catch (SocketException ex)
        {
            return null;
        }
    }

    /// <summary>The local address most of the game's connections use (VPN adapters can add others; one interface is captured).</summary>
    static uint BestLocalIp(List<TcpRow> rows)
    {
        uint best = 0;
        var bestCount = 0;
        var counts = new Dictionary<uint, int>();
        foreach (var row in rows)
        {
            counts.TryGetValue(row.LocalAddr, out var count);
            counts[row.LocalAddr] = ++count;
            if (count > bestCount)
            {
                best = row.LocalAddr;
                bestCount = count;
            }
        }

        return best;
    }
}

internal readonly record struct TcpRow(uint LocalAddr, ushort LocalPort, uint RemoteAddr, ushort RemotePort);

/// <summary>Established, non-loopback IPv4 connections owned by one process (GetExtendedTcpTable).</summary>
internal sealed class TcpTable
{
    const int AfInet = 2;
    const int TcpTableOwnerPidAll = 5;
    const int Established = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern int GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, int reserved);

    IntPtr _buffer = IntPtr.Zero;
    int _capacity;

    public List<TcpRow> Read(int pid)
    {
        var rows = new List<TcpRow>();
        var size = _capacity;
        var result = GetExtendedTcpTable(_buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        if (result == 122) // ERROR_INSUFFICIENT_BUFFER
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _capacity = size + 4096;
            _buffer = Marshal.AllocHGlobal(_capacity);
            size = _capacity;
            result = GetExtendedTcpTable(_buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        }

        if (result != 0 || _buffer == IntPtr.Zero) return rows;
        var count = Marshal.ReadInt32(_buffer);
        for (var i = 0; i < count; i++)
        {
            var row = _buffer + 4 + i * 24;
            if (Marshal.ReadInt32(row, 20) != pid) continue;
            if (Marshal.ReadInt32(row, 0) != Established) continue;
            var local = (uint)Marshal.ReadInt32(row, 4);
            var remote = (uint)Marshal.ReadInt32(row, 12);
            if ((local & 0xFF) == 127 || (remote & 0xFF) == 127) continue; // loopback
            rows.Add(new TcpRow(local, Port(Marshal.ReadInt32(row, 8)), remote, Port(Marshal.ReadInt32(row, 16))));
        }

        return rows;
    }

    static ushort Port(int raw) => (ushort)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
}
