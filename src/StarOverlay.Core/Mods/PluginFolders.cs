using StarOverlay.Core.Model;

namespace StarOverlay.Core.Mods;

public sealed record PluginFolder(string Id, bool Enabled, IReadOnlyList<string> Dlls);

public enum PluginFolderError
{
    GameDirectoryMissing,
    InvalidId,
    SourceMissing,
    NotDll,
    AlreadyExists,
    Missing,
}

public static class PluginFolders
{
    public static IReadOnlyList<PluginFolder> List(string gameDirectory)
    {
        var found = new List<PluginFolder>();
        AddRange(found, ModLayout.PluginsDirectory(gameDirectory), enabled: true);
        AddRange(found, ModLayout.DisabledDirectory(gameDirectory), enabled: false);
        return found;
    }

    public static bool TryImport(string gameDirectory, string? pluginId, string dllSource, out PluginFolderError error)
    {
        if (!Directory.Exists(gameDirectory))
        {
            error = PluginFolderError.GameDirectoryMissing;
            return false;
        }

        if (!ModId.TryParse(pluginId, out var id))
        {
            error = PluginFolderError.InvalidId;
            return false;
        }

        if (!File.Exists(dllSource))
        {
            error = PluginFolderError.SourceMissing;
            return false;
        }

        var fileName = Path.GetFileName(dllSource);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            error = PluginFolderError.NotDll;
            return false;
        }

        if (Folder(gameDirectory, id.Value, enabled: false) is not null)
        {
            error = PluginFolderError.AlreadyExists;
            return false;
        }

        var destinationDir = Path.Combine(ModLayout.PluginsDirectory(gameDirectory), id.Value);
        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, fileName);
        if (!ModLayout.IsInsideRoot(gameDirectory, destination))
        {
            error = PluginFolderError.InvalidId;
            return false;
        }

        File.Copy(dllSource, destination, overwrite: true);
        PluginLibrary.MirrorAssets(Path.GetDirectoryName(dllSource), destinationDir);
        error = default;
        return true;
    }

    public static bool TrySetEnabled(string gameDirectory, string pluginId, bool enabled, out PluginFolderError error)
    {
        if (!ModId.TryParse(pluginId, out var id))
        {
            error = PluginFolderError.InvalidId;
            return false;
        }

        var source = Folder(gameDirectory, id.Value, enabled: !enabled);
        if (source is null)
        {
            error = PluginFolderError.Missing;
            return false;
        }

        var destinationRoot = enabled ? ModLayout.PluginsDirectory(gameDirectory) : ModLayout.DisabledDirectory(gameDirectory);
        var destination = Path.Combine(destinationRoot, id.Value);
        if (Directory.Exists(destination))
        {
            error = PluginFolderError.AlreadyExists;
            return false;
        }

        Directory.CreateDirectory(destinationRoot);
        Directory.Move(source, destination);
        error = default;
        return true;
    }

    private static void AddRange(List<PluginFolder> found, string root, bool enabled)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(directory);
            if (!ModId.TryParse(name, out var id)) continue;
            var dlls = Directory.GetFiles(directory, "*.dll").Select(Path.GetFileName).Where(file => file is not null).Cast<string>().ToArray();
            found.Add(new PluginFolder(id.Value, enabled, dlls));
        }
    }

    private static string? Folder(string gameDirectory, string id, bool enabled)
    {
        var path = Path.Combine(enabled ? ModLayout.PluginsDirectory(gameDirectory) : ModLayout.DisabledDirectory(gameDirectory), id);
        return Directory.Exists(path) ? path : null;
    }
}
