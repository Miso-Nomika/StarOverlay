using System.IO.Compression;
using System.Reflection;

namespace StarOverlay.Core.Setup;

/// <summary>
/// The files the installer puts on the PC: the background service (host\), the receiving helper (capture\), the in-game loader
/// package (ingame\), the standard plugins (plugins\). In the released exe they travel inside it as <c>payload.zip</c> and are
/// unpacked once per version under the data folder; during development they are the <c>payload</c> folder next to the app or in
/// <c>artifacts\</c>.
/// </summary>
public sealed record PayloadPaths(string Root)
{
    public string HostExe => Path.Combine(Root, "host", "StarOverlay.Host.exe");

    public string CaptureExe => Path.Combine(Root, "capture", "StarOverlay.Capture.exe");

    public string IngamePackage => Path.Combine(Root, "ingame");

    public string Plugins => Path.Combine(Root, "plugins");

    /// <summary>The settings a new user starts with (copied only where the user has no file of their own).</summary>
    public string ConfigDefaults => Path.Combine(Root, "config-defaults");

    public bool IsComplete => File.Exists(HostExe) && File.Exists(CaptureExe) && Directory.Exists(IngamePackage) && Directory.Exists(Plugins);
}

public static class PayloadStore
{
    public const string ResourceName = "payload.zip";
    public const string ReadyFile = ".ready";

    /// <summary>Where the unpacked payload of one build lives: <c>&lt;data&gt;\app\&lt;id&gt;</c>.</summary>
    public static string UnpackedRoot(string dataRoot, string id) => Path.Combine(dataRoot, "app", id);

    /// <summary>The payload to install from: unpacked from the exe when it carries one, else the development folders.</summary>
    public static PayloadPaths? Find(Assembly assembly, string dataRoot, string appBase, string? projectRoot)
    {
        var embedded = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase));
        if (embedded is not null)
        {
            using var stream = assembly.GetManifestResourceStream(embedded)!;
            return Unpack(stream, dataRoot);
        }

        var candidates = new List<string> { Path.Combine(appBase, "payload") };
        if (projectRoot is not null) candidates.Add(Path.Combine(projectRoot, "artifacts", "payload"));
        return candidates.Select(root => new PayloadPaths(root)).FirstOrDefault(paths => paths.IsComplete);
    }

    /// <summary>
    /// Unpacks a payload zip into <c>&lt;data&gt;\app\&lt;id&gt;</c> (the id is the text of <c>payload.id</c> inside the zip) unless that
    /// folder is already complete, and removes the folders of older builds that are not in use.
    /// </summary>
    public static PayloadPaths? Unpack(Stream zip, string dataRoot)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var idEntry = archive.GetEntry("payload.id");
        if (idEntry is null) return null;
        string id;
        using (var reader = new StreamReader(idEntry.Open())) id = reader.ReadToEnd().Trim();
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        var root = UnpackedRoot(dataRoot, id);
        var paths = new PayloadPaths(root);
        if (File.Exists(Path.Combine(root, ReadyFile)) && paths.IsComplete) return paths;

        var temp = root + ".unpacking";
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        archive.ExtractToDirectory(temp, overwriteFiles: true);
        File.WriteAllText(Path.Combine(temp, ReadyFile), id);
        Directory.Move(temp, root);
        RemoveOlder(dataRoot, id);
        return paths.IsComplete ? paths : null;
    }

    /// <summary>Deletes the unpacked folders of other builds. One that is still running or locked is simply left for next time.</summary>
    public static void RemoveOlder(string dataRoot, string keepId)
    {
        var parent = Path.Combine(dataRoot, "app");
        if (!Directory.Exists(parent)) return;
        foreach (var directory in Directory.GetDirectories(parent))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, keepId, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
