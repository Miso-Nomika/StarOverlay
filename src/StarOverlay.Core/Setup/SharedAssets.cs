using System.Text.Json;

namespace StarOverlay.Core.Setup;

/// <summary>
/// Pictures several plugins use (the skill and imagine pictures of the buff tracker, the continuous input and the meter, the class pictures) are packed once in the
/// payload, in <c>shared\&lt;name&gt;</c>, instead of once in every plugin: <c>shared\shared.json</c> lists for every name the plugin folders (<c>&lt;plugin id&gt;/&lt;folder inside it&gt;</c>)
/// that need it. Installing a plugin copies the shared folders it needs into its own folder, so a plugin still finds its pictures next to itself.
/// </summary>
public static class SharedAssets
{
    public const string ManifestName = "shared.json";

    public sealed record SharedSet(string Name, List<string> Targets);

    /// <summary>The shared folder that belongs to a payload's <c>plugins</c> folder (next to it).</summary>
    public static string RootOf(string pluginsFolder) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(pluginsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? ".", "shared");

    /// <summary>Copies the shared folders that plugin <paramref name="pluginId"/> needs into <paramref name="pluginFolder"/>. Nothing happens when there is no shared folder.</summary>
    public static void Expand(string sharedRoot, string pluginId, string pluginFolder)
    {
        var manifest = Path.Combine(sharedRoot, ManifestName);
        if (!File.Exists(manifest)) return;
        List<SharedSet>? sets;
        try
        {
            sets = JsonSerializer.Deserialize<List<SharedSet>>(File.ReadAllText(manifest));
        }
        catch (JsonException)
        {
            return;
        }

        foreach (var set in sets ?? [])
        {
            var source = Path.Combine(sharedRoot, set.Name);
            if (!Directory.Exists(source)) continue;
            foreach (var target in set.Targets)
            {
                var slash = target.IndexOf('/');
                if (slash <= 0 || !string.Equals(target[..slash], pluginId, StringComparison.OrdinalIgnoreCase)) continue;
                CopyTree(source, Path.Combine(pluginFolder, target[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar)));
            }
        }
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
