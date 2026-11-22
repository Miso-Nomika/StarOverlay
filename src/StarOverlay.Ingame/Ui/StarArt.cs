using System;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>The star of the favourite button, drawn here (a filled and an outlined one), so it shows whatever the font has.</summary>
static class StarArt
{
    const int Size = 64;
    static Texture2D _filled;
    static Texture2D _outline;

    internal static Texture2D Filled => _filled ??= Make(false);

    internal static Texture2D Outline => _outline ??= Make(true);

    static Texture2D Make(bool outline)
    {
        var points = Polygon(1f);
        var inner = Polygon(0.62f);
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // 4x4 samples per pixel: smooth edges
                var covered = 0;
                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        var px = x + (sx + 0.5f) / 4f;
                        var py = y + (sy + 0.5f) / 4f;
                        var inside = Inside(points, px, py);
                        if (outline && inside && Inside(inner, px, py)) inside = false;
                        if (inside) covered++;
                    }
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, covered / 16f));
            }
        }

        texture.Apply();
        return texture;
    }

    // five points, one up; the inner points sit at 0.42 of the outer radius; scaled around the middle
    static Vector2[] Polygon(float scale)
    {
        var result = new Vector2[10];
        var middle = new Vector2(Size / 2f, Size / 2f - 1.5f);
        for (var i = 0; i < 10; i++)
        {
            var radius = (i % 2 == 0 ? 0.47f : 0.47f * 0.42f) * Size * scale;
            var angle = Math.PI / 2 + i * Math.PI / 5;
            result[i] = new Vector2(middle.x + (float)Math.Cos(angle) * radius, middle.y + (float)Math.Sin(angle) * radius);
        }

        return result;
    }

    static bool Inside(Vector2[] polygon, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].y > y) != (polygon[j].y > y) && x < (polygon[j].x - polygon[i].x) * (y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside;
        }

        return inside;
    }
}
