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
    public static readonly string[] StandardPlugins = ["dps.meter", "chat", "party.join", "buff.tracker"];

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

    public static InstallReport Uninstall(string game, Func<string, bool>? isGameRunning = null)
    {
        var result = IngameDeployer.Uninstall(game, isGameRunning);
        return new InstallReport(result.Success, result.Success ? "제거했습니다. 플러그인 설정과 전투 기록은 데이터 폴더에 그대로 있습니다." : result.Message, []);
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
        foreach (var id in StandardPlugins)
        {
            var source = Path.Combine(pluginsFolder, id);
            var dll = Directory.Exists(source) ? Directory.GetFiles(source, "*.dll").FirstOrDefault() : null;
            if (dll is null) continue;
            if (!present.TryGetValue(id, out var existing))
            {
                if (PluginFolders.TryImport(modHome, id, dll, out _)) done.Add(id);
                continue;
            }

            var folder = Path.Combine(existing.Enabled ? ModLayout.PluginsDirectory(modHome) : ModLayout.DisabledDirectory(modHome), id);
            File.Copy(dll, Path.Combine(folder, Path.GetFileName(dll)), overwrite: true);
            PluginLibrary.MirrorAssets(source, folder);
            done.Add(id + "(갱신)");
        }

        return done;
    }
}