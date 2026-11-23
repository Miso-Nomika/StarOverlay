using System;
using System.Collections.Generic;
using System.Linq;
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
    static Sprite _outerRing; // the outer half of the ring's width (a combined icon has two rings)
    static Sprite _innerRing; // the inner half: drawn in a box that is 4% smaller on every side, so it lies just inside the outer one

    readonly Transform _parent;
    readonly Action<string> _press;
    readonly Action<string> _remove;
    readonly UiRoot _ui;
    readonly List<Cell> _cells = new();
    readonly List<TextMeshProUGUI> _headers = new(); // the titles above the columns of a grouped list
    TextMeshProUGUI _badge;  // the short text at the right end of the title row
    List<IconDto> _placed = new(); // the icon of every cell that is drawn, in the order of the cells

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
        return (size, size + GapOf(widget));
    }

    /// <summary>The space between two rows of a list: the one the plugin asked for, or the default.</summary>
    static double GapOf(WidgetDto widget) => widget.IconGap >= 0 ? widget.IconGap : Gap;

    const int DefaultHeaderSize = 14;

    static int HeaderSizeOf(WidgetDto widget) => widget.HeaderSize > 0 ? widget.HeaderSize : DefaultHeaderSize;

    /// <summary>The height of the titles above the columns of a grouped list (0 for a list without columns): the text size and a little room.</summary>
    public static double HeaderHeightOf(WidgetDto widget) => Grouped(widget) ? Math.Max(14, HeaderSizeOf(widget) + 6) : 0;

    /// <summary>A pressable list whose rows name a group (<see cref="IconDto.Group"/>) is drawn in columns, one per group side by side, each with its title above.</summary>
    public static bool Grouped(WidgetDto widget) => widget.IconsClickable && widget.Icons.Any(icon => icon.Group.Length > 0);

    static List<List<IconDto>> Columns(WidgetDto widget) => widget.Icons.GroupBy(icon => icon.Group).Select(group => group.ToList()).ToList();

    /// <summary>How many rows a list has: its icons, or in columns the rows of the longest column (all columns scroll together).</summary>
    public static int TotalRows(WidgetDto widget) => Grouped(widget) ? Columns(widget).Max(column => column.Count) : widget.Icons.Count;

    /// <summary>Lays the icons out from (0, y) in the width given; returns the height used. A list can show only <paramref name="count"/> rows from the row <paramref name="first"/> on.</summary>
    public double Apply(WidgetDto widget, double y, double width, int first = 0, int count = int.MaxValue)
    {
        var list = widget.IconsClickable; // a pressable list: one wide row per icon with its name and on/off
        var size = widget.IconSize > 0 ? widget.IconSize : list ? ListIconSize : DefaultSize;
        _placed = new List<IconDto>();
        double used;
        if (Grouped(widget))
        {
            var columns = Columns(widget);
            var rowCount = columns.Max(column => column.Count);
            var head = HeaderHeightOf(widget);
            var gap = GapOf(widget);
            var firstRow = Math.Clamp(first, 0, rowCount);
            var shownRows = Math.Min(count, rowCount - firstRow);
            var columnWidth = (width - Gap * (columns.Count - 1)) / columns.Count;
            for (var g = 0; g < columns.Count; g++)
            {
                while (_headers.Count <= g)
                {
                    var header = UiKit.Text("Group", _parent, 14, Color.white, TextAlignmentOptions.MidlineLeft);
                    _headers.Add(header);
                }

                _headers[g].fontSize = HeaderSizeOf(widget);
                UiKit.Place(_headers[g].gameObject, g * (columnWidth + Gap) + 4, y, columnWidth - 4, head);
                UiKit.SetText(_headers[g], columns[g][0].Group);
                _headers[g].gameObject.SetActive(true);
                for (var r = 0; r < shownRows && firstRow + r < columns[g].Count; r++)
                {
                    var cell = CellAt(_placed.Count);
                    cell.Place(g * (columnWidth + Gap), y + head + r * (size + gap), size, columnWidth, false);
                    cell.Bind(columns[g][firstRow + r], true, false, true);
                    _placed.Add(columns[g][firstRow + r]);
                }
            }

            for (var g = columns.Count; g < _headers.Count; g++) _headers[g].gameObject.SetActive(false);
            ApplyBadge(widget, y, width, head);
            used = head + shownRows * (size + gap);
        }
        else
        {
            foreach (var header in _headers) header.gameObject.SetActive(false);
            if (_badge != null) _badge.gameObject.SetActive(false);
            var icons = widget.Icons;
            var firstIcon = Math.Clamp(first, 0, icons.Count);
            var shown = Math.Min(count, icons.Count - firstIcon);
            var perRow = list || widget.IconsVertical ? 1 : Math.Max(1, (int)((width + Gap) / (size + Gap)));
            for (var i = 0; i < shown; i++)
            {
                var cell = CellAt(i);
                var x = i % perRow * (size + Gap);
                var top = y + i / perRow * (size + Gap);
                cell.Place(x, top, size, list ? width : 0, list && widget.IconsRemovable);
                cell.Bind(icons[firstIcon + i], list, list && widget.IconsRemovable);
                _placed.Add(icons[firstIcon + i]);
            }

            used = (shown == 0 ? 0 : (shown - 1) / perRow + 1) * (size + Gap);
        }

        for (var i = _placed.Count; i < _cells.Count; i++) _cells[i].Hide();
        Tick();
        return used;
    }

    /// <summary>The short text at the right end of the title row (for the list of presets: whether the character is in combat).</summary>
    void ApplyBadge(WidgetDto widget, double y, double width, double head)
    {
        if (widget.Badge.Length == 0)
        {
            if (_badge != null) _badge.gameObject.SetActive(false);
            return;
        }

        if (_badge == null)
        {
            _badge = UiKit.Text("Badge", _parent, 14, Color.white, TextAlignmentOptions.MidlineRight);
        }

        const double badgeWidth = 90;
        _badge.fontSize = HeaderSizeOf(widget);
        UiKit.Place(_badge.gameObject, width - badgeWidth - 4, y, badgeWidth, head);
        UiKit.SetText(_badge, widget.Badge);
        _badge.gameObject.SetActive(true);
    }

    Cell CellAt(int index)
    {
        while (_cells.Count <= index) _cells.Add(new Cell(_parent, _press, _remove, _ui));
        return _cells[index];
    }

    public void Hide()
    {
        foreach (var cell in _cells) cell.Hide();
        foreach (var header in _headers) header.gameObject.SetActive(false);
        if (_badge != null) _badge.gameObject.SetActive(false);
        _placed = new List<IconDto>();
    }

    /// <summary>Every frame: the gauges and the time texts (a text is written only when its second changes).</summary>
    public void Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < _placed.Count && i < _cells.Count; i++)
        {
            _cells[i].Tick(_placed[i], now);
        }
    }

    internal static Sprite Disc()
    {
        return _disc ??= Make(0f);
    }

    static Sprite Ring()
    {
        return _ring ??= Make(0.84f);
    }

    // two rings: each is 12% of the radius thick (the single ring is 16%), the outer one from 0.88 to 1.0, the inner one from 0.76 to 0.88
    const float DualRing = 0.12f;

    static Sprite OuterRing()
    {
        return _outerRing ??= Make(1f - DualRing);
    }

    static Sprite InnerRing()
    {
        return _innerRing ??= Make((1f - 2 * DualRing) / (1f - DualRing)); // lies in a box with the radius 0.88, see LayoutRings
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
        readonly Image _accent;     // the bar at the left of the row that is the current one (a list in columns)
        double _end, _rowWidth;
        readonly Image _hitBack;
        readonly TextMeshProUGUI _name;
        readonly TextMeshProUGUI _state;
        readonly GameObject _removeButton;
        string _key = "";
        readonly CanvasGroup _group;
        readonly CanvasGroup _art;
        readonly Image _track;
        readonly Image _gauge;
        readonly Image _outerTrack;
        readonly Image _outerGauge;
        bool _dual; // the icon has two rings: the existing ring then takes the inner half of its width
        readonly Image _face;
        readonly Image _picture;
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
            _outerTrack = UiKit.Box("OuterTrack", art.transform, new Color(1f, 1f, 1f, 0.22f));
            _outerTrack.sprite = OuterRing();
            UiKit.Stretch(_outerTrack.gameObject);
            _outerGauge = UiKit.Box("OuterGauge", art.transform, Color.white);
            _outerGauge.sprite = OuterRing();
            _outerGauge.type = Image.Type.Filled;
            _outerGauge.fillMethod = Image.FillMethod.Radial360;
            _outerGauge.fillOrigin = 2;
            _outerGauge.fillClockwise = false;
            UiKit.Stretch(_outerGauge.gameObject);
            _outerTrack.gameObject.SetActive(false);
            _outerGauge.gameObject.SetActive(false);
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
            _center = UiKit.Text("Center", _box.transform, 16, Color.white, TextAlignmentOptions.Center);
            _corner = UiKit.Text("Corner", _box.transform, 12, Color.white, TextAlignmentOptions.BottomRight);
            var removeLabel = UiKit.Button("Remove", _root.transform, "✕", new Color(0.55f, 0.2f, 0.22f, 0.7f), () =>
            {
                if (_key.Length > 0) remove?.Invoke(_key); // deleted at once, no question asked
            });
            _removeButton = removeLabel.transform.parent.gameObject;
            removeLabel.fontSize = 14;
            if (ui != null) UiKit.Hover(_removeButton, () => ui.ShowTip(_removeButton, "목록에서 삭제"), () => ui.HideTip(_removeButton));
            _accent = UiKit.Box("Accent", _root.transform, new Color(0.48f, 0.89f, 0.6f, 1f));
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
                (_end, _rowWidth) = (end, width);
                PlaceName(true);
                UiKit.Place(_state.gameObject, end - StateWidth, 0, StateWidth, size);
                UiKit.Place(_accent.gameObject, 0, 3, 3, Math.Max(4, size - 6));
                UiKit.Place(_removeButton, width - 6 - RemoveSize, (size - RemoveSize) / 2, RemoveSize, RemoveSize);
                _name.fontSize = (float)Math.Max(12, size * 0.42);
                _state.fontSize = (float)Math.Max(11, size * 0.32);
            }

            LayoutRings();
            UiKit.Stretch(_center.gameObject);
            UiKit.Place(_corner.gameObject, 0, 0, size - 2, size - 1);
            _center.fontSize = (float)(size * 0.36);
            _corner.fontSize = (float)(size * 0.36);
        }

        const double StateWidth = 44;

        /// <summary>The name takes the row up to the state text; a row without a state text gives the name all the room.</summary>
        void PlaceName(bool hasState) =>
            UiKit.Place(_name.gameObject, _size + 8, 0, Math.Max(20, _end - (hasState ? StateWidth + 4 : 0) - _size - 8), _size);

        /// <summary>One ring: the whole width. Two rings: the existing one on the inner half (a smaller box with the inner ring picture), the outer one on the outer half.</summary>
        void LayoutRings()
        {
            var ring = _dual ? (float)(_size * DualRing / 2) : 0f; // the box of the inner ring is smaller by the width of the outer one
            UiKit.Stretch(_track.gameObject, ring, ring, ring, ring);
            UiKit.Stretch(_gauge.gameObject, ring, ring, ring, ring);
            var face = (float)(_size * (_dual ? DualRing : 0.08)); // the picture stays inside the rings
            UiKit.Stretch(_face.gameObject, face, face, face, face);
        }

        public void Bind(IconDto icon, bool list, bool removable, bool compact = false)
        {
            _removeButton.SetActive(removable);
            _root.SetActive(true);
            _key = list ? icon.Key : "";
            _group.blocksRaycasts = list; // only a row of a pressable list takes the mouse
            _hit.SetActive(list);
            _name.gameObject.SetActive(list);
            // a list in columns shows a state text only when a row has something to say; the current row has the bar at its left instead
            var hasState = !compact || icon.State.Length > 0;
            _state.gameObject.SetActive(list && hasState);
            _accent.gameObject.SetActive(list && compact && !icon.Dim);
            if (list)
            {
                _hitBack.color = icon.Dim ? new Color(0.35f, 0.35f, 0.4f, 0.22f) : new Color(0.2f, 0.5f, 0.35f, 0.32f);
                UiKit.SetText(_name, icon.Name);
                PlaceName(hasState);
                if (hasState) UiKit.SetText(_state, icon.State.Length > 0 ? icon.State : icon.Dim ? "<color=#9ea9bb>꺼짐</color>" : "<color=#7be39a>켜짐</color>");
            }

            var sprite = UiKit.Picture(icon.Image);
            _picture.sprite = sprite;
            _picture.gameObject.SetActive(sprite != null);
            _gauge.color = UiKit.Rgb(icon.Color == 0 ? 0x4FC3F7 : icon.Color);
            var dual = icon.OuterColor != 0;
            if (dual != _dual || _track.sprite == null)
            {
                _dual = dual;
                _track.sprite = dual ? InnerRing() : Ring();
                _gauge.sprite = dual ? InnerRing() : Ring();
                LayoutRings();
            }

            _outerGauge.color = UiKit.Rgb(icon.OuterColor);
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
            _outerTrack.gameObject.SetActive(show && _dual);
            // a ring with nothing to count (its buff is not there) is only its track when the icon has two rings; with one ring it is full while the buff is there
            var inner = !(_dual && icon.EndsAtMs <= 0);
            var outer = _dual && icon.OuterEndsAtMs > 0;
            _gauge.gameObject.SetActive(show && !icon.Dim && inner);
            _outerGauge.gameObject.SetActive(show && !icon.Dim && outer);
            if (!show || icon.Dim) return;
            if (inner) Fill(_gauge, icon.EndsAtMs, icon.TotalMs, gauge, nowMs);
            if (outer) Fill(_outerGauge, icon.OuterEndsAtMs, icon.OuterTotalMs, gauge, nowMs);
        }

        static void Fill(Image ring, long endsAtMs, long totalMs, string gauge, long nowMs)
        {
            float share;
            if (endsAtMs > 0 && totalMs > 0) share = Mathf.Clamp01((float)((endsAtMs - nowMs) / (double)totalMs));
            else share = endsAtMs > 0 ? 0f : 1f; // no duration: a full ring while the buff is there
            var fill = gauge == "ring-reverse" ? 1f - share : share;
            if (Mathf.Abs(ring.fillAmount - fill) > 0.004f) ring.fillAmount = fill; // a change redraws the canvas: only when it is visible
        }

        static string Format(double seconds)
        {
            if (seconds >= 60) return $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
            return seconds >= 10 ? Math.Ceiling(seconds).ToString("0") : seconds.ToString("0.0");
        }
    }
}
