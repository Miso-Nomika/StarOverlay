using StarOverlay.Core.Model;
using StarOverlay.Core.Platform;

namespace StarOverlay.Core.Mods;

public static class ModLayout
{
    public const string RootFolder = "bpsr";

    public static string Root(string gameDirectory) => Path.Combine(gameDirectory, RootFolder);




    public static string PluginsDirectory(string gameDirectory) => Path.Combine(Root(gameDirectory), "plugins");

    public static string DisabledDirectory(string gameDirectory) => Path.Combine(Root(gameDirectory), "plugins-disabled");

    public static bool IsInsideRoot(string gameDirectory, string candidate)
    {
        if (!AbsolutePath.TryNormalize(gameDirectory, out var game, out _)) return false;
        if (!AbsolutePath.TryNormalize(candidate, out var full, out _)) return false;
        var root = Path.Combine(game, RootFolder);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = HostPlatform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.StartsWith(prefix, comparison);
    }
}
