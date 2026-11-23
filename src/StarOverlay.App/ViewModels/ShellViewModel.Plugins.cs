using System.Collections.ObjectModel;
using StarOverlay.App.Services;
using StarOverlay.Core.Mods;
using StarOverlay.Core.Setup;
using StarOverlay.Framework;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

public partial class ShellViewModel
{
    private readonly IDllPicker? _dllPicker;
    private readonly string _libraryRoot;

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = [];

    public ObservableCollection<LibraryRowViewModel> Library { get; } = [];

    public bool HasPlugins => Plugins.Count > 0;

    public bool ShowEmptyPlugins => Plugins.Count == 0;

    public bool HasLibrary => Library.Count > 0;

    public bool ShowEmptyLibrary => Library.Count == 0;

    private bool CanManageClient() => SelectedClient is not null;

    /// <summary>Plugins live in the data folder (ModHome), not in the game folder; the first call copies what an older install kept there.</summary>
    private static string ModHomeOf(string installPath) => ModHome.Prepare(installPath);

    [RelayCommand]
    private async Task AddToLibraryAsync()
    {
        if (_dllPicker is null)
        {
            Status = "이 환경에서는 DLL을 고를 수 없습니다.";
            return;
        }

        var picked = await _dllPicker.PickDllAsync();
        if (string.IsNullOrWhiteSpace(picked)) return;
        if (!PluginInspector.TryRead(picked, out var info, out var readError) || info is null)
        {
            Status = readError;
            return;
        }

        if (!PluginLibrary.TryAdd(_libraryRoot, info.Id, picked, out var error))
        {
            Status = ImportMessage(error);
            return;
        }

        RefreshLibrary();
        Status = $"{info.Name} 플러그인을 보관함에 추가했습니다.";
    }

    private void ActOnPlugin(string id)
    {
        var row = Library.FirstOrDefault(plugin => plugin.Id == id);
        if (row is null) return;
        if (row.Presence == PluginPresence.LibraryOnly)
            ApplyLibraryPlugin(id);
        else
            TogglePlugin(id);
    }

    private void ApplyLibraryPlugin(string id)
    {
        if (SelectedClient is null)
        {
            Status = "먼저 게임을 선택해 주세요.";
            return;
        }

        var entry = PluginLibrary.List(_libraryRoot).FirstOrDefault(plugin => plugin.Id == id);
        if (entry is null)
        {
            Status = "보관함에서 플러그인을 찾지 못했습니다.";
            return;
        }

        var installed = Directory.Exists(SelectedClient.InstallPath)
            ? PluginFolders.List(ModHomeOf(SelectedClient.InstallPath)).FirstOrDefault(plugin => plugin.Id == id)
            : null;
        if (installed is { Enabled: true })
        {
            Status = $"{id} 플러그인은 이미 적용되어 있습니다.";
            RefreshLibrary();
            return;
        }

        if (installed is { Enabled: false })
        {
            if (!PluginFolders.TrySetEnabled(ModHomeOf(SelectedClient.InstallPath), id, enabled: true, out var enableError))
            {
                Status = ImportMessage(enableError);
                return;
            }
        }
        else if (!PluginFolders.TryImport(ModHomeOf(SelectedClient.InstallPath), id, entry.DllPath, out var importError))
        {
            Status = ImportMessage(importError);
            return;
        }

        RefreshClient();
        Status = $"{id} 플러그인을 적용했습니다. 게임을 다시 켜면 반영됩니다.";
    }

    public void IncludeBundledPlugins(string bundledDirectory)
    {
        if (!Directory.Exists(bundledDirectory)) return;
        foreach (var dll in Directory.GetFiles(bundledDirectory, "*.dll", SearchOption.AllDirectories))
        {
            if (!PluginInspector.TryRead(dll, out var info, out _) || info is null) continue;
            if (PluginLibrary.TryAdd(_libraryRoot, info.Id, dll, out _))
                SharedAssets.Expand(SharedAssets.RootOf(bundledDirectory), info.Id, Path.Combine(_libraryRoot, info.Id)); // the pictures several plugins share
        }

        RefreshLibrary();
    }

    private void TogglePlugin(string id)
    {
        if (SelectedClient is null) return;
        var row = Plugins.FirstOrDefault(plugin => plugin.Id == id);
        if (row is null) return;
        var enable = !row.Enabled;
        if (!PluginFolders.TrySetEnabled(ModHomeOf(SelectedClient.InstallPath), id, enable, out var error))
        {
            Status = ImportMessage(error);
            return;
        }

        RefreshClient();
        Status = enable ? $"{id} 플러그인을 켰습니다. 게임을 다시 켜면 반영됩니다." : $"{id} 플러그인을 껐습니다.";
    }

    private void RefreshClient()
    {
        Plugins.Clear();
        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(ShowEmptyPlugins));
        if (SelectedClient is null)
        {
            DetailText = EmptyDetail;
            RefreshIngame();
            RefreshLibrary();
            return;
        }

        var game = SelectedClient.InstallPath;
        if (Directory.Exists(game))
        {
            foreach (var plugin in PluginFolders.List(ModHomeOf(game)))
                Plugins.Add(new PluginRowViewModel(plugin, TogglePlugin));
        }

        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(ShowEmptyPlugins));
        DetailText = $"{SelectedClient.DisplayName} · 플러그인 {Plugins.Count}개";
        RefreshIngame();
        RefreshLibrary();
    }
    private void RefreshLibrary()
    {
        var installed = Plugins.ToDictionary(plugin => plugin.Id, plugin => plugin.Enabled, StringComparer.OrdinalIgnoreCase);
        Library.Clear();
        foreach (var entry in PluginLibrary.List(_libraryRoot))
        {
            var name = entry.Id;
            var version = "";
            if (PluginInspector.TryRead(entry.DllPath, out var info, out _) && info is not null)
            {
                name = info.Name;
                version = info.Version;
            }

            var presence = installed.TryGetValue(entry.Id, out var enabled)
                ? enabled ? PluginPresence.Enabled : PluginPresence.Disabled
                : PluginPresence.LibraryOnly;
            Library.Add(new LibraryRowViewModel(entry.Id, name, version, presence, SelectedClient is not null, ActOnPlugin));
        }

        foreach (var plugin in Plugins)
        {
            if (Library.Any(row => string.Equals(row.Id, plugin.Id, StringComparison.OrdinalIgnoreCase))) continue;
            var presence = plugin.Enabled ? PluginPresence.Enabled : PluginPresence.Disabled;
            Library.Add(new LibraryRowViewModel(plugin.Id, plugin.Id, "", presence, SelectedClient is not null, ActOnPlugin));
        }

        OnPropertyChanged(nameof(HasLibrary));
        OnPropertyChanged(nameof(ShowEmptyLibrary));
    }

    private static string ImportMessage(PluginFolderError error) => error switch
    {
        PluginFolderError.GameDirectoryMissing => "게임 폴더를 찾지 못했습니다.",
        PluginFolderError.InvalidId => "플러그인 이름은 영문 식별자여야 합니다.",
        PluginFolderError.SourceMissing => "플러그인 DLL을 찾지 못했습니다.",
        PluginFolderError.NotDll => "플러그인 파일은 DLL이어야 합니다.",
        PluginFolderError.AlreadyExists => "같은 이름의 플러그인이 이미 있습니다.",
        PluginFolderError.Missing => "플러그인 폴더를 찾지 못했습니다.",
        _ => "플러그인을 바꿀 수 없습니다.",
    };
}
