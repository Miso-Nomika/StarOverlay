using StarOverlay.Core.Model;
using StarOverlay.Core.Platform;

namespace StarOverlay.Core.Clients;

public enum CatalogAddError
{
    NameRequired,
    NameInvalid,
    PathRequired,
    PathNotAbsolute,
    PathInvalid,
    PathDuplicate,
}

public abstract record CatalogAddResult
{
    private CatalogAddResult() { }

    public sealed record Added(GameClient Client) : CatalogAddResult;

    public sealed record Rejected(CatalogAddError Error) : CatalogAddResult;
}

public sealed class GameClientCatalog
{
    private readonly List<GameClient> _clients = [];
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<Guid> _ids;

    public GameClientCatalog(Func<DateTimeOffset>? clock = null, Func<Guid>? ids = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ids = ids ?? Guid.NewGuid;
    }

    public IReadOnlyList<GameClient> List() => _clients.ToArray();

    public CatalogAddResult TryAdd(string? displayName, string? installPath)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length == 0)
            return new CatalogAddResult.Rejected(CatalogAddError.NameRequired);
        if (name.Length > 64 || name.Any(char.IsControl))
            return new CatalogAddResult.Rejected(CatalogAddError.NameInvalid);

        if (string.IsNullOrWhiteSpace(installPath))
            return new CatalogAddResult.Rejected(CatalogAddError.PathRequired);

        if (!AbsolutePath.TryNormalize(installPath, out var normalized, out var pathError))
        {
            var error = pathError switch
            {
                PathNormalizeError.NotAbsolute => CatalogAddError.PathNotAbsolute,
                PathNormalizeError.Invalid => CatalogAddError.PathInvalid,
                _ => CatalogAddError.PathRequired,
            };
            return new CatalogAddResult.Rejected(error);
        }

        if (_clients.Any(client => HostPlatform.PathComparer.Equals(client.InstallPath, normalized)))
            return new CatalogAddResult.Rejected(CatalogAddError.PathDuplicate);

        var id = _ids();
        if (id == Guid.Empty)
            throw new InvalidOperationException("Client id factory returned an empty id.");

        var client = new GameClient(id, name, normalized, _clock());
        _clients.Add(client);
        return new CatalogAddResult.Added(client);
    }

    public bool Restore(GameClient client)
    {
        if (client.Id == Guid.Empty) return false;
        var name = client.DisplayName.Trim();
        if (name.Length == 0 || name.Length > 64 || name.Any(char.IsControl)) return false;
        if (!AbsolutePath.TryNormalize(client.InstallPath, out var normalized, out _)) return false;
        if (_clients.Any(existing => existing.Id == client.Id || HostPlatform.PathComparer.Equals(existing.InstallPath, normalized)))
            return false;

        _clients.Add(client with { DisplayName = name, InstallPath = normalized });
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0) return false;
        _clients.RemoveAt(index);
        return true;
    }
}
