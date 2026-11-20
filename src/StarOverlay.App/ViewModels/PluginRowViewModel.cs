using StarOverlay.Core.Mods;
using CommunityToolkit.Mvvm.Input;

namespace StarOverlay.App.ViewModels;

public sealed class PluginRowViewModel
{
    public PluginRowViewModel(PluginFolder folder, Action<string> toggle)
    {
        Id = folder.Id;
        Enabled = folder.Enabled;
        Summary = folder.Dlls.Count == 0 ? "DLL 없음" : string.Join(", ", folder.Dlls);
        ToggleCommand = new RelayCommand(() => toggle(Id));
    }

    public string Id { get; }

    public bool Enabled { get; }

    public string Summary { get; }

    public string ToggleLabel => Enabled ? "끄기" : "켜기";

    public IRelayCommand ToggleCommand { get; }
}
