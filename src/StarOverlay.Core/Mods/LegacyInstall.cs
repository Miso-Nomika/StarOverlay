using System.Text;

namespace StarOverlay.Core.Mods;

/// <summary>
/// What the old installers left in a game folder (the <c>version.dll</c> proxy that started a separate overlay program, and the
/// copies of the framework and the host under <c>bpsr\</c>). The in-game loader makes them useless, so installing it clears them.
/// Only things we made are removed: a <c>version.dll</c> counts as ours only when it carries our marker.
/// </summary>
public static class LegacyInstall
{
    /// <summary>The old proxy wrote this text at byte 0x40 of its <c>version.dll</c>.</summary>
    public const string ProxyMarker = "BPSR-SL";

    public static bool IsOurProxy(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var marker = Encoding.ASCII.GetBytes(ProxyMarker);
            var buffer = new byte[marker.Length];
            using var stream = File.OpenRead(path);
            if (stream.Length < 0x40 + marker.Length) return false;
            stream.Position = 0x40;
            stream.ReadExactly(buffer);
            return buffer.AsSpan().SequenceEqual(marker);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The old things found in the game folder, as paths relative to it (what <see cref="Clean"/> would remove).</summary>
    public static IReadOnlyList<string> Find(string game)
    {
        var found = new List<string>();
        var proxy = Path.Combine(game, "version.dll");
        if (IsOurProxy(proxy))
        {
            found.Add("version.dll");
            if (File.Exists(Path.Combine(game, "version_orig.dll"))) found.Add("version_orig.dll");
        }

        foreach (var folder in new[] { "host", "framework" })
        {
            if (Directory.Exists(Path.Combine(ModLayout.Root(game), folder))) found.Add(Path.Combine(ModLayout.RootFolder, folder));
        }

        return found;
    }

    /// <summary>Removes the old proxy and the old folders. Returns what was removed (relative paths).</summary>
    public static IReadOnlyList<string> Clean(string game)
    {
        var removed = new List<string>();
        foreach (var relative in Find(game))
        {
            var path = Path.Combine(game, relative);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
            removed.Add(relative);
        }

        return removed;
    }
}
