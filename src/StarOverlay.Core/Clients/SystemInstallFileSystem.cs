namespace StarOverlay.Core.Clients;

public sealed class SystemInstallFileSystem : IInstallFileSystem
{
    public bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public bool FileExists(string path)
    {
        try { return File.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public IReadOnlyList<string> ListDirectories(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return [];
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
