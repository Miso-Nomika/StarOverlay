using System;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>The check and the cross of the accept and reject buttons, drawn here (white on clear), so they show whatever the font has.</summary>
static class MarkArt
{
    const int Size = 64;
    const float Half = 0.085f; // half the thickness of a stroke, in the unit square
    static Sprite _check;
    static Sprite _cross;

    internal static Sprite Check => _check ??= Make([(0.2f, 0.5f), (0.42f, 0.27f), (0.82f, 0.74f)]);

    internal static Sprite Cross => _cross ??= Make([(0.24f, 0.24f), (0.76f, 0.76f)], [(0.24f, 0.76f), (0.76f, 0.24f)]);

    /// <summary>A sprite of the strokes, each a list of corner points (the unit square, y up) joined by lines with round ends.</summary>
    static Sprite Make(params (float X, float Y)[][] strokes)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var px = (x + 0.5f) / Size;
                var py = (y + 0.5f) / Size;
                var near = float.MaxValue;
                foreach (var stroke in strokes)
                    for (var i = 0; i + 1 < stroke.Length; i++)
                        near = Math.Min(near, Distance(px, py, stroke[i], stroke[i + 1]));
                var alpha = Mathf.Clamp01(0.5f + (Half - near) * Size); // one pixel of soft edge
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    static float Distance(float px, float py, (float X, float Y) a, (float X, float Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var t = Mathf.Clamp01(((px - a.X) * dx + (py - a.Y) * dy) / (dx * dx + dy * dy));
        var cx = a.X + t * dx - px;
        var cy = a.Y + t * dy - py;
        return Mathf.Sqrt(cx * cx + cy * cy);
    }
}
