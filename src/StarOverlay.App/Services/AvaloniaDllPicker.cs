using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace StarOverlay.App.Services;

public sealed class AvaloniaDllPicker(Func<Window?> window) : IDllPicker
{
    public async Task<string?> PickDllAsync()
    {
        var storage = window()?.StorageProvider;
        if (storage is null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "플러그인 DLL",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("플러그인") { Patterns = ["*.dll"] }],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
