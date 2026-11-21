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
    const int ListIconSize = 36;
    const float Gap = 6f;
    const int Texels = 128;

    static Sprite _disc;
    static Sprite _ring;

    readonly Transform _parent;
    readonly Action<string> _press;
    readonly Action<string> _remove;
    readonly UiRoot _ui;
    readonly List<Cell> _cells = new();
    List<IconDto> _icons = new();
    int _first;  // the first icon that is drawn (a list that is scrolled)
    int _shown;  // how many icons are drawn from there

    /// <param name="press">Called with the key of a row that was pressed (only a pressable list has such rows).</param>
    public IconStrip(Transform parent, Action<string> press = null, Action<string> remove = null, UiRoot ui = null)
    {
        _parent = parent;
        _press = press;
        _remove = remove;
        _ui = ui;
    }

    /// <summary>The size of the picture of a row of a pressable list and the distance from one row to the next.</summary>
    public static (double Size, double Step) ListMetrics(WidgetDto widget)
    {
        var size = widget.IconSize > 0 ? widget.IconSize : ListIconSize;
        return (size, size + Gap);
    }

    /// <summary>Lays the icons out from (0, y) in the width given; returns the height used. A list can show only <paramref name="count"/> rows from the row <paramref name="first"/> on.</summary>
    public double Apply(WidgetDto widget, double y, double width, int first = 0, int count = int.MaxValue)
    {
        _icons = widget.Icons;
        _first = Math.Clamp(first, 0, _icons.Count);
        _shown = Math.Min(count, _icons.Count - _first);
        var list = widget.IconsClickable; // a pressable list: one wide row per icon with its name and on/off
        var size = widget.IconSize > 0 ? widget.IconSize : list ? ListIconSize : DefaultSize;
        var perRow = list || widget.IconsVertical ? 1 : Math.Max(1, (int)((width + Gap) / (size + Gap)));
        for (var i = 0; i < _shown; i++)
        {
            while (_cells.Count <= i) _cells.Add(new Cell(_parent, _press, _remove, _ui));
            var x = i % perRow * (size + Gap);
            var top = y + i / perRow * (size + Gap);
            _cells[i].Place(x, top, size, list ? width : 0, list && widget.IconsRemovable);
            _cells[i].Bind(_icons[_first + i], list, list && widget.IconsRemovable);
        }

        for (var i = _shown; i < _cells.Count; i++) _cells[i].Hide();
        Tick();
        var rows = _shown == 0 ? 0 : (_shown - 1) / perRow + 1;
        return rows * (size + Gap);
    }

    public void Hide()
    {
        foreach (var cell in _cells) cell.Hide();
        _icons = new List<IconDto>();
        _first = 0;
        _shown = 0;
    }

    /// <summary>Every frame: the gauges and the time texts (a text is written only when its second changes).</summary>
    public void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < _shown && i < _cells.Count && _first + i < _icons.Count; i++)
        {
            _cells[i].Tick(_icons[_first + i], now);
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
        readonly GameObject _box;   // the square with the picture, the ring and the texts (the whole cell in a row of icons)
        readonly GameObject _hit;   // the row of a pressable list
        readonly Image _hitBack;
        readonly TextMeshProUGUI _name;
        readonly TextMeshProUGUI _state;
        readonly GameObject _removeButton;
        string _key = "";
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

        public Cell(Transform parent, Action<string> press, Action<string> remove, UiRoot ui)
        {
            _root = UiKit.Node("Icon", parent);
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var rowLabel = UiKit.Button("Row", _root.transform, "", new Color(0.2f, 0.5f, 0.35f, 0.28f), () =>
            {
                if (_key.Length > 0) press?.Invoke(_key);
            });
            _hit = rowLabel.transform.parent.gameObject;
            _hitBack = _hit.GetComponent<Image>();
            UiKit.Stretch(_hit);
            _box = UiKit.Node("Box", _root.transform);
            var art = UiKit.Node("Art", _box.transform); // the picture with its ring: one group, so its opacity does not touch the texts
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
            _center = UiKit.Text("Center", _box.transform, 16, Color.white, TextAlignmentOptions.Center);
            _corner = UiKit.Text("Corner", _box.transform, 12, Color.white, TextAlignmentOptions.BottomRight);
            var removeLabel = UiKit.Button("Remove", _root.transform, "✕", new Color(0.55f, 0.2f, 0.22f, 0.7f), () =>
            {
                if (_key.Length > 0) remove?.Invoke(_key); // deleted at once, no question asked
            });
            _removeButton = removeLabel.transform.parent.gameObject;
            removeLabel.fontSize = 14;
            if (ui != null) UiKit.Hover(_removeButton, () => ui.ShowTip(_removeButton, "목록에서 삭제"), () => ui.HideTip(_removeButton));
            _name = UiKit.Text("Name", _root.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft);
            _state = UiKit.Text("State", _root.transform, 14, Color.white, TextAlignmentOptions.MidlineRight);
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

        const double RemoveSize = 22;

        public void Place(double x, double y, double size, double width, bool removable)
        {
            _size = size;
            UiKit.Place(_root, x, y, width > 0 ? width : size, size);
            UiKit.Place(_box, 0, 0, size, size);
            if (width > 0)
            {
                var end = width - 6 - (removable ? RemoveSize + 6 : 0); // the delete button sits at the very end of the row
                UiKit.Place(_name.gameObject, size + 8, 0, Math.Max(20, end - StateWidth - 4 - size - 8), size);
                UiKit.Place(_state.gameObject, end - StateWidth, 0, StateWidth, size);
                UiKit.Place(_removeButton, width - 6 - RemoveSize, (size - RemoveSize) / 2, RemoveSize, RemoveSize);
                _name.fontSize = (float)Math.Max(12, size * 0.42);
                _state.fontSize = (float)Math.Max(11, size * 0.36);
            }

            var inset = (float)(size * 0.08);
            UiKit.Stretch(_face.gameObject, inset, inset, inset, inset);
            UiKit.Stretch(_center.gameObject);
            UiKit.Place(_corner.gameObject, 0, 0, size - 2, size - 1);
            _center.fontSize = (float)(size * 0.36);
            _corner.fontSize = (float)(size * 0.28);
            _letter.fontSize = (float)(size * 0.45);
        }

        const double StateWidth = 44;

        public void Bind(IconDto icon, bool list, bool removable)
        {
            _removeButton.SetActive(removable);
            _root.SetActive(true);
            _key = list ? icon.Key : "";
            _group.blocksRaycasts = list; // only a row of a pressable list takes the mouse
            _hit.SetActive(list);
            _name.gameObject.SetActive(list);
            _state.gameObject.SetActive(list);
            if (list)
            {
                _hitBack.color = icon.Dim ? new Color(0.35f, 0.35f, 0.4f, 0.22f) : new Color(0.2f, 0.5f, 0.35f, 0.32f);
                UiKit.SetText(_name, icon.Name);
                UiKit.SetText(_state, icon.Dim ? "<color=#9ea9bb>꺼짐</color>" : "<color=#7be39a>켜짐</color>");
            }

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
