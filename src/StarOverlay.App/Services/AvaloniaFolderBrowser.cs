using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace StarOverlay.App.Services;

public sealed class AvaloniaFolderBrowser(Func<Window?> window) : IFolderBrowser
{
    public async Task<string?> PickFolderAsync(string? currentPath)
    {
        var owner = window();
        if (owner?.StorageProvider is not { } storage) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = "게임 설치 폴더 선택",
            AllowMultiple = false,
        };

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                var start = await storage.TryGetFolderFromPathAsync(currentPath);
                if (start is not null)
                    options.SuggestedStartLocation = start;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }

        var picked = await storage.OpenFolderPickerAsync(options);
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }
}
