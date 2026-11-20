using System.Text;

namespace StarOverlay.Game.Ingame;

/// <summary>Small file log of the service side (connections, snapshots, errors) so a "nothing shows in game" report can be traced.</summary>
public sealed class IngameLog(string? path)
{
    readonly object _gate = new();

    public void Write(string text)
    {
        if (path is null) return;
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:MM-dd HH:mm:ss.fff} {text}\n", new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
        }
    }
}
