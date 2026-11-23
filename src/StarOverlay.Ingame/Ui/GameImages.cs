using System;
using System.Collections.Generic;
using Panda.ZUi;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Pictures of the game's own sprite files, asked for by their address ("ui/atlas/season_talent/s2talent03_01"). The game's loader puts the picture into
/// a ZImage (SetImage); a picture file (texture) goes into a ZRawImage, found by its name through <see cref="AddressBook"/>. It is copied from there into a texture of
/// ours (the game's pictures are not readable), so a window of ours can show it ("game:&lt;address&gt;" in <see cref="UiKit.Picture"/>, or <see cref="Texture"/> by name).
/// A picture that is not there yet is asked for and shows up as soon as it was loaded.
/// </summary>
static class GameImages
{
    const float GiveUpSeconds = 8f;

    sealed class Pending
    {
        public GameObject Host;
        public ZImage Image;       // an atlas sprite
        public ZRawImage Raw;      // a picture file (texture)
        public float Since;
    }

    static readonly Dictionary<string, Sprite> Sprites = new();
    static readonly Dictionary<string, Pending> Loading = new();
    static readonly HashSet<string> Failed = new();

    /// <summary>Raised when a picture was loaded, so the windows are drawn again.</summary>
    internal static int Version;

    /// <summary>The picture of an address (null while it is loading or when the game has none).</summary>
    internal static Sprite Get(string address)
    {
        if (string.IsNullOrEmpty(address)) return null;
        if (Sprites.TryGetValue(address, out var sprite) && sprite != null) return sprite;
        if (!Loading.ContainsKey(address) && !Failed.Contains(address)) Request(address, address);
        return null;
    }

    static readonly Dictionary<int, string> ProfessionAddress = new();

    /// <summary>
    /// The class picture of a profession (the white class symbol the meter tints with the class colour): the game's own, found in its profession table
    /// (ProfessionSystemTableMgr.Icon, an atlas sprite such as ui/atlas/profession/profession_horizontal_08). Null while it is loading and for class 0.
    /// </summary>
    internal static Sprite Profession(int id)
    {
        if (id <= 0) return null;
        if (!ProfessionAddress.TryGetValue(id, out var address))
        {
            try
            {
                address = GameLua.Query("local row = _G.Z.TableMgr.GetTable('ProfessionSystemTableMgr').GetRow(" + id + ", true) return row and row.Icon or ''");
            }
            catch (Exception)
            {
                return null; // the game's tables are not up yet: asked again at the next draw
            }

            ProfessionAddress[id] = address;
        }

        return address.Length == 0 ? null : Get(address);
    }

    /// <summary>The picture file with this name (a skill, imagine or item picture of the game): null while it is being loaded or when the game has none.</summary>
    internal static Sprite Texture(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var key = "tex:" + name;
        if (Sprites.TryGetValue(key, out var sprite) && sprite != null) return sprite;
        if (Loading.ContainsKey(key) || Failed.Contains(key)) return null;
        var address = AddressBook.AddressOf(name);
        if (address == null) return null; // the lists are still being read (asked again at the next draw), or the game has no such picture
        Request(key, address, raw: true);
        return null;
    }

    /// <summary>
    /// A skill picture of the game by name: the picture file when the game has one, otherwise a sprite of the game's skill atlas (a few skill pictures are only there,
    /// at ui/atlas/skill/&lt;name&gt;). Null while it is loading and when the game has neither.
    /// </summary>
    internal static Sprite Skill(string name)
    {
        var sprite = Texture(name);
        if (sprite != null) return sprite;
        return AddressBook.Ready && AddressBook.AddressOf(name) == null ? Get("ui/atlas/skill/" + name) : null;
    }

    static void Request(string key, string address, bool raw = false)
    {
        try
        {
            var host = new GameObject("StarOverlayImage");
            host.hideFlags = HideFlags.HideAndDontSave;
            var pending = new Pending { Host = host, Since = Time.realtimeSinceStartup };
            Loading[key] = pending;
            if (raw)
            {
                pending.Raw = host.AddComponent<ZRawImage>();
                pending.Raw.SetImage(address);
            }
            else
            {
                pending.Image = host.AddComponent<ZImage>();
                pending.Image.SetImage(address); // the game's loader fills it in a moment; Poll sees when it is there
            }
        }
        catch (Exception)
        {
            Finish(key);
            Failed.Add(key);
        }
    }

    static void Loaded(string address)
    {
        try
        {
            if (!Loading.TryGetValue(address, out var pending)) return;
            var copy = pending.Raw != null ? CopyTexture(pending.Raw.texture) : pending.Image?.sprite == null ? null : Copy(pending.Image.sprite);
            Finish(address);
            if (copy == null)
            {
                Failed.Add(address);
                return;
            }

            Sprites[address] = copy;
            Version++;
        }
        catch (Exception)
        {
            Finish(address);
            Failed.Add(address);
        }
    }

    static Sprite Copy(Sprite source) => Copy(source.texture, source.textureRect);

    static Sprite CopyTexture(Texture source) => source == null ? null : Copy(source, new Rect(0, 0, source.width, source.height));

    static Sprite Copy(Texture from, Rect rect)
    {
        // at most 128 pixels: the pictures are drawn small, and every one that was asked for stays in memory
        var width = Math.Clamp((int)Math.Round(rect.width), 8, 128);
        var height = Math.Clamp((int)Math.Round(rect.height), 8, 128);
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(from, target, new Vector2(rect.width / from.width, rect.height / from.height), new Vector2(rect.x / from.width, rect.y / from.height));
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static void Finish(string address)
    {
        if (!Loading.TryGetValue(address, out var pending)) return;
        Loading.Remove(address);
        if (pending.Host != null) UnityEngine.Object.Destroy(pending.Host);
    }

    /// <summary>Every frame: a picture that has arrived is copied, one that never came is given up.</summary>
    public static void Poll()
    {
        if (Loading.Count == 0) return;
        List<string> arrived = null;
        List<string> late = null;
        foreach (var pair in Loading)
        {
            var ready = pair.Value.Raw != null ? pair.Value.Raw.texture != null : pair.Value.Image != null && pair.Value.Image.sprite != null;
            if (ready) (arrived ??= new List<string>()).Add(pair.Key);
            else if (Time.realtimeSinceStartup - pair.Value.Since > GiveUpSeconds) (late ??= new List<string>()).Add(pair.Key);
        }

        if (arrived != null)
            foreach (var address in arrived) Loaded(address);
        if (late == null) return;
        foreach (var address in late)
        {
            Finish(address);
            Failed.Add(address);
        }
    }
}
