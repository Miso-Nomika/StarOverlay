using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace StarOverlay.Framework;

/// <summary>One saved character: uid, detailed job, whether it is confirmed, fight power, far-sea gear, season.</summary>
public sealed record SpecRow(long Id, string Spec, bool Confirmed, long FightPoint, bool FarSea, int Season = SpecBook.CurrentSeason);

public sealed class SpecStore : IDisposable
{
    /// <summary>
    /// Every saved row, by uid, read without changing anything (the service may still have the file open).
    /// A missing or unreadable file gives an empty list and is never created.
    /// </summary>
    public static IReadOnlyList<SpecRow> ReadAll(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT id, spec, \"check\", fightPoint, farSea, season FROM spec ORDER BY id";
            using var rows = read.ExecuteReader();
            var list = new List<SpecRow>();
            while (rows.Read())
                list.Add(new SpecRow(rows.GetInt64(0), rows.GetString(1), rows.GetInt64(2) != 0, rows.GetInt64(3), rows.GetInt64(4) != 0, (int)rows.GetInt64(5)));
            return list;
        }
        catch (SqliteException)
        {
            return []; // an old file without the newer columns, or a locked one: show nothing rather than fail
        }
    }

    /// <summary>The season of rows saved before the season column existed.</summary>
    const int LegacySeason = 3;

    readonly SqliteConnection _db;
    readonly SqliteCommand _upsert;
    readonly object _gate = new();

    public SpecStore(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        using (var prepare = _db.CreateCommand())
        {
            prepare.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE IF NOT EXISTS spec (
                    id INTEGER PRIMARY KEY,
                    spec TEXT NOT NULL
                );
                """;
            prepare.ExecuteNonQuery();
        }

        EnsureColumns();
        using (var clear = _db.CreateCommand())
        {
            // values saved before the limit existed: the character stays, only the inflated number goes
            clear.CommandText = "UPDATE spec SET fightPoint = 0 WHERE fightPoint >= $limit";
            clear.Parameters.AddWithValue("$limit", SpecBook.FightPointLimit);
            clear.ExecuteNonQuery();
        }

        _upsert = _db.CreateCommand();
        _upsert.CommandText = """
            INSERT INTO spec(id, spec, "check", fightPoint, farSea, season) VALUES ($id, $spec, $check, $fight, $far, $season)
            ON CONFLICT(id) DO UPDATE SET
                spec = CASE WHEN excluded.season > spec.season OR (excluded.season = spec.season AND (spec."check" = 0 OR excluded."check" = 1 OR excluded.fightPoint > spec.fightPoint)) THEN excluded.spec ELSE spec.spec END,
                "check" = CASE WHEN excluded.season > spec.season OR (excluded.season = spec.season AND (spec."check" = 0 OR excluded."check" = 1 OR excluded.fightPoint > spec.fightPoint)) THEN excluded."check" ELSE spec."check" END,
                fightPoint = CASE WHEN excluded.season > spec.season OR (excluded.season = spec.season AND (spec."check" = 0 OR excluded."check" = 1 OR excluded.fightPoint > spec.fightPoint)) THEN excluded.fightPoint ELSE spec.fightPoint END,
                farSea = CASE WHEN excluded.season >= spec.season THEN excluded.farSea ELSE spec.farSea END,
                season = MAX(spec.season, excluded.season)
            """;
        _upsert.Parameters.Add("$id", SqliteType.Integer);
        _upsert.Parameters.Add("$spec", SqliteType.Text);
        _upsert.Parameters.Add("$check", SqliteType.Integer);
        _upsert.Parameters.Add("$fight", SqliteType.Integer);
        _upsert.Parameters.Add("$far", SqliteType.Integer);
        _upsert.Parameters.Add("$season", SqliteType.Integer);
        if (folder != null) ImportJson(Path.Combine(folder, "spec-db.json"));
    }

    public void Load()
    {
        lock (_gate)
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT id, spec, \"check\", fightPoint, farSea, season FROM spec";
            using var rows = read.ExecuteReader();
            while (rows.Read())
                SpecBook.Remember(rows.GetInt64(0), rows.GetString(1), rows.GetInt64(2) != 0, rows.GetInt64(3), rows.GetInt64(4) != 0, (int)rows.GetInt64(5));
        }
    }

    /// <summary>Saves characters seen now (season <see cref="SpecBook.CurrentSeason"/>).</summary>
    public void Save(IReadOnlyList<(long Id, string Spec, bool Check, long FightPoint, bool FarSea)> rows) =>
        Save(rows.Select(r => (r.Id, r.Spec, r.Check, r.FightPoint, r.FarSea, SpecBook.CurrentSeason)).ToList());

    public void Save(IReadOnlyList<(long Id, string Spec, bool Check, long FightPoint, bool FarSea, int Season)> rows)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            _upsert.Transaction = tx;
            foreach (var row in rows)
            {
                _upsert.Parameters["$id"].Value = row.Id;
                _upsert.Parameters["$spec"].Value = row.Spec;
                _upsert.Parameters["$check"].Value = row.Check ? 1 : 0;
                _upsert.Parameters["$fight"].Value = row.FightPoint;
                _upsert.Parameters["$far"].Value = row.FarSea ? 1 : 0;
                _upsert.Parameters["$season"].Value = row.Season;
                _upsert.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void Delete(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0) return;
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            using var delete = _db.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM spec WHERE id = $id";
            var id = delete.Parameters.Add("$id", SqliteType.Integer);
            foreach (var row in ids)
            {
                id.Value = row;
                delete.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _upsert.Dispose();
            _db.Dispose();
        }
    }

    void ImportJson(string path)
    {
        if (!File.Exists(path)) return;
        using var count = _db.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM spec";
        if (Convert.ToInt64(count.ExecuteScalar()) > 0) return;
        FileBody? body;
        try
        {
            body = JsonSerializer.Deserialize<FileBody>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (body?.Players == null) return;
        var rows = new List<(long Id, string Spec, bool Check, long FightPoint, bool FarSea, int Season)>();
        foreach (var row in body.Players)
        {
            if (row.Id == 0 || string.IsNullOrEmpty(row.Spec)) continue;
            rows.Add((row.Id, row.Spec, false, 0, false, LegacySeason));
        }

        Save(rows);
        File.Move(path, path + ".imported", true);
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record FileBody(Row[] Players);

    void EnsureColumns()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using (var info = _db.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(spec)";
            using var rows = info.ExecuteReader();
            while (rows.Read()) names.Add(rows.GetString(1));
        }

        if (!names.Contains("check"))
        {
            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE spec ADD COLUMN \"check\" INTEGER NOT NULL DEFAULT 0";
            alter.ExecuteNonQuery();
        }

        if (!names.Contains("fightPoint"))
        {
            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE spec ADD COLUMN fightPoint INTEGER NOT NULL DEFAULT 0";
            alter.ExecuteNonQuery();
        }

        if (!names.Contains("season"))
        {
            // everything saved before seasons existed is season 3 (the default fills the old rows)
            using var alter = _db.CreateCommand();
            alter.CommandText = $"ALTER TABLE spec ADD COLUMN season INTEGER NOT NULL DEFAULT {LegacySeason}";
            alter.ExecuteNonQuery();
        }

        if (!names.Contains("farSea"))
        {
            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE spec ADD COLUMN farSea INTEGER NOT NULL DEFAULT 0";
            alter.ExecuteNonQuery();
        }
    }

    sealed record Row(long Id, string Spec);
}
