namespace StarOverlay.Game.Packets;

/// <summary>
/// Frame format shared by the raw-socket helper (Capture.exe), the packet recorder and the replay source.
/// One frame: [length 4B little endian][direction 1B: 1 inbound, 2 outbound][UTC ticks 8B little endian][IPv4 packet].
/// length = 1 + 8 + IP packet size. This file has no dependencies so Capture.exe links it as source.
/// </summary>
public static class PacketFrames
{
    public const int MaxIpLength = 65535;
    public const byte Inbound = 1;
    public const byte Outbound = 2;

    public static void Write(Stream stream, bool outbound, DateTime utcTime, ReadOnlySpan<byte> ip)
    {
        Span<byte> header = stackalloc byte[13];
        BitConverter.TryWriteBytes(header[..4], 9 + ip.Length);
        header[4] = outbound ? Outbound : Inbound;
        BitConverter.TryWriteBytes(header[5..], utcTime.ToUniversalTime().Ticks);
        stream.Write(header);
        stream.Write(ip);
    }

    /// <summary>Reads one frame. Returns false at end of stream. buffer must hold at least MaxIpLength bytes.</summary>
    public static bool TryRead(Stream stream, byte[] buffer, out bool outbound, out DateTime utcTime, out int ipLength)
    {
        outbound = false;
        utcTime = DateTime.MinValue;
        ipLength = 0;
        Span<byte> header = stackalloc byte[13];
        if (!ReadFully(stream, header)) return false;
        var length = BitConverter.ToInt32(header[..4]);
        if (length < 9 || length - 9 > MaxIpLength) throw new InvalidDataException("bad frame length " + length);
        outbound = header[4] == Outbound;
        utcTime = new DateTime(BitConverter.ToInt64(header[5..]), DateTimeKind.Utc);
        ipLength = length - 9;
        return ReadFully(stream, buffer.AsSpan(0, ipLength));
    }

    /// <summary>
    /// Reads the older experiment dump from T36: [direction 1B][length 4B little endian][IPv4 packet], no time.
    /// The caller supplies the time stamp.
    /// </summary>
    public static bool TryReadLegacy(Stream stream, byte[] buffer, out bool outbound, out int ipLength)
    {
        outbound = false;
        ipLength = 0;
        Span<byte> header = stackalloc byte[5];
        if (!ReadFully(stream, header)) return false;
        var length = BitConverter.ToInt32(header[1..]);
        if (length < 20 || length > MaxIpLength) throw new InvalidDataException("bad legacy frame length " + length);
        outbound = header[0] == Outbound;
        ipLength = length;
        return ReadFully(stream, buffer.AsSpan(0, ipLength));
    }

    static bool ReadFully(Stream stream, Span<byte> target)
    {
        var done = 0;
        while (done < target.Length)
        {
            var read = stream.Read(target[done..]);
            if (read <= 0) return false;
            done += read;
        }

        return true;
    }
}
