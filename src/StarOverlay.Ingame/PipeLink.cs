using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Ingame;

/// <summary>Pipe client to the Host service. Reads on its own thread; the game thread drains <see cref="Inbox"/>.</summary>
internal sealed class PipeLink
{
    readonly ConcurrentQueue<Envelope> _inbox = new();
    readonly object _write = new();
    StreamWriter _writer;
    volatile bool _connected;
    Thread _thread;

    public bool Connected => _connected;

    public int Received;

    public int Unreadable;

    public bool TryDequeue(out Envelope message) => _inbox.TryDequeue(out message);

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "staroverlay-pipe" };
        _thread.Start();
    }

    public void Send(Envelope message)
    {
        lock (_write)
        {
            try
            {
                _writer?.WriteLine(ProtocolJson.Write(message));
            }
            catch (Exception)
            {
                // the read loop notices a broken pipe
            }
        }
    }

    void Loop()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        var name = Protocol.Protocol.PipeName(sid);
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(2000);
                var reader = new StreamReader(pipe, new UTF8Encoding(false));
                lock (_write)
                {
                    _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                }

                _connected = true;
                Plugin.L.LogInfo("service connected");
                // from another thread: this thread must be reading while the hello goes out
                var hello = new Envelope { T = MessageType.Hello, V = Protocol.Protocol.Version, Agent = "ingame", GameHash = GameHash(), Unity = UnityEngine.Application.unityVersion };
                ThreadPool.QueueUserWorkItem(_ => Send(hello));
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var message = ProtocolJson.Read(line);
                    Received++;
                    if (message != null) _inbox.Enqueue(message);
                    else Unreadable++;
                }
            }
            catch (TimeoutException)
            {
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("pipe: " + e.Message);
            }
            finally
            {
                if (_connected) Plugin.L.LogInfo("service disconnected");
                _connected = false;
                lock (_write) _writer = null;
            }

            Thread.Sleep(2000);
        }
    }

    internal static string GameHash()
    {
        try
        {
            var file = Path.Combine(Plugin.GameRoot, "GameAssembly.dll");
            var info = new FileInfo(file);
            return info.Length + "-" + info.LastWriteTimeUtc.Ticks;
        }
        catch (Exception)
        {
            return "";
        }
    }
}
