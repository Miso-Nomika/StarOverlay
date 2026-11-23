using System;
using System.Collections.Generic;
using System.Linq;
using StarOverlay.Ingame.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Picture chat messages arrive as text like "emojiPic=%s=%ssticker_17_317003". The game draws the picture in its own chat
/// bubbles (hidden or not), so the picture is copied from there into a texture of ours, keyed by the sticker name.
/// </summary>
static class StickerCache
{
    static readonly Dictionary<string, Texture2D> _textures = new();
    static float _next;

    /// <summary>Raised when a new picture was copied, so the chat window redraws.</summary>
    internal static int Version;

    /// <summary>The sticker name inside a chat text (null when the line is not a picture message).</summary>
    internal static string NameOf(string text)
    {
        return StickerPairing.NameOf(text);
    }

    internal static Texture2D Get(string name) => name != null && _textures.TryGetValue(name, out var texture) && texture != null ? texture : null;

    static (List<ChatLineDto> Lines, int Count, int Version) _looked;

    public static void Poll()
    {
        if (Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 0.2f;
        try
        {
            var lines = GameChatUi.Lines;
            if (lines == null) return;
            // the same lines and the same pictures as at the last look: nothing new to ask for (the names are read with a pattern match, line by line)
            var signature = (lines, lines.Count, Version);
            if (signature == _looked) return;
            _looked = signature;
            var wanted = new List<string>();
            foreach (var line in lines)
            {
                var name = NameOf(line.Text);
                if (name != null && Get(name) == null) wanted.Add(name);
            }

            if (wanted.Count == 0) return;
            // the picture file of the client is loaded by its name (no chat bubble of the game is needed)
            StickerLoader.Prepare();
            StickerLoader.Watch();
            foreach (var name in wanted) StickerLoader.Request(name);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("sticker cache: " + e.Message);
        }
    }

    // The series parts of the stickers seen so far, per pack (the "8" of 118010 for the texture sticker_3_11): the first ones were seen in the game, the
    // rest is learned from the stickers copied or loaded. They tell two pictures apart that both fit a name (emoji_2_266019: emoji_2_2 would be series "66").
    static readonly Dictionary<string, HashSet<string>> SeriesOfPack = new()
    {
        ["emoji_2"] = ["12", "6"], ["sticker_1"] = ["3"], ["sticker_3"] = ["8"], ["sticker_4"] = ["9"], ["sticker_5"] = ["10"],
        ["sticker_7"] = ["11"], ["sticker_16"] = ["15"], ["sticker_17"] = ["17"], ["sticker_18"] = ["18"],
    };

    /// <summary>The name of the game's picture a sticker was copied from (null when not copied in this session).</summary>
    internal static string TextureOfName(string code) => code != null && _textureOfName.TryGetValue(code, out var texture) ? texture : null;

    internal static void RememberTexture(string code, string texture) => _textureOfName[code] = texture;

    internal static bool SeriesKnown(string code, string series)
    {
        var pack = StickerPairing.PackOf(code);
        return pack != null && series != null && SeriesOfPack.TryGetValue(pack, out var known) && known.Contains(series);
    }

    internal static void LearnSeries(string code, string texture)
    {
        var pack = StickerPairing.PackOf(code);
        var series = StickerPairing.SeriesOf(code, texture);
        if (pack == null || series == null) return;
        if (!SeriesOfPack.TryGetValue(pack, out var known)) SeriesOfPack[pack] = known = new HashSet<string>();
        known.Add(series);
    }
    static readonly Dictionary<string, string> _textureOfName = new(); // sticker name -> the texture it was copied from

#if DEV_TOOLS
    static string _lastDiag;
    static int _diagBlocks;
#endif

    /// <summary>Development log (bpsr\state\sticker-diag.txt). A release build (DEV_TOOLS is not defined) has neither the calls nor the writer.</summary>
    [System.Diagnostics.Conditional("DEV_TOOLS")]
    internal static void Diag(string text)
    {
#if DEV_TOOLS
        if (text == _lastDiag || _diagBlocks >= 400) return;
        _lastDiag = text;
        _diagBlocks++;
        var folder = System.IO.Path.Combine(Plugin.GameRoot, "bpsr", "state");
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "sticker-diag.txt"), $"--- {DateTime.Now:HH:mm:ss.fff}\n{text}");
#endif
    }

    /// <summary>Copies the picture a RawImage shows into the cache under a sticker name (for the loader).</summary>
    internal static bool CopyFrom(string name, RawImage source) => Get(name) != null || Copy(name, source);

    static bool Copy(string name, RawImage source)
    {
        var from = source.texture;
        var uv = source.uvRect;
        var width = Math.Clamp((int)Math.Round(from.width * uv.width), 8, 256);
        var height = Math.Clamp((int)Math.Round(from.height * uv.height), 8, 256);
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(from, target, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            _textures[name] = texture;
            return true;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

}
