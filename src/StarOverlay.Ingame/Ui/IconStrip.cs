using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The "icons" widget: round icons in a row (wrapping into more rows), each with a picture inside a circle, a ring gauge around it,
/// the remaining time or a text in the middle and the stacks in the lower right. The picture and the ring are made in code, no files.
/// The remaining time is counted here every frame from the end time the service sent, so the snapshot does not change while a buff runs.
/// </summary>
internal sealed class IconStrip
{
    const int DefaultSize = 48;
    const float Gap = 6f;
    const int Texels = 128;

    static Sprite _disc;
    static Sprite _ring;

    readonly Transform _parent;
    readonly List<Cell> _cells = new();
    List<IconDto> _icons = new();

    public IconStrip(Transform parent) => _parent = parent;

    /// <summary>Lays the icons out from (0, y) in the width given; returns the height used.</summary>
    public double Apply(WidgetDto widget, double y, double width)
    {
        _icons = widget.Icons;
        var size = widget.IconSize > 0 ? widget.IconSize : DefaultSize;
        var perRow = widget.IconsVertical ? 1 : Math.Max(1, (int)((width + Gap) / (size + Gap)));
        for (var i = 0; i < _icons.Count; i++)
        {
            while (_cells.Count <= i) _cells.Add(new Cell(_parent));
            var x = i % perRow * (size + Gap);
            var top = y + i / perRow * (size + Gap);
            _cells[i].Place(x, top, size);
            _cells[i].Bind(_icons[i]);
        }

        for (var i = _icons.Count; i < _cells.Count; i++) _cells[i].Hide();
        Tick();
        var rows = _icons.Count == 0 ? 0 : (_icons.Count - 1) / perRow + 1;
        return rows * (size + Gap);
    }

    public void Hide()
    {
        foreach (var cell in _cells) cell.Hide();
        _icons = new List<IconDto>();
    }

    /// <summary>Every frame: the gauges and the time texts (a text is written only when its second changes).</summary>
    public void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < _icons.Count && i < _cells.Count; i++)
        {
            _cells[i].Tick(_icons[i], now);
        }
    }

    static Sprite Disc()
    {
        return _disc ??= Make(0f);
    }

    static Sprite Ring()
    {
        return _ring ??= Make(0.84f);
    }

    /// <summary>A white circle (inner = 0) or ring with a soft edge; the inner edge is the share of the radius where the ring starts.</summary>
    static Sprite Make(float inner)
    {
        var texture = new Texture2D(Texels, Texels, TextureFormat.RGBA32, false);
        var radius = Texels / 2f;
        for (var y = 0; y < Texels; y++)
        {
            for (var x = 0; x < Texels; x++)
            {
                var d = new Vector2(x + 0.5f - radius, y + 0.5f - radius).magnitude / radius;
                var alpha = Mathf.Clamp01((1f - d) * radius); // 1 pixel soft outer edge
                if (inner > 0f) alpha *= Mathf.Clamp01((d - inner) * radius);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;
        var sprite = Sprite.Create(texture, new Rect(0, 0, Texels, Texels), new Vector2(0.5f, 0.5f), 100f);
        texture.hideFlags = HideFlags.HideAndDontSave;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    sealed class Cell
    {
        readonly GameObject _root;
        readonly CanvasGroup _group;
        readonly CanvasGroup _art;
        readonly Image _track;
        readonly Image _gauge;
        readonly Image _face;
        readonly Image _picture;
        readonly TextMeshProUGUI _letter;
        readonly TextMeshProUGUI _center;
        readonly TextMeshProUGUI _corner;
        double _size;
        string _lastCenter = "";

        public Cell(Transform parent)
        {
            _root = UiKit.Node("Icon", parent);
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var art = UiKit.Node("Art", _root.transform); // the picture with its ring: one group, so its opacity does not touch the texts
            UiKit.Stretch(art);
            _art = art.AddComponent<CanvasGroup>();
            _art.blocksRaycasts = false;
            _track = UiKit.Box("Track", art.transform, new Color(1f, 1f, 1f, 0.22f));
            _track.sprite = Ring();
            UiKit.Stretch(_track.gameObject);
            _gauge = UiKit.Box("Gauge", art.transform, Color.white);
            _gauge.sprite = Ring();
            _gauge.type = Image.Type.Filled;
            _gauge.fillMethod = Image.FillMethod.Radial360;
            _gauge.fillOrigin = 2; // from the top
            _gauge.fillClockwise = false;
            UiKit.Stretch(_gauge.gameObject);
            _face = UiKit.Box("Face", art.transform, new Color(0.16f, 0.18f, 0.22f, 1f));
            _face.sprite = Disc();
            _face.gameObject.AddComponent<Mask>().showMaskGraphic = true; // the picture is cut to the circle
            _picture = UiKit.Box("Picture", _face.transform, Color.white);
            _picture.preserveAspect = true;
            UiKit.Stretch(_picture.gameObject);
            _letter = UiKit.Text("Letter", _face.transform, 20, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.Center);
            UiKit.Stretch(_letter.gameObject);
            _center = UiKit.Text("Center", _root.transform, 16, Color.white, TextAlignmentOptions.Center);
            _corner = UiKit.Text("Corner", _root.transform, 12, Color.white, TextAlignmentOptions.BottomRight);
            Outline(_center);
            Outline(_corner);
        }

        static void Outline(TextMeshProUGUI text)
        {
            try
            {
                text.outlineWidth = 0.4f; // a thicker dark edge keeps the digits readable over any picture
                text.outlineColor = new Color32(0, 0, 0, 255);
            }
            catch (Exception)
            {
            }
        }

        public void Place(double x, double y, double size)
        {
            _size = size;
            UiKit.Place(_root, x, y, size, size);
            var inset = (float)(size * 0.08);
            UiKit.Stretch(_face.gameObject, inset, inset, inset, inset);
            UiKit.Stretch(_center.gameObject);
            UiKit.Place(_corner.gameObject, 0, 0, size - 2, size - 1);
            _center.fontSize = (float)(size * 0.36);
            _corner.fontSize = (float)(size * 0.28);
            _letter.fontSize = (float)(size * 0.45);
        }

        public void Bind(IconDto icon)
        {
            _root.SetActive(true);
            var sprite = UiKit.Picture(icon.Image);
            _picture.sprite = sprite;
            _picture.gameObject.SetActive(sprite != null);
            _letter.gameObject.SetActive(sprite == null);
            UiKit.SetText(_letter, icon.Name.Length == 0 ? "" : icon.Name.Substring(0, 1));
            _gauge.color = UiKit.Rgb(icon.Color == 0 ? 0x4FC3F7 : icon.Color);
            _art.alpha = Math.Clamp(icon.Alpha, 0, 100) / 100f; // the picture, its ring and its track: see-through on their own, the texts keep theirs
            _center.alpha = Math.Clamp(icon.TimeAlpha, 0, 100) / 100f; // the time and the stacks are drawn at their own opacity
            _corner.alpha = Math.Clamp(icon.StacksAlpha, 0, 100) / 100f;
            UiKit.SetText(_corner, icon.Corner);
            _lastCenter = null;
        }

        public void Hide() => _root.SetActive(false);

        public void Tick(IconDto icon, long nowMs)
        {
            var counting = icon.ShowTime && icon.EndsAtMs > 0;
            var remaining = counting ? Math.Max(0, (icon.EndsAtMs - nowMs) / 1000.0) : 0;
            var text = counting ? Format(remaining) : icon.Text;
            if (text != _lastCenter)
            {
                _lastCenter = text;
                UiKit.SetText(_center, text);
            }

            var gauge = icon.Gauge;
            var show = gauge is "ring" or "ring-reverse";
            _track.gameObject.SetActive(show);
            _gauge.gameObject.SetActive(show && !icon.Dim);
            if (!show || icon.Dim) return;
            float share;
            if (icon.EndsAtMs > 0 && icon.TotalMs > 0) share = Mathf.Clamp01((float)((icon.EndsAtMs - nowMs) / (double)icon.TotalMs));
            else share = icon.EndsAtMs > 0 ? 0f : 1f; // no duration: a full ring while the buff is there
            var fill = gauge == "ring-reverse" ? 1f - share : share;
            if (Mathf.Abs(_gauge.fillAmount - fill) > 0.004f) _gauge.fillAmount = fill; // a change redraws the canvas: only when it is visible
        }

        static string Format(double seconds)
        {
            if (seconds >= 60) return $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
            return seconds >= 10 ? Math.Ceiling(seconds).ToString("0") : seconds.ToString("0.0");
        }
    }
}
