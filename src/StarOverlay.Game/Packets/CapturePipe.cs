using System.Security.Cryptography;
using System.Text;

namespace StarOverlay.Game.Packets;

/// <summary>
/// The pipe between the raw-socket helper and the service of ONE game: the helper (one process for every game that runs) and the service both write the game
/// folder the same way here, so each game's packets reach only its own service. The helper project links this file, so it must stay free of anything else.
/// </summary>
public static class CapturePipe
{
    /// <summary>"base.key": the base name is per Windows user, the key says which game folder.</summary>
    public static string Name(string baseName, string gameFolder) => baseName + "." + FolderKey(gameFolder);

    public static string FolderKey(string folder)
    {
        var text = (folder ?? "").Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder();
        for (var i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }
}
