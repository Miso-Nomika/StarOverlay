using System.Text;
using StarOverlay.Core;

namespace StarOverlay.Game.Packets;

/// <summary>
/// Writes every packet of an external source to local/captures (or %LOCALAPPDATA%\StarOverlay\captures) in the
/// <see cref="PacketFrames"/> format, so a play session can be replayed in tests.
/// Only on when the environment variable STAROVERLAY_RECORD=1.
/// </summary>
public sealed class PacketRecorder : IDisposable
{
    readonly FileStream _file;
    readonly ExternalPacketSource _source;
    readonly object _gate = new();

    PacketRecorder(ExternalPacketSource source, string path)
    {
        _source = source;
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _source.PacketReceived += OnPacket;
        Path = path;
    }

    public string Path { get; }

    public static bool Enabled => Environment.GetEnvironmentVariable("STAROVERLAY_RECORD") == "1";

    public static PacketRecorder? TryStart(ExternalPacketSource source)
    {
        if (!Enabled) return null;
        try
        {
            var folder = System.IO.Path.Combine(AppStorage.Root(), "captures");
            Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".frames");
            return new PacketRecorder(source, path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    void OnPacket(ReadOnlyMemory<byte> ip, bool outbound, DateTime utcTime)
    {
        lock (_gate)
        {
            try
            {
                PacketFrames.Write(_file, outbound, utcTime, ip.Span);
            }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        _source.PacketReceived -= OnPacket;
        lock (_gate)
        {
            _file.Dispose();
        }
    }
}
