using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

public enum PluginPresence
{
    LibraryOnly,
    Disabled,
    Enabled,
}

public sealed class LibraryRowViewModel
{
    public LibraryRowViewModel(string id, string name, string version, PluginPresence presence, bool canAct, Action<string> act)
    {
        Id = id;
        Name = name;
        Detail = string.IsNullOrWhiteSpace(version) ? id : $"{id} · {version}";
        Presence = presence;
        CanAct = canAct;
        ActCommand = new RelayCommand(() => act(id));
    }

    public string Id { get; }

    public string Name { get; }

    public string Detail { get; }

    public PluginPresence Presence { get; }

    public bool CanAct { get; }

    public string ActionLabel => Presence switch
    {
        PluginPresence.Enabled => "끄기",
        PluginPresence.Disabled => "켜기",
        _ => "적용",
    };

    public IRelayCommand ActCommand { get; }
}
