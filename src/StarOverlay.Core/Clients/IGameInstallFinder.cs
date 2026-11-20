namespace StarOverlay.Core.Clients;

public interface IGameInstallFinder
{
    IReadOnlyList<DiscoveredInstall> Find();

    DiscoveredInstall? Resolve(string folder);
}
