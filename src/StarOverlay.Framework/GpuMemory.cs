using System.Diagnostics;
using System.Globalization;

namespace StarOverlay.Framework;

/// <summary>
/// Free and total video memory of the first NVIDIA card, read with nvidia-smi (it comes with the driver). It is read only while a
/// settings window is open (<see cref="Watching"/>), at most every three seconds, in the background; otherwise nothing is run.
/// Null for other makes of card or while unknown.
/// </summary>
public static class GpuMemory
{
    public sealed record Reading(double FreeGb, double TotalGb);

    static readonly TimeSpan Every = TimeSpan.FromSeconds(3);
    static readonly object Gate = new();
    static Reading? _last;
    static DateTime _at = DateTime.MinValue;
    static bool _running;
    static volatile bool _watching;

    /// <summary>True while a settings window is open in the game: only then the card is asked.</summary>
    public static bool Watching
    {
        get => _watching;
        set => _watching = value;
    }

    /// <summary>The last reading, or null when nobody is watching. Starts a refresh in the background when it is older than three seconds.</summary>
    public static Reading? Current()
    {
        if (!_watching) return null;
        lock (Gate)
        {
            if (!_running && DateTime.UtcNow - _at >= Every)
            {
                _running = true;
                _at = DateTime.UtcNow;
                _ = Task.Run(Refresh);
            }

            return _last;
        }
    }

    /// <summary>"free MiB, total MiB" as nvidia-smi prints it (several cards: the first line).</summary>
    public static Reading? Parse(string? output)
    {
        var line = (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null) return null;
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var free)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            || total <= 0)
            return null;
        return new Reading(Math.Round(free / 1024, 1), Math.Round(total / 1024, 1));
    }

    static void Refresh()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.free,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return;
            var text = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill();
                return;
            }

            var reading = Parse(text);
            lock (Gate) _last = reading;
        }
        catch (Exception)
        {
            // no NVIDIA driver (or another make of card): there is no reading
        }
        finally
        {
            lock (Gate) _running = false;
        }
    }
}
