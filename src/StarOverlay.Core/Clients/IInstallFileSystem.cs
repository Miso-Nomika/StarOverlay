namespace StarOverlay.Core.Clients;

public interface IInstallFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IReadOnlyList<string> ListDirectories(string path);
}
