namespace StarOverlay.App.Services;

public interface IFolderBrowser
{
    Task<string?> PickFolderAsync(string? currentPath);
}
