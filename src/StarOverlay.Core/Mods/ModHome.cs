namespace StarOverlay.Core.Mods;

/// <summary>
/// Where plugins, plugin settings, fight records and state files live. It is a folder under the app's data folder
/// (<c>AppStorage.Root()\mods</c>), not the game folder, so a game update that changes <c>release_x</c> never loses them.
/// The mod APIs keep taking a "game directory" argument and add <c>bpsr\...</c> to it; this folder is what is passed there.
/// </summary>
public static class ModHome
{
    public const string FolderName = "mods";
    public const string MigratedMarker = ".migrated-from-game";

    static readonly string[] Migrated = ["config", "records", "plugins", "plugins-disabled"];

    public static string Path() => System.IO.Path.Combine(AppStorage.Root(), FolderName);

    /// <summary>
    /// Creates the mod home and, once, copies what an older install kept in <c>&lt;game&gt;\bpsr</c> into it.
    /// Copies only into an empty target and never deletes the originals.
    /// </summary>
    public static string Prepare(string gameDirectory) => Prepare(gameDirectory, Path());

    public static string Prepare(string gameDirectory, string home)
    {
        var target = System.IO.Path.Combine(home, ModLayout.RootFolder);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(ModLayout.PluginsDirectory(home));
        Directory.CreateDirectory(ModLayout.DisabledDirectory(home));
        var marker = System.IO.Path.Combine(target, MigratedMarker);
        if (File.Exists(marker)) return home;

        var source = System.IO.Path.Combine(gameDirectory, ModLayout.RootFolder);
        try
        {
            if (Directory.Exists(source))
            {
                foreach (var name in Migrated)
                {
                    var from = System.IO.Path.Combine(source, name);
                    var to = System.IO.Path.Combine(target, name);
                    if (Directory.Exists(from) && IsEmpty(to)) CopyDirectory(from, to);
                }
            }

            File.WriteAllText(marker, gameDirectory);
        }
        catch (IOException)
        {
            // try again next start
        }
        catch (UnauthorizedAccessException)
        {
        }

        return home;
    }

    static bool IsEmpty(string directory) => !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any();

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.GetDirectories(from))
            CopyDirectory(directory, System.IO.Path.Combine(to, System.IO.Path.GetFileName(directory)));
    }
}
