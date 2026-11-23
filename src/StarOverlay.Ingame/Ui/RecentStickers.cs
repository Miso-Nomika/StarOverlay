using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The stickers and emojis the player sent last and the ones the player starred, kept in files of the game folder (bpsr\state\recent-stickers.txt and
/// favorite-stickers.txt) so they are there again after the game is started anew. The recent ones are taken from the player's own picture lines.
/// The box of the chat window shows <see cref="Slots"/> places: the starred ones first (they stay where they are, whatever is sent), the recent ones after
/// them in the places that are left.
/// </summary>
static class RecentStickers
{
    public const int Slots = 10;
    const int Stored = 30; // recent ones kept, so that places freed by taking a star away are filled again

    static readonly List<string> Recent = new();
    static readonly List<string> Favorites = new();
    static readonly HashSet<long> Seen = new(); // the sticker lines already counted
    static readonly HashSet<long> Plain = new(); // the lines of the player without a sticker: not read again
    static bool _loaded;

    /// <summary>Raised on every change, so the box draws again.</summary>
    internal static int Version;

    static string Folder => Path.Combine(Plugin.GameRoot, "bpsr", "state");

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        ReadInto(Path.Combine(Folder, "recent-stickers.txt"), Recent, Stored);
        ReadInto(Path.Combine(Folder, "favorite-stickers.txt"), Favorites, Slots);
    }

    static void ReadInto(string file, List<string> into, int max)
    {
        try
        {
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file))
            {
                var code = line.Trim();
                if (StickerPairing.PackOf(code) != null && !into.Contains(code) && into.Count < max) into.Add(code);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>What the box shows, place by place: the starred ones, then the recent ones that are not starred, at most <see cref="Slots"/>.</summary>
    internal static List<(string Code, bool Favorite)> Display()
    {
        Load();
        var list = Favorites.Select(code => (code, true)).ToList();
        foreach (var code in Recent)
        {
            if (list.Count >= Slots) break;
            if (!Favorites.Contains(code)) list.Add((code, false));
        }

        return list;
    }

    /// <summary>The first picture of the box (for the count of places that are taken).</summary>
    internal static bool IsFavorite(string code)
    {
        Load();
        return Favorites.Contains(code);
    }

    /// <summary>Stars a sticker or takes its star away. Returns false when all places are starred already and one more cannot be.</summary>
    internal static bool ToggleFavorite(string code)
    {
        Load();
        if (!Favorites.Remove(code))
        {
            if (Favorites.Count >= Slots) return false;
            Favorites.Add(code);
        }

        Version++;
        Save("favorite-stickers.txt", Favorites);
        return true;
    }

    /// <summary>Counts the player's own picture lines that were not counted before (the lines come oldest first).</summary>
    internal static void Observe(List<ChatLineDto> lines)
    {
        if (lines == null) return;
        Load();
        var changed = false;
        foreach (var line in lines)
        {
            if (!line.Self || Plain.Contains(line.Id) || Seen.Contains(line.Id)) continue;
            var code = StickerCache.NameOf(line.Text);
            if (code == null)
            {
                Plain.Add(line.Id);
                continue;
            }

            Seen.Add(line.Id);
            Recent.Remove(code);
            Recent.Insert(0, code);
            if (Recent.Count > Stored) Recent.RemoveRange(Stored, Recent.Count - Stored);
            changed = true;
        }

        if (Seen.Count > 600) Seen.Clear();
        if (Plain.Count > 2000) Plain.Clear();
        if (!changed) return;
        Version++;
        Save("recent-stickers.txt", Recent);
    }

    static void Save(string name, List<string> items)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder, name), items);
        }
        catch (Exception)
        {
        }
    }
}
