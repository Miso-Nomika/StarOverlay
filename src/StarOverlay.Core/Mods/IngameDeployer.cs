using System.Diagnostics;
using System.Text.Json;

namespace StarOverlay.Core.Mods;

public enum IngameError
{
    GameDirectoryMissing,
    PackageMissing,
    GameRunning,
    ForeignLoader,
    CopyFailed,
}

public enum IngameState
{
    NotInstalled,
    InstalledOn,
    InstalledOff,

    /// <summary>Installed with an older package build; installing again updates it.</summary>
    Outdated,
}

public sealed record IngameResult(bool Success, IngameError? Error, string Message);

public sealed class IngameManifest
{
    public string PackageId { get; set; } = "";

    public string HostExe { get; set; } = "";

    public DateTime InstalledAt { get; set; }

    /// <summary>Every file the deployer wrote, relative to the game folder.</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>Folders the deployer created, relative to the game folder (removed again only when empty).</summary>
    public List<string> Directories { get; set; } = [];

    /// <summary>Files that an older proxy install left and the deployer removed (for the record).</summary>
    public List<string> Removed { get; set; } = [];
}

/// <summary>
/// Puts the in-game loader into a game folder and takes it out again. Everything it writes is listed in
/// <c>&lt;game&gt;\bpsr\ingame-manifest.json</c>; removing deletes exactly those files plus the loader's own generated
/// folder, so the game folder ends up as it was before. Existing files are never overwritten, and a
/// <c>winhttp.dll</c> that is not ours stops the install.
/// </summary>
public static class IngameDeployer
{
    public const string ManifestName = "ingame-manifest.json";
    public const string ServiceName = "service.json";
    public const string OffFileName = "ingame.off";
    public const string LoaderFolder = "BepInEx";
    public const string AgentFolder = "StarOverlay";
    public const string DoorstopConfig = "doorstop_config.ini";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ManifestPath(string game) => Path.Combine(ModLayout.Root(game), ManifestName);

    public static string ServicePath(string game) => Path.Combine(ModLayout.Root(game), ServiceName);

    public static IngameManifest? ReadManifest(string game)
    {
        try
        {
            var path = ManifestPath(game);
            return File.Exists(path) ? JsonSerializer.Deserialize<IngameManifest>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The package folder layout: bepinex\ (loader files), agent\ (our plugin), fonts\, unity-libs\ (optional), package.id.</summary>
    public static string? PackageId(string packageDir)
    {
        var path = Path.Combine(packageDir, "package.id");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    public static IngameState State(string game, string? packageDir = null)
    {
        var manifest = ReadManifest(game);
        if (manifest is null || !File.Exists(Path.Combine(game, "winhttp.dll"))) return IngameState.NotInstalled;
        if (packageDir is not null && PackageId(packageDir) is { } id && id != manifest.PackageId) return IngameState.Outdated;
        return IsEnabled(game) ? IngameState.InstalledOn : IngameState.InstalledOff;
    }

    public static bool IsEnabled(string game)
    {
        var path = Path.Combine(game, DoorstopConfig);
        if (!File.Exists(path)) return false;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("enabled", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("enabled_", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase))
                return parts[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Turns the loader on or off without removing it (doorstop <c>enabled</c>).</summary>
    public static bool SetEnabled(string game, bool enabled)
    {
        var path = Path.Combine(game, DoorstopConfig);
        if (ReadManifest(game) is null || !File.Exists(path)) return false;
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("enabled", StringComparison.OrdinalIgnoreCase) && trimmed.Split('=', 2) is [var key, _] && key.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "enabled = " + (enabled ? "true" : "false");
                File.WriteAllLines(path, lines);
                return true;
            }
        }

        return false;
    }

    public static bool IsGameRunning(string game)
    {
        foreach (var name in new[] { "StarASIA", "BPSR", "BPSR_STEAM" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var file = process.MainModule?.FileName;
                    if (file is null) return true; // cannot tell: assume it is this game
                    if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(file)!), Path.TrimEndingDirectorySeparator(Path.GetFullPath(game)), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IngameResult Install(string game, string packageDir, string hostExe, Func<string, bool>? isGameRunning = null)
    {
        if (!Directory.Exists(game)) return Fail(IngameError.GameDirectoryMissing, "게임 폴더를 찾지 못했습니다.");
        var loader = Path.Combine(packageDir, "bepinex");
        var agent = Path.Combine(packageDir, "agent");
        if (!Directory.Exists(loader) || !Directory.Exists(agent) || !File.Exists(Path.Combine(loader, "winhttp.dll")))
            return Fail(IngameError.PackageMissing, "인게임 설치 파일을 찾지 못했습니다.");
        if ((isGameRunning ?? IsGameRunning)(game)) return Fail(IngameError.GameRunning, "게임이 실행 중입니다. 게임을 끈 뒤 다시 시도하세요.");

        var previous = ReadManifest(game);
        var theirWinHttp = Path.Combine(game, "winhttp.dll");
        if (File.Exists(theirWinHttp) && previous is null)
            return Fail(IngameError.ForeignLoader, "게임 폴더에 다른 프로그램의 winhttp.dll이 있어 설치하지 않았습니다.");

        var manifest = new IngameManifest { PackageId = PackageId(packageDir) ?? "", HostExe = hostExe, InstalledAt = DateTime.UtcNow };
        var bpsrExisted = Directory.Exists(ModLayout.Root(game));
        try
        {
            // what the old installers left (our own version.dll proxy, bpsr\host, bpsr\framework) is cleared; anything else named version.dll stays
            foreach (var removed in LegacyInstall.Clean(game)) manifest.Removed.Add(removed);

            // an update of our own earlier install: take the old files out first so nothing stale stays behind
            if (previous is not null) RemoveFiles(game, previous);

            Directory.CreateDirectory(ModLayout.Root(game));
            var created = new List<string>();
            if (!Directory.Exists(Path.Combine(game, "bpsr"))) created.Add("bpsr");
            Copy(loader, game, game, manifest);
            Copy(agent, Path.Combine(game, LoaderFolder, "plugins", AgentFolder), game, manifest);
            var fonts = Path.Combine(packageDir, "fonts");
            if (Directory.Exists(fonts)) Copy(fonts, Path.Combine(game, LoaderFolder, "plugins", AgentFolder, "fonts"), game, manifest);
            var icons = Path.Combine(packageDir, "icons");
            if (Directory.Exists(icons)) Copy(icons, Path.Combine(game, LoaderFolder, "plugins", AgentFolder, "icons"), game, manifest);
            var unityLibs = Path.Combine(packageDir, "unity-libs");
            if (Directory.Exists(unityLibs)) Copy(unityLibs, Path.Combine(game, LoaderFolder, "unity-libs"), game, manifest);

            WriteText(Path.Combine(game, LoaderFolder, "config", "StarOverlay.cfg.note"), "Written by Star Overlay.\n", game, manifest);
            WriteText(Path.Combine(game, LoaderFolder, "config", "BepInEx.cfg"), "[Logging.Console]\nEnabled = false\n\n[Logging.Disk]\nEnabled = true\nWriteUnityLog = false\n", game, manifest);
            WriteText(ServicePath(game), JsonSerializer.Serialize(new { host = hostExe, package = manifest.PackageId }, Json), game, manifest);
            foreach (var directory in created) manifest.Directories.Add(directory);
            File.WriteAllText(ManifestPath(game), JsonSerializer.Serialize(manifest, Json));
            return new IngameResult(true, null, "인게임 표시를 설치했습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // leave nothing half done
            try
            {
                RemoveFiles(game, manifest);
                var bpsr = ModLayout.Root(game);
                if (!bpsrExisted && Directory.Exists(bpsr) && !Directory.EnumerateFileSystemEntries(bpsr).Any()) Directory.Delete(bpsr);
            }
            catch (Exception)
            {
            }

            return Fail(IngameError.CopyFailed, "파일을 복사하지 못했습니다: " + ex.Message);
        }
    }

    public static IngameResult Uninstall(string game, Func<string, bool>? isGameRunning = null)
    {
        if (!Directory.Exists(game)) return Fail(IngameError.GameDirectoryMissing, "게임 폴더를 찾지 못했습니다.");
        if ((isGameRunning ?? IsGameRunning)(game)) return Fail(IngameError.GameRunning, "게임이 실행 중입니다. 게임을 끈 뒤 다시 시도하세요.");
        var manifest = ReadManifest(game);
        if (manifest is null) return new IngameResult(true, null, "설치된 인게임 표시가 없습니다.");
        try
        {
            RemoveFiles(game, manifest);
            // BepInEx wrote its own folders (interop, cache, logs ...) during play: the whole loader folder goes
            var loaderDir = Path.Combine(game, LoaderFolder);
            if (Directory.Exists(loaderDir)) Directory.Delete(loaderDir, recursive: true);
            File.Delete(ManifestPath(game));
            var bpsr = ModLayout.Root(game);
            if (Directory.Exists(bpsr) && !Directory.EnumerateFileSystemEntries(bpsr).Any()) Directory.Delete(bpsr);
            return new IngameResult(true, null, "인게임 표시를 제거했습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(IngameError.CopyFailed, "파일을 지우지 못했습니다: " + ex.Message);
        }
    }

    /// <summary>Creates or removes the file that makes the agent do nothing (same effect as doorstop off, without touching the loader).</summary>
    public static void SetAgentOff(string game, bool off)
    {
        var path = Path.Combine(ModLayout.Root(game), OffFileName);
        if (off) File.WriteAllText(path, "");
        else File.Delete(path);
    }

    static void RemoveFiles(string game, IngameManifest manifest)
    {
        foreach (var relative in manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(game, relative));
            if (!path.StartsWith(Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(path)) File.Delete(path);
        }

        // folders we created, deepest first, only when they ended up empty ("bpsr" is handled by Uninstall)
        var folders = manifest.Directories
            .Concat(manifest.Files.Select(Path.GetDirectoryName).Where(d => !string.IsNullOrEmpty(d)).Select(d => d!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(d => d.Length);
        foreach (var relative in folders)
        {
            if (string.Equals(relative, "bpsr", StringComparison.OrdinalIgnoreCase)) continue;
            var directory = Path.Combine(game, relative);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    static void Copy(string source, string destination, string game, IngameManifest manifest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            if (File.Exists(target)) throw new IOException("이미 있는 파일을 덮어쓰지 않습니다: " + Path.GetRelativePath(game, target));
            NoteDirectories(Path.GetDirectoryName(target)!, game, manifest);
            File.Copy(file, target);
            manifest.Files.Add(Path.GetRelativePath(game, target));
        }
    }

    static void WriteText(string path, string text, string game, IngameManifest manifest)
    {
        if (File.Exists(path))
        {
            if (!manifest.Files.Contains(Path.GetRelativePath(game, path))) return; // someone else's file stays
        }

        NoteDirectories(Path.GetDirectoryName(path)!, game, manifest);
        File.WriteAllText(path, text);
        var relative = Path.GetRelativePath(game, path);
        if (!manifest.Files.Contains(relative)) manifest.Files.Add(relative);
    }

    static void NoteDirectories(string directory, string game, IngameManifest manifest)
    {
        var missing = new List<string>();
        var current = directory;
        while (!Directory.Exists(current) && Path.GetFullPath(current).Length > Path.GetFullPath(game).Length)
        {
            missing.Add(Path.GetRelativePath(game, current));
            current = Path.GetDirectoryName(current)!;
        }

        Directory.CreateDirectory(directory);
        foreach (var relative in missing)
        {
            if (!manifest.Directories.Contains(relative)) manifest.Directories.Add(relative);
        }
    }

    static IngameResult Fail(IngameError error, string message) => new(false, error, message);
}
