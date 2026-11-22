using System.Collections.ObjectModel;
using StarOverlay.App.Services;
using StarOverlay.Core.Clients;
using StarOverlay.Core.Model;
using StarOverlay.Core;
using StarOverlay.Core.Mods;
using StarOverlay.Core.Setup;
using StarOverlay.Framework;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly GameClientCatalog _catalog;
    private readonly IGameInstallFinder? _finder;
    private readonly IFolderBrowser? _browser;
    private readonly string? _rosterPath;

    public ShellViewModel(
        GameClientCatalog? catalog = null,
        IGameInstallFinder? finder = null,
        IFolderBrowser? browser = null,
        IDllPicker? dllPicker = null,
        string? libraryRoot = null,
        string? rosterPath = null,
        PayloadPaths? payload = null,
        Func<string, bool>? isGameRunning = null,
        Func<string, Task<string>>? captureInstaller = null,
        string? specPath = null)
    {
        _specPath = specPath ?? DefaultSpecPath();
        _catalog = catalog ?? new GameClientCatalog();
        _finder = finder;
        _browser = browser;
        _dllPicker = dllPicker;
        _libraryRoot = libraryRoot ?? PluginLibrary.DefaultRoot();
        _rosterPath = rosterPath;
        _payload = payload;
        _captureInstaller = captureInstaller;
        _isGameRunning = isGameRunning;
        Guid? selectedId = null;
        if (!string.IsNullOrWhiteSpace(rosterPath))
        {
            var roster = ClientRosterStore.Load(rosterPath);
            selectedId = roster.SelectedId;
            foreach (var client in roster.Clients)
                _catalog.Restore(client);
        }

        foreach (var client in _catalog.List())
            Clients.Add(client);

        Clients.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasClients));
            OnPropertyChanged(nameof(ShowEmptyClients));
        };

        DetailText = EmptyDetail;
        RefreshLibrary();
        RefreshSpecs();
        if (Clients.Count > 0)
            SelectedClient = Clients.FirstOrDefault(client => client.Id == selectedId) ?? Clients[0];
    }

    public ObservableCollection<GameClient> Clients { get; } = [];

    public bool HasClients => Clients.Count > 0;

    public bool ShowEmptyClients => Clients.Count == 0;

    public CatalogAddError? LastError { get; private set; }

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private GameClient? _selectedClient;

    [ObservableProperty]
    private string _detailText = "";

    [ObservableProperty]
    private bool _isSearching;

    private bool CanFind() => !IsSearching && (_finder is not null || _browser is not null);

    public Task DiscoverInstallsAsync() => DiscoverAsync(browseWhenMissing: false);

    [RelayCommand(CanExecute = nameof(CanFind))]
    private Task FindInstallsAsync() => DiscoverAsync(browseWhenMissing: true);

    private async Task DiscoverAsync(bool browseWhenMissing)
    {
        if (IsSearching) return;
        IsSearching = true;
        FindInstallsCommand.NotifyCanExecuteChanged();
        try
        {
            IReadOnlyList<DiscoveredInstall> found = [];
            if (_finder is not null)
            {
                Status = "게임 설치 위치를 찾는 중...";
                try
                {
                    found = await Task.Run(() => _finder.Find());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Status = "게임 설치 위치를 찾는 동안 오류가 났습니다.";
                    return;
                }
            }

            DropMissingClients(found);
            var added = RegisterAll(found);
            if (added.Count > 0)
            {
                SelectedClient = added[0];
                Status = added.Count == 1
                    ? $"{added[0].DisplayName}을(를) 목록에 추가했습니다."
                    : $"게임 {added.Count}개를 목록에 추가했습니다.";
                return;
            }

            if (found.Count > 0)
            {
                Status = "찾은 게임은 이미 목록에 있습니다.";
                return;
            }

            if (!browseWhenMissing || _browser is null)
            {
                Status = "게임을 찾지 못했습니다.";
                return;
            }

            await BrowseAndRegister();
        }
        finally
        {
            IsSearching = false;
            FindInstallsCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>A game update moves the game to a new folder: saved clients whose folder is gone (and not found again) leave the list.</summary>
    private void DropMissingClients(IReadOnlyList<DiscoveredInstall> found)
    {
        var gone = Clients
            .Where(client => !Directory.Exists(client.InstallPath)
                && !found.Any(install => string.Equals(install.Path, client.InstallPath, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (gone.Count == 0) return;

        var selectedGone = SelectedClient is { } selected && gone.Contains(selected);
        foreach (var client in gone)
        {
            _catalog.Remove(client.Id);
            Clients.Remove(client);
        }

        if (selectedGone) SelectedClient = Clients.FirstOrDefault();
        else Persist();
    }

    private async Task BrowseAndRegister()
    {
        if (_browser is null) return;
        Status = "게임을 찾지 못했습니다. 게임 폴더를 직접 선택해 주세요.";
        string? picked;
        try
        {
            picked = await _browser.PickFolderAsync(SelectedClient?.InstallPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "폴더 선택 창을 열지 못했습니다.";
            return;
        }

        if (string.IsNullOrWhiteSpace(picked))
        {
            Status = "폴더 선택을 취소했습니다.";
            return;
        }

        if (_finder?.Resolve(picked) is { } install)
        {
            RegisterOne(install.SuggestedName, install.Path, "선택한 폴더의 게임을 목록에 추가했습니다.");
            return;
        }

        var name = Path.GetFileName(picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        RegisterOne(string.IsNullOrWhiteSpace(name) ? "게임" : name, picked, "선택한 폴더를 게임으로 추가했습니다.");
    }

    private List<GameClient> RegisterAll(IEnumerable<DiscoveredInstall> found)
    {
        var added = new List<GameClient>();
        foreach (var install in found)
        {
            if (TryRegister(install.SuggestedName, install.Path) is { } client)
                added.Add(client);
        }

        return added;
    }

    private void RegisterOne(string name, string path, string success)
    {
        if (TryRegister(name, path) is not { } client)
        {
            Status = Message(LastError ?? CatalogAddError.PathInvalid);
            return;
        }

        SelectedClient = client;
        Status = success;
    }

    private GameClient? TryRegister(string? name, string path)
    {
        switch (_catalog.TryAdd(name, path))
        {
            case CatalogAddResult.Added added:
                Clients.Add(added.Client);
                LastError = null;
                return added.Client;
            case CatalogAddResult.Rejected rejected:
                LastError = rejected.Error;
                return null;
            default:
                return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedClient is not { } client) return;
        if (!_catalog.Remove(client.Id)) return;

        Clients.Remove(client);
        SelectedClient = Clients.FirstOrDefault();
        LastError = null;
        Persist();
        Status = $"{client.DisplayName}을(를) 목록에서 뺐습니다.";
    }

    private bool CanRemoveSelected() => SelectedClient is not null;

    partial void OnSelectedClientChanged(GameClient? value)
    {
        RefreshClient();
        Persist();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(_rosterPath) || _removedAll) return;
        ClientRosterStore.Save(_rosterPath, Clients.ToArray(), SelectedClient?.Id);
    }

    private const string EmptyDetail = "게임을 선택하면 그 게임의 플러그인을 여기서 관리합니다.";

    private static string Message(CatalogAddError error) => error switch
    {
        CatalogAddError.NameRequired => "이름을 입력하세요.",
        CatalogAddError.NameInvalid => "이름은 64자 이하여야 하며 제어 문자를 넣을 수 없습니다.",
        CatalogAddError.PathRequired => "게임 폴더를 입력하세요.",
        CatalogAddError.PathNotAbsolute => "게임 폴더는 전체 경로여야 합니다.",
        CatalogAddError.PathInvalid => "게임 폴더를 확인할 수 없습니다.",
        CatalogAddError.PathDuplicate => "이미 목록에 있는 게임 폴더입니다.",
        _ => "게임을 추가할 수 없습니다.",
    };
}
