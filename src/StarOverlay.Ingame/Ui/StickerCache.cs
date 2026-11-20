using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Picture chat messages arrive as text like "emojiPic=%s=%ssticker_17_317003". The game draws the picture in its own chat
/// bubbles (hidden or not), so the picture is copied from there into a texture of ours, keyed by the sticker name.
/// </summary>
static class StickerCache
{
    static readonly Regex Code = new(@"[A-Za-z]+_\d+_\d+", RegexOptions.Compiled); // sticker_17_317003, emoji_2_2012005: the picture's name in the game
    static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    static readonly Dictionary<string, Texture2D> _textures = new();
    static float _next;

    /// <summary>Raised when a new picture was copied, so the chat window redraws.</summary>
    internal static int Version;

    /// <summary>The sticker name inside a chat text (null when the line is not a picture message).</summary>
    internal static string NameOf(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("emojiPic=", StringComparison.Ordinal)) return null;
        var match = Code.Match(text);
        return match.Success ? match.Value : null;
    }

    internal static Texture2D Get(string name) => name != null && _textures.TryGetValue(name, out var texture) && texture != null ? texture : null;

    public static void Poll()
    {
        if (Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 0.2f;
        try
        {
            var lines = GameChatUi.Lines;
            if (lines == null) return;
            var wanted = new List<ChatLineDto>();
            foreach (var line in lines)
            {
                var name = NameOf(line.Text);
                if (name != null && Get(name) == null) wanted.Add(line);
            }

            if (wanted.Count == 0) return;
            var chat = GameChatUi.FindChat();
            if (chat == null) return;
            var list = GameChatUi.Find(chat, "node_dialogue");
            var content = list == null ? null : GameChatUi.Find(list, "node_content");
            if (content == null) return;

            // the picture bubbles of the game's list, oldest first as the game orders them on screen
            var bubbles = new List<(Transform Node, float Y)>();
            for (var i = 0; i < content.childCount; i++)
            {
                var node = content.GetChild(i);
                if (node.gameObject.activeSelf && node.name.Contains("_picture")) bubbles.Add((node, node.position.y));
            }

            bubbles.Sort((a, b) => b.Y.CompareTo(a.Y)); // top of the screen first = oldest first
            var copiedAny = false;
            var pictures = new List<(string Who, RawImage Image)>();
            foreach (var (node, _) in bubbles)
            {
                var image = GameChatUi.Find(node, "rimg_pic")?.GetComponent<RawImage>();
                if (image == null || image.texture == null) continue;
                var who = GameChatUi.Find(node, "lab_name")?.GetComponent<TMP_Text>();
                pictures.Add((who == null ? "" : Squash(Tags.Replace(who.text ?? "", "")), image));
                var byName = Code.Match(image.texture.name ?? "");
                if (byName.Success && Get(byName.Value) == null && Copy(byName.Value, image)) copiedAny = true; // the texture carries the sticker name
            }

            // otherwise pair them by order: the newest picture bubble with the newest picture line of the same sender
            var linesNewestFirst = new List<ChatLineDto>(wanted);
            linesNewestFirst.Reverse();
            var next = pictures.Count - 1;
            foreach (var line in linesNewestFirst)
            {
                var name = NameOf(line.Text);
                if (Get(name) != null) continue;
                while (next >= 0 && pictures[next].Who != Squash(line.Name)) next--;
                if (next < 0) break;
                if (Copy(name, pictures[next].Image)) copiedAny = true;
                next--;
            }

            if (copiedAny) Version++;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("sticker cache: " + e.Message);
        }
    }

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

    static string Squash(string text) => Regex.Replace(text ?? "", @"\s+", "");
}
