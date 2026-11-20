using StarOverlay.Core.Model;

namespace StarOverlay.Core.Mods;

public sealed record LibraryEntry(string Id, string DllPath);

public static class PluginLibrary
{
    public static string DefaultRoot() =>
        Path.Combine(StarOverlay.Core.AppStorage.Root(), "plugin-library");

    public static IReadOnlyList<LibraryEntry> List(string libraryRoot)
    {
        if (!Directory.Exists(libraryRoot)) return [];

        var found = new List<LibraryEntry>();
        foreach (var directory in Directory.GetDirectories(libraryRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(directory);
            if (!ModId.TryParse(name, out var id)) continue;
            var dll = Directory.GetFiles(directory, "*.dll").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (dll is null) continue;
            found.Add(new LibraryEntry(id.Value, dll));
        }

        return found;
    }

    public static bool TryAdd(string libraryRoot, string? pluginId, string sourceDll, out PluginFolderError error)
    {
        if (!ModId.TryParse(pluginId, out var id))
        {
            error = PluginFolderError.InvalidId;
            return false;
        }

        if (!File.Exists(sourceDll))
        {
            error = PluginFolderError.SourceMissing;
            return false;
        }

        var fileName = Path.GetFileName(sourceDll);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            error = PluginFolderError.NotDll;
            return false;
        }

        var destinationDir = Path.Combine(libraryRoot, id.Value);
        Directory.CreateDirectory(destinationDir);
        foreach (var old in Directory.GetFiles(destinationDir, "*.dll"))
        {
            if (!string.Equals(Path.GetFileName(old), fileName, StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        }

        File.Copy(sourceDll, Path.Combine(destinationDir, fileName), overwrite: true);
        MirrorAssets(Path.GetDirectoryName(sourceDll), destinationDir);
        error = default;
        return true;
    }

    internal static void MirrorAssets(string? sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrEmpty(sourceDirectory)) return;
        var source = Path.Combine(sourceDirectory, "assets");
        if (!Directory.Exists(source)) return;
        CopyTree(source, Path.Combine(destinationDirectory, "assets"));
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
