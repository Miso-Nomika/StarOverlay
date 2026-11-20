using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The pictures of the game's own chat channel tabs (all, world, guild, party, local, newbie), copied from the game's chat once
/// it is on the screen and kept as PNG files in bpsr\tab-icons, so the chat window's channel buttons look like the game's.
/// </summary>
static class TabIcons
{
    public const int Tabs = 6;
    static readonly Sprite[] _bg = new Sprite[Tabs];
    static readonly Sprite[] _on = new Sprite[Tabs];
    static readonly Sprite[] _off = new Sprite[Tabs];
    static bool _diskRead;
    static bool _complete;
    static float _next;

    /// <summary>Raised when pictures became available, so the rail is painted again.</summary>
    internal static int Version;

    static string Dir => Path.Combine(Plugin.GameRoot, "bpsr", "tab-icons");

    internal static bool Has(int tab) => tab >= 0 && tab < Tabs && _on[tab] != null && _off[tab] != null;

    internal static Sprite Back(int tab) => tab >= 0 && tab < Tabs ? _bg[tab] : null;

    internal static Sprite On(int tab) => tab >= 0 && tab < Tabs ? _on[tab] : null;

    internal static Sprite Off(int tab) => tab >= 0 && tab < Tabs ? _off[tab] : null;

    public static void Poll()
    {
        if (_complete || Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 1f;
        try
        {
            if (!_diskRead)
            {
                _diskRead = true;
                var found = 0;
                for (var i = 0; i < Tabs; i++)
                {
                    _bg[i] = UiKit.LoadSprite(Path.Combine(Dir, $"tab{i}-bg.png"));
                    _on[i] = UiKit.LoadSprite(Path.Combine(Dir, $"tab{i}-on.png"));
                    _off[i] = UiKit.LoadSprite(Path.Combine(Dir, $"tab{i}-off.png"));
                    if (Has(i)) found++;
                }

                if (found > 0) Version++;
                if (found == Tabs)
                {
                    _complete = true;
                    return;
                }
            }

            var chat = GameChatUi.FindChat();
            var content = chat == null ? null : GameChatUi.Find(GameChatUi.Find(chat, "loop_channel"), "content");
            if (content == null) return;
            var tabs = new System.Collections.Generic.List<(Transform Node, float Y)>();
            for (var i = 0; i < content.childCount; i++)
            {
                var node = content.GetChild(i);
                if (node.name.StartsWith("main_chat_channel_tpl_pc", StringComparison.Ordinal)) tabs.Add((node, node.position.y));
            }

            if (tabs.Count < Tabs) return;
            tabs.Sort((a, b) => b.Y.CompareTo(a.Y)); // top to bottom: all, world, guild, party, local, newbie
            var copied = 0;
            for (var i = 0; i < Tabs; i++)
            {
                var on = GameChatUi.Find(tabs[i].Node, "group_on");
                var off = GameChatUi.Find(tabs[i].Node, "group_off");
                _bg[i] ??= Copy(Find(on, "img_bg"), $"tab{i}-bg");
                _on[i] ??= Copy(Find(on, "img_icon"), $"tab{i}-on");
                _off[i] ??= Copy(Find(off, "img_icon"), $"tab{i}-off");
                if (Has(i)) copied++;
            }

            if (copied > 0) Version++;
            if (copied == Tabs) _complete = true;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("tab icons: " + e.Message);
        }
    }

    static Image Find(Transform parent, string name) => GameChatUi.Find(parent, name)?.GetComponent<Image>();

    /// <summary>Copies a picture of the game into a texture of ours and writes it to disk.</summary>
    static Sprite Copy(Image image, string name)
    {
        var sprite = image == null ? null : image.sprite;
        if (sprite == null || sprite.texture == null) return null;
        var from = sprite.texture;
        var rect = sprite.textureRect;
        var width = Math.Clamp((int)Math.Round(rect.width), 4, 256);
        var height = Math.Clamp((int)Math.Round(rect.height), 4, 256);
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
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), ImageConversion.EncodeToPNG(texture));
            var copy = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            copy.hideFlags = HideFlags.HideAndDontSave;
            return copy;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
