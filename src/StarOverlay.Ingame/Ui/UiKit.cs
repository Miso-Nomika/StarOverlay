using System;
using System.IO;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>Small helpers to build UGUI objects from code. All text uses one font (PretendardJP shipped with the agent).</summary>
internal static class UiKit
{
    static TMP_FontAsset _font;
    static bool _fontTried;

    // fonts of the system that are tried, in this order, for a character the main font has no picture of (symbols such as ☪ ꙳ ┈ and rare letters)
    static readonly string[] FallbackFiles =
    {
        "seguisym.ttf",  // Segoe UI Symbol: symbols, box drawing, many dingbats
        "segoeui.ttf",   // Segoe UI: Cyrillic extended and more
        "seguihis.ttf",  // Segoe UI Historic
        "malgun.ttf",    // Malgun Gothic
        "YuGothM.ttc",   // Yu Gothic
        "msgothic.ttc",  // MS Gothic: box drawing and Japanese symbols
        "arialuni.ttf",  // Arial Unicode MS (when installed)
    };

    static void AddFallbackFonts(TMP_FontAsset main)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            main.fallbackFontAssetTable ??= new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
            var added = new System.Collections.Generic.List<string>();
            foreach (var file in FallbackFiles)
            {
                var path = Path.Combine(folder, file);
                if (!File.Exists(path)) continue;
                var asset = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                if (asset == null) continue;
                asset.hideFlags = HideFlags.HideAndDontSave;
                main.fallbackFontAssetTable.Add(asset);
                added.Add(file);
            }

        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("font fallbacks failed: " + e.Message);
        }
    }

    public static TMP_FontAsset Font
    {
        get
        {
            if (_fontTried) return _font;
            _fontTried = true;
            try
            {
                var path = Path.Combine(Plugin.PluginDirectory, "fonts", "PretendardJP-Bold.otf");
                if (File.Exists(path))
                    _font = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                if (_font != null) AddFallbackFonts(_font);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("font failed: " + e.Message);
            }

            return _font;
        }
    }

    static readonly System.Collections.Generic.Dictionary<string, Sprite> Icons = new();

    /// <summary>A header icon (white on transparent PNG shipped in the agent's icons folder), or null when it cannot be loaded.</summary>
    public static Sprite Icon(string name) => LoadSprite(Path.Combine(Plugin.PluginDirectory, "icons", name + ".png"));

    static readonly System.Collections.Generic.Dictionary<string, Sprite> Sliced9 = new();

    /// <summary>A rounded image that stretches without distorting its corners (<paramref name="border"/> pixels stay fixed).</summary>
    public static Sprite NineSlice(string name, float border)
    {
        if (Sliced9.TryGetValue(name, out var cached)) return cached;
        Sprite sprite = null;
        try
        {
            var path = Path.Combine(Plugin.PluginDirectory, "icons", name + ".png");
            if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
                    Keep(texture, sprite);
                }
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("nine-slice " + name + ": " + e.Message);
        }

        Sliced9[name] = sprite;
        return sprite;
    }

    /// <summary>A PNG file as a sprite (cached; also remembers a miss so a missing file is looked up only once).</summary>
    /// <summary>Runtime-made images that no active Image uses are unloaded by Unity at the next scene change; the cache would then hold dead sprites (blank squares). Marking them keeps them.</summary>
    static void Keep(Texture2D texture, Sprite sprite)
    {
        texture.hideFlags = HideFlags.HideAndDontSave;
        sprite.hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>A PNG file as a sprite (cached once found; a missing file is looked up again next time and warned about once).</summary>
    public static Sprite LoadSprite(string path)
    {
        var name = path;
        if (Icons.TryGetValue(name, out var cached)) return cached;
        Sprite sprite = null;
        try
        {
            if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    Keep(texture, sprite);
                }
            }

            if (sprite == null) Plugin.L.LogWarning("icon not loaded: " + name);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("icon " + name + ": " + e.Message);
        }

        Icons[name] = sprite;
        return sprite;
    }

    /// <summary>A picture the plugin names as "file:<absolute path>" (a bundled or user PNG); null for anything else.</summary>
    public static Sprite Picture(string reference) =>
        !string.IsNullOrEmpty(reference) && reference.StartsWith("file:", StringComparison.Ordinal) ? LoadSprite(reference.Substring(5)) : null;

    /// <summary>Puts an icon image over a button (the button's own label is cleared).</summary>
    public static void SetIcon(TextMeshProUGUI label, Sprite sprite)
    {
        label.text = "";
        var go = Node("Icon", label.transform.parent);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = true;
        Stretch(go, 4, 3, 4, 3);
    }

    public static GameObject Node(string name, Transform parent)
    {
        var go = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        go.transform.SetParent(parent, false);
        return go;
    }

    public static RectTransform Rt(GameObject go) => go.GetComponent<RectTransform>();

    /// <summary>Top-left anchored rectangle in canvas reference pixels.</summary>
    public static void Place(GameObject go, double x, double y, double w, double h)
    {
        var rt = Rt(go);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2((float)x, (float)-y);
        rt.sizeDelta = new Vector2((float)w, (float)h);
    }

    public static void Stretch(GameObject go, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        var rt = Rt(go);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    public static Image Box(string name, Transform parent, Color color, bool raycast = false)
    {
        var go = Node(name, parent);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var go = Node(name, parent);
        var text = go.AddComponent<TextMeshProUGUI>();
        var font = Font;
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = true;
        return text;
    }

    public static void SetText(TextMeshProUGUI text, string value)
    {
        if (text.text != value) text.text = value;
    }

    /// <summary>A flat button with a text label. Returns the label so the caller can change the caption.</summary>
    public static TextMeshProUGUI Button(string name, Transform parent, string caption, Color background, Action click)
    {
        var box = Box(name, parent, background, raycast: true);
        var button = box.gameObject.AddComponent<Button>();
        button.onClick.AddListener((UnityAction)(() =>
        {
            try
            {
                click();
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("button " + name + ": " + e.Message);
            }
        }));
        var label = Text("Label", box.transform, 14, Color.white, TextAlignmentOptions.Center);
        Stretch(label.gameObject);
        label.text = caption;
        return label;
    }

    /// <summary>Calls <paramref name="delta"/> with the pointer movement in reference pixels while dragging, <paramref name="end"/> when released.</summary>
    public static void Drag(GameObject go, Action<Vector2> delta, Action end)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        drag.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            try
            {
                var pointer = data.TryCast<PointerEventData>();
                if (pointer == null) return;
                var (dx, dy) = StarOverlay.Ingame.Protocol.LayoutMath.FromPointerDelta(pointer.delta.x, pointer.delta.y, UiRoot.Scale);
                delta(new Vector2((float)dx, (float)dy));
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("drag: " + e.Message);
            }
        }));
        trigger.triggers.Add(drag);
        var up = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
        up.callback.AddListener((UnityAction<BaseEventData>)(_ =>
        {
            try
            {
                end();
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("drag end: " + e.Message);
            }
        }));
        trigger.triggers.Add(up);
    }

    /// <summary>Calls <paramref name="enter"/> when the cursor rests on <paramref name="go"/> and <paramref name="exit"/> when it leaves.</summary>
    public static void Hover(GameObject go, Action enter, Action exit)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var on = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        on.callback.AddListener((UnityAction<BaseEventData>)(_ => Guard(enter)));
        trigger.triggers.Add(on);
        var off = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        off.callback.AddListener((UnityAction<BaseEventData>)(_ => Guard(exit)));
        trigger.triggers.Add(off);
        // an EventTrigger takes the wheel for itself even without a wheel entry: a list under the cursor would stop scrolling while a note is
        // shown, so the wheel is handed on to the parent (the list's scroll region)
        var wheel = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
        wheel.callback.AddListener((UnityAction<BaseEventData>)(data => Guard(() =>
        {
            var parent = go.transform.parent;
            if (parent != null) ExecuteEvents.ExecuteHierarchy(parent.gameObject, data, ExecuteEvents.scrollHandler);
        })));
        trigger.triggers.Add(wheel);
    }

    static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("hover: " + e.Message);
        }
    }

    /// <summary>Calls <paramref name="steps"/> with +1 (wheel down) or -1 (wheel up) when the wheel turns over <paramref name="go"/> or a child.</summary>
    public static void Scroll(GameObject go, Action<int> steps)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
        entry.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            try
            {
                var pointer = data.TryCast<PointerEventData>();
                if (pointer == null || pointer.scrollDelta.y == 0) return;
                steps(pointer.scrollDelta.y > 0 ? -1 : 1);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("scroll: " + e.Message);
            }
        }));
        trigger.triggers.Add(entry);
    }

    const int WheelRows = 3;

    /// <summary>
    /// Scrolling for a list: the wheel moves three rows per turn, and dragging the list moves it like a touch screen
    /// (drag down = earlier rows). <paramref name="rows"/> gets the change in rows (negative = up).
    /// </summary>
    public static void Scroller(GameObject go, double rowHeight, Action<int> rows) => Scroller(go, () => rowHeight, rows);

    /// <summary>Same, for lists whose row height changes (the meter): the height is asked for at every move.</summary>
    public static void Scroller(GameObject go, Func<double> rowHeight, Action<int> rows)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var carry = 0.0;

        var wheel = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
        wheel.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            try
            {
                var pointer = data.TryCast<PointerEventData>();
                if (pointer == null || pointer.scrollDelta.y == 0) return;
                rows(pointer.scrollDelta.y > 0 ? -WheelRows : WheelRows);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("wheel: " + e.Message);
            }
        }));
        trigger.triggers.Add(wheel);

        var begin = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
        begin.callback.AddListener((UnityAction<BaseEventData>)(_ => carry = 0));
        trigger.triggers.Add(begin);

        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        drag.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            try
            {
                var pointer = data.TryCast<PointerEventData>();
                if (pointer == null) return;
                var (_, dy) = StarOverlay.Ingame.Protocol.LayoutMath.FromPointerDelta(pointer.delta.x, pointer.delta.y, UiRoot.Scale);
                var height = Math.Max(8, rowHeight());
                // a quick flick travels further: the faster the pointer moves, the more rows each pixel is worth (up to 6x)
                var boost = Math.Clamp(1 + Math.Abs(dy) / (height * 2), 1, 6);
                carry += dy * boost;
                var whole = (int)(carry / height);
                if (whole == 0) return;
                carry -= whole * height;
                rows(-whole);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("list drag: " + e.Message);
            }
        }));
        trigger.triggers.Add(drag);
    }
    public static Color Rgb(int value, float alpha = 1f) =>
        new(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f, alpha);

    public static string Hex(int value) => (value & 0xFFFFFF).ToString("X6");
}
