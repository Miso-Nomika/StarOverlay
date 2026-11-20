using StarOverlay.Core.Platform;

namespace StarOverlay.Core.Clients;

public sealed class GameInstallFinder : IGameInstallFinder
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "game_mini", "game", "StarLauncher", "Star", "Programs", "Local", "AppData",
    };

    private readonly IInstallFileSystem _files;
    private readonly IReadOnlyList<string> _roots;

    public GameInstallFinder(IInstallFileSystem files, IReadOnlyList<string> roots)
    {
        _files = files;
        _roots = roots;
    }

    public IReadOnlyList<DiscoveredInstall> Find()
    {
        var found = new List<DiscoveredInstall>();
        var seen = new HashSet<string>(HostPlatform.PathComparer);
        foreach (var root in _roots)
        {
            Collect(root, seen, found);
            foreach (var child in _files.ListDirectories(root))
                Collect(child, seen, found);
        }

        return found;
    }

    public DiscoveredInstall? Resolve(string folder)
    {
        if (!AbsolutePath.TryNormalize(folder, out var normalized, out _))
            return null;

        var direct = Locate(normalized);
        if (direct is not null)
            return Describe(direct);

        foreach (var child in _files.ListDirectories(normalized))
        {
            var nested = Locate(child);
            if (nested is not null)
                return Describe(nested);
        }

        return null;
    }

    private void Collect(string directory, HashSet<string> seen, List<DiscoveredInstall> found)
    {
        var install = Locate(directory);
        if (install is null) return;
        if (!AbsolutePath.TryNormalize(install, out var normalized, out _))
            normalized = install;
        if (!seen.Add(normalized)) return;
        found.Add(Describe(normalized));
    }

    private DiscoveredInstall Describe(string path) => new(path, SuggestName(path));

    private string? Locate(string directory)
    {
        if (!_files.DirectoryExists(directory)) return null;
        if (IsGameDirectory(directory)) return directory;

        foreach (var gameRoot in new[]
        {
            Path.Combine(directory, "StarLauncher", "game"),
            Path.Combine(directory, "Star", "StarLauncher", "game"),
            Path.Combine(directory, "game"),
        })
        {
            var mini = NewestGameMini(gameRoot);
            if (mini is not null) return mini;
        }

        return null;
    }

    private string? NewestGameMini(string gameRoot)
    {
        if (!_files.DirectoryExists(gameRoot)) return null;

        string? best = null;
        var bestVersion = new Version(0, 0);
        foreach (var release in _files.ListDirectories(gameRoot))
        {
            var name = Path.GetFileName(release);
            if (!name.StartsWith("release_", StringComparison.OrdinalIgnoreCase)) continue;

            var mini = Path.Combine(release, "game_mini");
            if (!IsGameDirectory(mini)) continue;

            var version = ReleaseVersion(name);
            if (best is null || version > bestVersion)
            {
                best = mini;
                bestVersion = version;
            }
        }

        return best;
    }

    private bool IsGameDirectory(string directory)
    {
        if (!_files.FileExists(Path.Combine(directory, "GameAssembly.dll"))) return false;

        foreach (var child in _files.ListDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith("Star", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string SuggestName(string installPath)
    {
        var current = installPath;
        for (var depth = 0; depth < 8; depth++)
        {
            var trimmed = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            var parent = Path.GetDirectoryName(trimmed);
            var generic = string.IsNullOrEmpty(name)
                || GenericNames.Contains(name)
                || name.StartsWith("release_", StringComparison.OrdinalIgnoreCase);
            if (!generic)
            {
                var profile = Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                if (!string.IsNullOrEmpty(profile) && name.Equals(profile, StringComparison.OrdinalIgnoreCase))
                    return "StarLauncher";
                return name;
            }

            if (string.IsNullOrEmpty(parent) || HostPlatform.PathComparer.Equals(parent, trimmed))
                break;
            current = parent;
        }

        return "게임";
    }

    private static Version ReleaseVersion(string directoryName)
    {
        var raw = directoryName["release_".Length..];
        return Version.TryParse(raw, out var version) ? version : new Version(0, 0);
    }
}
