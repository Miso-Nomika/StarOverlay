using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarOverlay.Framework;

public sealed class ImagineCatalog
{
    readonly Dictionary<long, Player> _players = [];

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

    public (int Imagine, string Spec, int Count)[] Counts()
    {
        var scores = new Dictionary<(int Imagine, string Spec), int>();
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

    sealed record Row(long Id, string Spec, int[] Imagines);
}
