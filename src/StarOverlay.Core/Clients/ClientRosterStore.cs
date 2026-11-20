using System.Text.Json;
using StarOverlay.Core.Model;

namespace StarOverlay.Core.Clients;

public sealed record ClientRoster(IReadOnlyList<GameClient> Clients, Guid? SelectedId);

public static class ClientRosterStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string DefaultPath() =>
        Path.Combine(StarOverlay.Core.AppStorage.Root(), "clients.json");

    public static ClientRoster Load(string path)
    {
        if (!File.Exists(path)) return new ClientRoster([], null);
        try
        {
            var document = JsonSerializer.Deserialize<RosterFile>(File.ReadAllText(path), Json);
            if (document?.Clients is null) return new ClientRoster([], null);
            var clients = document.Clients
                .Select(client => new GameClient(client.Id, client.Name ?? "", client.Path ?? "", client.AddedAt))
                .ToArray();
            return new ClientRoster(clients, document.SelectedId);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClientRoster([], null);
        }
    }

    public static void Save(string path, IReadOnlyList<GameClient> clients, Guid? selectedId)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var document = new RosterFile
        {
            SelectedId = selectedId,
            Clients = clients.Select(client => new RosterClient
            {
                Id = client.Id,
                Name = client.DisplayName,
                Path = client.InstallPath,
                AddedAt = client.AddedAt,
            }).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, Json));
    }

    private sealed class RosterFile
    {
        public Guid? SelectedId { get; set; }

        public List<RosterClient>? Clients { get; set; }
    }

    private sealed class RosterClient
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public string? Path { get; set; }

        public DateTimeOffset AddedAt { get; set; }
    }
}
