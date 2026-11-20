using System.Text.Json;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Game.Ingame;

/// <summary>Window positions and states of the in-game overlay, stored as normalized (0..1) values in one JSON file.</summary>
public sealed class LayoutStore
{
    readonly string _path;
    readonly object _gate = new();
    readonly Dictionary<string, LayoutDto> _layouts;

    public LayoutStore(string path)
    {
        _path = path;
        _layouts = Load(path);
    }

    public LayoutDto Get(string id, int index, double defaultWidth, double defaultHeight)
    {
        lock (_gate)
        {
            if (_layouts.TryGetValue(id, out var stored)) return LayoutMath.Clamp(stored);
            return LayoutMath.Default(index, defaultWidth, defaultHeight);
        }
    }

    /// <summary>The stored layout, or <paramref name="fallback"/> (clamped) when none is stored.</summary>
    public LayoutDto Get(string id, LayoutDto fallback)
    {
        lock (_gate)
        {
            return LayoutMath.Clamp(_layouts.TryGetValue(id, out var stored) ? stored : fallback);
        }
    }

    /// <summary>Applies a change to the stored layout (creating it from <paramref name="current"/> when new) and saves.</summary>
    public LayoutDto Update(string id, LayoutDto current, Func<LayoutDto, LayoutDto> change)
    {
        lock (_gate)
        {
            var from = _layouts.TryGetValue(id, out var stored) ? stored : current;
            var next = LayoutMath.Clamp(change(LayoutMath.Clamp(from)));
            _layouts[id] = next;
            Save();
            return next;
        }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_layouts, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    static Dictionary<string, LayoutDto> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, LayoutDto>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return [];
    }
}
