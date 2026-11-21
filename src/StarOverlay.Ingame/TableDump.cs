using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Ingame;

/// <summary>
/// Saves the tables the game keeps in memory (Bokura.&lt;name&gt;Base.GetTable) as JSON, only the fields the service uses, into the folder
/// the service names. It reads, never calls the game or the server, and does it once per game version (manifest.json). The rows are read a
/// few at a time per frame on the game thread; the files are written on another thread from plain managed values.
/// </summary>
static class TableDump
{
    sealed record Spec(string Table, string[] Fields);

    static readonly Spec[] Specs =
    [
        new("SkillTable", ["Id", "Name", "SlotPositionId", "Icon", "SkillLevelGroup", "SkySkillId", "NextSkillId", "SwitchSkillId"]),
        new("BuffTable", ["Id", "Name", "NameDesign", "Desc", "SkillId", "BuffAbilityType", "DestroyParam"]),
        new("SkillFightLevelTable", ["Id", "SkillId", "PVECoolTime"]),
        new("SceneTable", ["Id", "Name"]),
        new("DungeonsTable", ["Id", "SceneID", "PlayType", "DungeonTypeName"]),
        new("TargetTable", ["Id", "TargetType", "TargetDes"]),
        new("MonsterTable", ["Id", "MonsterType"]),
        new("ItemTable", ["Id", "Name", "Quality", "Description", "Icon"]),
        new("EquipTable", ["Id", "EquipPart", "EquipProfession", "AdvancedAttrLibId"]),
        new("EquipAttrLibTable", ["Id", "AttrLibId", "AttrEffect"]),
        new("RecountTable", ["Id", "RecountName", "DamageId"]),
        new("DamageAttrTable", ["Id", "TypeEnum"]),
    ];

    const float StartDelay = 15f;      // seconds with the chat bar on the screen (the game has logged in and loaded its tables)
    const float RetryDelay = 30f;
    const int MaxTries = 5;
    const double FrameBudgetMs = 3; // small, so the game does not stutter while its tables are read (the reading takes a little longer)

    enum Phase { Idle, Waiting, Reading, Writing, Done }

    static Phase _phase;
    static PipeLink _link;
    static string _root;
    static float _visibleSince = -1f;
    static float _retryAt;
    static int _tries;
    static Type[] _types;

    static int _table;
    static List<object> _rows;
    static int _row;
    static List<Dictionary<string, object>> _current;
    static PropertyInfo[] _props;
    static Dictionary<string, List<Dictionary<string, object>>> _data = new();
    static readonly HashSet<string> Skipped = new();

    /// <summary>Set every few hundredths of a second: the game's chat input bar is on the screen.</summary>
    internal static bool Visible;

    /// <summary>The service asked for the tables: nothing to do when this game version is already saved there.</summary>
    internal static void Request(PipeLink link, string root)
    {
        if (_phase is Phase.Reading or Phase.Writing) return;
        _link = link;
        _root = root;
        try
        {
            if (UpToDate())
            {
                _phase = Phase.Done;
                _link.Send(new Envelope { T = MessageType.TablesDone, Written = false });
                return;
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("tables: manifest unreadable: " + e.Message);
        }

        if (_phase == Phase.Done) _tries = 0;
        _phase = Phase.Waiting;
        _visibleSince = -1f;
        _retryAt = 0f;
    }

    /// <summary>Once per frame.</summary>
    internal static void Tick()
    {
        try
        {
            switch (_phase)
            {
                case Phase.Waiting:
                    if (!Visible)
                    {
                        _visibleSince = -1f;
                        return;
                    }

                    if (_visibleSince < 0) _visibleSince = UnityEngine.Time.realtimeSinceStartup;
                    if (UnityEngine.Time.realtimeSinceStartup - _visibleSince >= StartDelay && UnityEngine.Time.realtimeSinceStartup >= _retryAt) Begin();
                    break;
                case Phase.Reading:
                    ReadSome();
                    break;
            }
        }
        catch (Exception e)
        {
            Fail(e.GetType().Name + ": " + e.Message);
        }
    }

    static string Release()
    {
        var folder = Path.GetFileName(Path.GetFullPath(Plugin.GameRoot).TrimEnd('\\', '/'));
        var parent = Path.GetDirectoryName(Path.GetFullPath(Plugin.GameRoot).TrimEnd('\\', '/'));
        return (parent == null ? "" : Path.GetFileName(parent)) + "/" + folder;
    }

    static string Signature() => string.Join(";", Specs.Select(s => s.Table + ":" + string.Join(",", s.Fields)));

    static bool UpToDate()
    {
        var manifest = Path.Combine(_root, "manifest.json");
        if (!File.Exists(manifest)) return false;
        if (Specs.Any(s => !File.Exists(Path.Combine(_root, s.Table + ".json")))) return false;
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var root = document.RootElement;
        return root.TryGetProperty("fields", out var fields) && fields.GetString() == Signature() // another field list: save again
            && root.TryGetProperty("gameHash", out var hash) && hash.GetString() == PipeLink.GameHash()
            && root.TryGetProperty("release", out var release) && release.GetString() == Release();
    }

    static void Begin()
    {
        _types ??= FindTypes();
        if (_types.Length == 0)
        {
            Fail("Panda.Table not loaded");
            return;
        }

        _data = new();
        Skipped.Clear();
        _table = 0;
        _rows = null;
        _phase = Phase.Reading;
        Plugin.L.LogInfo("tables: reading the game's tables");
    }

    static Type[] FindTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "Panda.Table") continue;
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
        }

        return [];
    }

    static void ReadSome()
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < FrameBudgetMs)
        {
            var spec = Specs[_table];
            if (_rows == null)
            {
                OpenTable(spec);
                if (_phase != Phase.Reading) return;
            }

            while (_row < _rows.Count && watch.Elapsed.TotalMilliseconds < FrameBudgetMs)
                _current.Add(ReadRow(spec, _rows[_row++]));

            if (_row < _rows.Count) return;
            _data[spec.Table] = _current;
            _rows = null;
            if (++_table < Specs.Length) continue;
            _phase = Phase.Writing;
            var data = _data;
            var skipped = Skipped.ToArray();
            var root = _root;
            var link = _link;
            Task.Run(() => Write(data, skipped, root, link));
            return;
        }
    }

    static void OpenTable(Spec spec)
    {
        var type = _types.FirstOrDefault(t => t.Name == spec.Table + "Base" && t.Namespace == "Bokura")
            ?? _types.FirstOrDefault(t => t.Name == spec.Table + "Base");
        if (type == null)
        {
            Fail(spec.Table + ": type not found");
            return;
        }

        var get = type.GetMethod("GetTable", BindingFlags.Public | BindingFlags.Static, null, [typeof(bool)], null);
        var table = get?.Invoke(null, [false]);
        var values = table?.GetType().GetProperty("Values", BindingFlags.Public | BindingFlags.Instance)?.GetValue(table);
        if (values == null)
        {
            Fail(spec.Table + ": no table in memory yet");
            return;
        }

        var enumerator = values.GetType().GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(values, null);
        if (enumerator == null)
        {
            Fail(spec.Table + ": cannot enumerate");
            return;
        }

        var next = enumerator.GetType().GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
        var current = enumerator.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
        var rows = new List<object>();
        while ((bool)next.Invoke(enumerator, null)) rows.Add(current.GetValue(enumerator));
        if (rows.Count == 0)
        {
            Fail(spec.Table + ": empty");
            return;
        }

        _props = spec.Fields.Select(f => rows[0].GetType().GetProperty(f, BindingFlags.Public | BindingFlags.Instance)).ToArray();
        for (var i = 0; i < _props.Length; i++)
            if (_props[i] == null) Skipped.Add(spec.Table + "." + spec.Fields[i] + " (missing)");
        _rows = rows;
        _row = 0;
        _current = new List<Dictionary<string, object>>(rows.Count);
    }

    static Dictionary<string, object> ReadRow(Spec spec, object row)
    {
        var map = new Dictionary<string, object>(spec.Fields.Length);
        for (var i = 0; i < spec.Fields.Length; i++)
        {
            if (_props[i] == null) continue;
            try
            {
                var value = Convert(_props[i].GetValue(row), 0);
                if (value != null) map[spec.Fields[i]] = value;
            }
            catch (Exception e)
            {
                Skipped.Add(spec.Table + "." + spec.Fields[i] + " (" + e.GetType().Name + ")");
            }
        }

        return map;
    }

    /// <summary>Plain values stay as they are; the game's array wrappers (Length and an indexer) become lists, nested ones too.</summary>
    static object Convert(object value, int depth)
    {
        switch (value)
        {
            case null: return null;
            case string or bool or int or long or double: return value;
            case float f: return float.IsFinite(f) ? f : 0f;
            case short or byte or sbyte or ushort: return System.Convert.ToInt32(value);
        }

        if (depth >= 3) throw new NotSupportedException("too deep");
        var type = value.GetType();
        var length = type.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance) ?? type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        var item = type.GetMethod("get_Item", [typeof(int)]) ?? type.GetMethod("GetValue", [typeof(int)]);
        if (length == null || item == null) throw new NotSupportedException(type.Name);
        var count = System.Convert.ToInt32(length.GetValue(value));
        if (count < 0 || count > 20000) throw new NotSupportedException("length " + count);
        var list = new List<object>(count);
        for (var i = 0; i < count; i++) list.Add(Convert(item.Invoke(value, [i]), depth + 1));
        return list;
    }

    static void Fail(string reason)
    {
        _rows = null;
        _current = null;
        _tries++;
        Plugin.L.LogWarning($"tables: {reason} (try {_tries}/{MaxTries})");
        if (_tries >= MaxTries)
        {
            _phase = Phase.Done;
            _link?.Send(new Envelope { T = MessageType.TablesDone, Error = reason });
            return;
        }

        _phase = Phase.Waiting;
        _retryAt = UnityEngine.Time.realtimeSinceStartup + RetryDelay;
    }

    static void Write(Dictionary<string, List<Dictionary<string, object>>> data, string[] skipped, string root, PipeLink link)
    {
        try
        {
            var tmp = root.TrimEnd('\\', '/') + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };
            foreach (var spec in Specs)
            {
                using var stream = File.Create(Path.Combine(tmp, spec.Table + ".json"));
                using var writer = new Utf8JsonWriter(stream, options);
                writer.WriteStartObject();
                foreach (var row in data[spec.Table])
                {
                    if (!row.TryGetValue("Id", out var id)) continue;
                    writer.WritePropertyName(id.ToString());
                    writer.WriteStartObject();
                    foreach (var pair in row)
                    {
                        writer.WritePropertyName(pair.Key);
                        WriteValue(writer, pair.Value);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            using (var stream = File.Create(Path.Combine(tmp, "manifest.json")))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartObject();
                writer.WriteString("gameHash", PipeLink.GameHash());
                writer.WriteString("release", Release());
                writer.WriteString("fields", Signature());
                writer.WriteString("lang", UnityEngine.Application.systemLanguage.ToString());
                writer.WriteString("dumpedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                writer.WriteStartObject("counts");
                foreach (var spec in Specs) writer.WriteNumber(spec.Table, data[spec.Table].Count);
                writer.WriteEndObject();
                writer.WriteStartArray("skipped");
                foreach (var item in skipped) writer.WriteStringValue(item);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.Move(tmp, root);
            Plugin.L.LogInfo("tables: saved " + string.Join(", ", Specs.Select(s => s.Table + "=" + data[s.Table].Count)));
            link.Send(new Envelope { T = MessageType.TablesDone, Written = true });
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("tables: could not save: " + e.Message);
            link.Send(new Envelope { T = MessageType.TablesDone, Error = e.GetType().Name + ": " + e.Message });
        }
        finally
        {
            _phase = Phase.Done;
        }
    }

    static void WriteValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case float f: writer.WriteNumberValue(f); break;
            case double d: writer.WriteNumberValue(d); break;
            case List<object> list:
                writer.WriteStartArray();
                foreach (var element in list) WriteValue(writer, element);
                writer.WriteEndArray();
                break;
            default: writer.WriteNullValue(); break;
        }
    }
}
