using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarOverlay.Framework;

public sealed class ImagineCatalog
{
    readonly Dictionary<long, Player> _players = [];
    (int Imagine, string Spec, int Count)[] _seed = [];

    sealed record Player(string Spec, int[] Imagines);

    public bool Note(long charId, string spec, IReadOnlyList<int> imagines)
    {
        if (charId == 0 || string.IsNullOrEmpty(spec) || imagines.Count == 0 || ImagineSpec.Profession(spec) == 0)
            return false;
        var ids = imagines.Where(id => id != 0).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return false;
        if (_players.TryGetValue(charId, out var current)
            && current.Spec == spec
            && current.Imagines.AsSpan().SequenceEqual(ids))
            return false;
        _players[charId] = new Player(spec, ids);
        return true;
    }

    /// <summary>
    /// Counts that ship with the program (how often each imagine was seen on players whose job their gear settled, summed over many players, no
    /// player in it): a new user starts with them instead of an empty list, so a job can be guessed from the imagines at the first sight.
    /// What this PC learns is added on top.
    /// </summary>
    public void UseSeed(string json)
    {
        try
        {
            var body = JsonSerializer.Deserialize<SeedBody>(json, Json);
            if (body?.Rows == null) return;
            _seed = body.Rows
                .Where(row => row.Length == 3 && row[0].ValueKind == JsonValueKind.Number && row[1].ValueKind == JsonValueKind.String && row[2].ValueKind == JsonValueKind.Number)
                .Select(row => (row[0].GetInt32(), row[1].GetString() ?? "", row[2].GetInt32()))
                .Where(row => row.Item3 > 0 && ImagineSpec.Profession(row.Item2) != 0)
                .ToArray();
        }
        catch (JsonException)
        {
        }
    }

    public (int Imagine, string Spec, int Count)[] Counts()
    {
        var scores = new Dictionary<(int Imagine, string Spec), int>();
        foreach (var (imagine, spec, count) in _seed) scores[(imagine, spec)] = count;
        foreach (var player in _players.Values)
        {
            foreach (var imagine in player.Imagines)
            {
                var key = (imagine, player.Spec);
                scores.TryGetValue(key, out var count);
                scores[key] = count + 1;
            }
        }

        var rows = new (int Imagine, string Spec, int Count)[scores.Count];
        var index = 0;
        foreach (var pair in scores)
            rows[index++] = (pair.Key.Imagine, pair.Key.Spec, pair.Value);
        return rows;
    }

    public string ToJson()
    {
        var rows = new Row[_players.Count];
        var index = 0;
        foreach (var pair in _players.OrderBy(pair => pair.Key))
            rows[index++] = new Row(pair.Key, pair.Value.Spec, pair.Value.Imagines);
        return JsonSerializer.Serialize(new FileBody(rows), Json);
    }

    public void Restore(string json)
    {
        _players.Clear();
        FileBody? body;
        try
        {
            body = JsonSerializer.Deserialize<FileBody>(json, Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (body?.Players == null) return;
        foreach (var row in body.Players)
        {
            if (row.Imagines == null) continue;
            Note(row.Id, row.Spec ?? "", row.Imagines);
        }
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record FileBody(Row[] Players);

    sealed record SeedBody(JsonElement[][] Rows);

    sealed record Row(long Id, string Spec, int[] Imagines);
}
