using StarOverlay.Core.Mods;

namespace StarOverlay.Core.Setup;

public sealed record InstallReport(bool Success, string Message, IReadOnlyList<string> Done);

/// <summary>
/// "Install" and "remove" as the user sees them: the in-game loader goes into the game folder, the standard plugins go into the
/// mod home (the data folder, kept when the loader is removed), and what the old installers left is cleared.
/// The receiving helper needs one administrator confirmation and is set up separately (see <c>CaptureSetup</c>).
/// </summary>
public static class ModInstaller
{
    /// <summary>The plugins that are switched on after an install, folder name = plugin id.</summary>
    public static readonly string[] StandardPlugins = ["dps.meter", "chat", "party.join", "buff.tracker", "skill.repeat", "quick.preset"];

    public static InstallReport Install(string game, PayloadPaths payload, Func<string, bool>? isGameRunning = null, string? modHome = null)
    {
        if (!payload.IsComplete) return new InstallReport(false, "설치 파일을 찾지 못했습니다.", []);
        var done = new List<string>();
        var home = modHome ?? ModHome.Prepare(game); // also brings over plugins an old install kept in the game folder
        var result = IngameDeployer.Install(game, payload.IngamePackage, payload.HostExe, isGameRunning);
        if (!result.Success) return new InstallReport(false, result.Message, done);
        done.Add("오버레이 설치");

        foreach (var removed in IngameDeployer.ReadManifest(game)?.Removed ?? [])
            done.Add("옛 설치 정리: " + removed);

        var added = AddStandardPlugins(home, payload.Plugins);
        if (added.Count > 0) done.Add("플러그인 추가: " + string.Join(", ", added));
        var settings = AddDefaultConfig(home, payload.ConfigDefaults);
        if (settings.Count > 0) done.Add("기본 설정 추가: " + string.Join(", ", settings));
        return new InstallReport(true, "설치를 마쳤습니다.", done);
    }

    /// <param name="purge">
    /// "전부 삭제": also deletes the game's <c>bpsr</c> folder (what the agent made while the game ran) and whatever another BepInEx
    /// program left in the game folder (see <see cref="LoaderLeftovers"/>), which would otherwise stop a new install.
    /// </param>
    public static InstallReport Uninstall(string game, Func<string, bool>? isGameRunning = null, bool purge = false)
    {
        var result = IngameDeployer.Uninstall(game, isGameRunning);
        if (result.Success && purge)
        {
            try
            {
                var bpsr = ModLayout.Root(game);
                if (Directory.Exists(bpsr)) Directory.Delete(bpsr, recursive: true);
                foreach (var path in LoaderLeftovers(game))
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new InstallReport(false, "게임 폴더의 오버레이·BepInEx 파일을 지우지 못했습니다: " + ex.Message, []);
            }
        }

        return new InstallReport(result.Success, result.Success ? "제거했습니다. 플러그인 설정과 전투 기록은 데이터 폴더에 그대로 있습니다." : result.Message, []);
    }

    /// <summary>
    /// What a BepInEx (Doorstop) install leaves in the game folder, ours or another program's: the loader proxy, its settings, the loader
    /// folder with every plugin in it, and Doorstop's own <c>dotnet</c> runtime folder (only when it holds <c>coreclr.dll</c>).
    /// A game's own files and other kinds of proxies (version.dll ...) are never listed.
    /// </summary>
    public static IReadOnlyList<string> LoaderLeftovers(string game)
    {
        var found = new List<string>();
        foreach (var name in new[] { "winhttp.dll", IngameDeployer.DoorstopConfig, ".doorstop_version", IngameDeployer.LoaderFolder })
        {
            var path = Path.Combine(game, name);
            if (File.Exists(path) || Directory.Exists(path)) found.Add(path);
        }

        var dotnet = Path.Combine(game, "dotnet");
        if (File.Exists(Path.Combine(dotnet, "coreclr.dll"))) found.Add(dotnet);
        return found;
    }

    /// <summary>
    /// The settings a new user starts with (window places, plugin settings): every .json file of <paramref name="defaultsFolder"/> that the
    /// user does not have yet in the config folder is copied there. A file the user already has is never touched.
    /// </summary>
    public static IReadOnlyList<string> AddDefaultConfig(string modHome, string defaultsFolder)
    {
        var done = new List<string>();
        if (!Directory.Exists(defaultsFolder)) return done;
        var config = Path.Combine(ModLayout.Root(modHome), "config");
        foreach (var file in Directory.GetFiles(defaultsFolder, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var target = Path.Combine(config, Path.GetFileName(file));
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(config);
            File.Copy(file, target);
            done.Add(Path.GetFileName(file));
        }

        return done;
    }

    /// <summary>
    /// Puts the standard plugins into the mod home: a missing one is added (switched on), an existing one gets the new build over its
    /// old files (a plugin the user switched off stays off, its settings and records are not touched).
    /// </summary>
    public static IReadOnlyList<string> AddStandardPlugins(string modHome, string pluginsFolder)
    {
        var done = new List<string>();
        if (!Directory.Exists(pluginsFolder)) return done;
        var present = PluginFolders.List(modHome).ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var handled = new List<string>();
        foreach (var id in StandardPlugins)
        {
            var source = Path.Combine(pluginsFolder, id);
            var dll = Directory.Exists(source) ? Directory.GetFiles(source, "*.dll").FirstOrDefault() : null;
            if (dll is null) continue;
            if (!present.TryGetValue(id, out var existing))
            {
                if (PluginFolders.TryImport(modHome, id, dll, out _))
                {
                    done.Add(id);
                    handled.Add(id);
                }

                continue;
            }

            var folder = Path.Combine(existing.Enabled ? ModLayout.PluginsDirectory(modHome) : ModLayout.DisabledDirectory(modHome), id);
            File.Copy(dll, Path.Combine(folder, Path.GetFileName(dll)), overwrite: true);
            PluginLibrary.MirrorAssets(source, folder);
            done.Add(id + "(갱신)");
            handled.Add(id);
        }

        // the pictures several plugins share are in the payload once: every plugin gets the ones it needs next to itself
        var shared = SharedAssets.RootOf(pluginsFolder);
        var after = PluginFolders.List(modHome).ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in handled)
        {
            if (!after.TryGetValue(id, out var plugin)) continue;
            SharedAssets.Expand(shared, id, Path.Combine(plugin.Enabled ? ModLayout.PluginsDirectory(modHome) : ModLayout.DisabledDirectory(modHome), id));
        }

        return done;
    }
}