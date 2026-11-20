using System;
using System.Collections.Generic;
using System.Text;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>One overlay window drawn in the game: header (title, buttons), body (widgets), resize handle.</summary>
internal sealed class WindowView
{
    const float Header = 30f;
    const float Pad = 6f;
    const float GripRoom = 26f; // keeps the footer clock clear of the resize triangle in the corner

    static readonly Color Panel = new(0.07f, 0.08f, 0.1f, 0.88f);
    static readonly Color Bar = new(0.12f, 0.14f, 0.18f, 1f);
    static readonly Color Button = new(0.22f, 0.3f, 0.45f, 1f);
    static readonly Color Muted = new(0.62f, 0.68f, 0.76f, 1f);

    readonly UiRoot _root;
    readonly GameObject _window;
    readonly GameObject _panel;
    readonly CanvasGroup _panelGroup;
    readonly GameObject _header;
    readonly CanvasGroup _headerGroup;
    readonly TextMeshProUGUI _title;
    readonly GameObject _body;
    readonly GameObject _handle;
    readonly List<GameObject> _buttons = new();
    readonly List<RowView> _rows = new();
    readonly List<TextMeshProUGUI> _labels = new();
    ChatPanel _chatPanel;
    IconStrip _strip;
    TextMeshProUGUI _footPlace;
    TextMeshProUGUI _footTime;
    TextMeshProUGUI _empty;
    WindowDto _dto;
    LayoutDto _live; // the layout while the user drags (not yet confirmed by the service)
    string _buttonSignature = "";
    int _scroll;
    int _maxScroll;
    double _rowStep = 24; // the height of one meter row (the drag distance that counts as one row)
    public int Failures;
    public bool Dead;

    public string Id { get; }

    public WindowView(UiRoot root, string id)
    {
        _root = root;
        Id = id;
        _window = UiKit.Node("Window_" + id, root.Canvas.transform);
        _panel = UiKit.Node("Panel", _window.transform);
        UiKit.Stretch(_panel);
        UiKit.Box("Background", _panel.transform, Panel, raycast: true); // catches the mouse wheel over empty areas
        UiKit.Stretch(_panel.transform.Find("Background").gameObject);
        UiKit.Scroller(_panel, () => _rowStep, Scrolled); // wheel and drag scroll the meter rows
        _panelGroup = _panel.AddComponent<CanvasGroup>();
        _body = UiKit.Node("Body", _panel.transform);
        _body.AddComponent<RectMask2D>();
        _header = UiKit.Node("Header", _window.transform);
        UiKit.Box("Bar", _header.transform, Bar, raycast: true);
        UiKit.Stretch(_header.transform.Find("Bar").gameObject);
        _headerGroup = _header.AddComponent<CanvasGroup>();
        _title = UiKit.Text("Title", _header.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft);
        _handle = UiKit.Node("Resize", _window.transform);
        var grip = UiKit.Box("Grip", _handle.transform, new Color(0.62f, 0.62f, 0.66f, 0.9f), raycast: true);
        grip.sprite = UiKit.Icon("grip"); // a grey right triangle in the corner
        UiKit.Stretch(grip.gameObject);

        // dragging the header moves the window, dragging the corner resizes it (both only while not pinned)
        UiKit.Drag(_header.transform.Find("Bar").gameObject, delta =>
        {
            if (_dto == null || _dto.Layout.Pinned) return;
            _live = LayoutMath.Moved(_live ?? _dto.Layout, delta.x, delta.y);
            ApplyLayout(_live);
        }, () =>
        {
            if (_live == null) return;
            _root.Send(new CommandDto { Window = Id, Action = CommandAction.Move, X = _live.X, Y = _live.Y });
            _live = null;
        });
        UiKit.Drag(grip.gameObject, delta =>
        {
            if (_dto == null || _dto.Layout.Pinned) return;
            _live = LayoutMath.Resized(_live ?? _dto.Layout, delta.x, delta.y);
            ApplyLayout(_live);
        }, () =>
        {
            if (_live == null) return;
            _root.Send(new CommandDto { Window = Id, Action = CommandAction.Resize, W = _live.W, H = _live.H });
            _live = null;
        });
    }

    /// <summary>The wheel moved the meter rows (also called from the row icons: an object with its own event handler would otherwise swallow the wheel).</summary>
    void Scrolled(int steps)
    {
        if (_dto == null || _dto.Layout.Collapsed) return;
        var next = Math.Clamp(_scroll + steps, 0, _maxScroll);
        if (next == _scroll) return;
        _scroll = next;
        FillBody(_dto, _live ?? _dto.Layout);
    }

    public void Tick()
    {
        _chatPanel?.Tick();
        if (_dto != null && (_dto.Bare || !_dto.Layout.Collapsed)) _strip?.Tick();
    }

    // ---- bare window: only the icons and one small corner handle

    const float BareGrip = 18f;   // width of the move handle
    const float GripHeight = 24f; // its height (the drag icon: 2 x 3 dots)
    GameObject _bareGrip;
    float _bareDragged;

    /// <summary>The window size of a bare window: the handle column plus the icons in one row (or one column).</summary>
    static (double W, double H) BareSize(WindowDto dto)
    {
        var widget = dto.Widgets.Find(w => w.Kind == WidgetKind.Icons);
        var count = widget?.Icons.Count ?? 0;
        var size = widget != null && widget.IconSize > 0 ? widget.IconSize : 48;
        const double gap = 6;
        if (count == 0) return (BareGrip + 2, GripHeight + 2);
        return widget.IconsVertical
            ? (BareGrip + 2 + size, Math.Max(GripHeight + 2, count * (size + gap) - gap))
            : (BareGrip + 2 + count * (size + gap) - gap, Math.Max(GripHeight + 2, size));
    }

    void EnsureBareGrip()
    {
        if (_bareGrip != null) return;
        var label = UiKit.Button("BareGrip", _window.transform, "", new Color(0.05f, 0.06f, 0.09f, 0.55f), () =>
        {
            if (_bareDragged < 4f) _root.OpenSettings(Id); // a click (not a drag) opens the settings window
            _bareDragged = 0f;
        });
        _bareGrip = label.transform.parent.gameObject;
        // the handle shows the drag icon: two columns of three round dots
        var dots = UiKit.Box("Dots", _bareGrip.transform, new Color(0.86f, 0.9f, 0.96f, 0.95f));
        dots.sprite = UiKit.Icon("drag");
        dots.preserveAspect = true;
        UiKit.Stretch(dots.gameObject, 3, 3, 3, 3);

        UiKit.Drag(_bareGrip, delta =>
        {
            if (_dto == null) return;
            _bareDragged += Math.Abs(delta.x) + Math.Abs(delta.y);
            _live = LayoutMath.Moved(_live ?? _dto.Layout, delta.x, delta.y);
            ApplyLayout(_live);
        }, () =>
        {
            if (_live == null) return;
            _root.Send(new CommandDto { Window = Id, Action = CommandAction.Move, X = _live.X, Y = _live.Y });
            _live = null;
        });
    }

    void ApplyBare(WindowDto dto, LayoutDto layout)
    {
        EnsureBareGrip();
        _bareGrip.SetActive(true);
        _header.SetActive(false);
        _handle.SetActive(false);
        _panel.transform.Find("Background").GetComponent<Image>().enabled = false;
        _panelGroup.blocksRaycasts = false;
        _panelGroup.alpha = 1f;
        _body.SetActive(true);
        ApplyLayout(layout);
        FillBody(dto, layout);
    }

    public void Destroy()
    {
        Dead = true;
        if (_window != null) UnityEngine.Object.Destroy(_window);
    }

    public void Apply(WindowDto dto)
    {
        _dto = dto;
        var layout = _live ?? dto.Layout;
        if (dto.Bare)
        {
            ApplyBare(dto, layout);
            return;
        }

        if (_bareGrip != null) // it was bare before (the plugin changed): back to a normal window
        {
            _bareGrip.SetActive(false);
            _header.SetActive(true);
            _panel.transform.Find("Background").GetComponent<Image>().enabled = true;
        }

        ApplyLayout(layout);
        UiKit.SetText(_title, dto.Title);
        BuildButtons(dto);
        _body.SetActive(!layout.Collapsed);
        _handle.SetActive(!layout.Collapsed && !layout.Pinned);
        // with the header hidden the window is still moved by a small corner handle (the resize triangle stays); the rest lets clicks through
        var hidden = layout.HeaderHidden && !layout.Collapsed;
        if (hidden)
        {
            EnsureBareGrip();
            _bareGrip.SetActive(!layout.Pinned);
        }
        else if (_bareGrip != null) _bareGrip.SetActive(false);
        if (!layout.Collapsed) FillBody(dto, layout);
    }

    void ApplyLayout(LayoutDto layout)
    {
        if (_dto != null && _dto.Bare)
        {
            var (bx, by, _, _) = LayoutMath.ToReferencePixels(layout);
            var (bw, bh) = BareSize(_dto);
            UiKit.Place(_window, bx, by, bw, bh);
            UiKit.Place(_body, BareGrip + 2, 0, Math.Max(1, bw - BareGrip - 2), bh);
            UiKit.Place(_bareGrip, 0, 0, BareGrip, GripHeight);
            return;
        }

        var (x, y, w, h) = LayoutMath.ToReferencePixels(layout);
        var height = layout.Collapsed ? Header : h;
        if (layout.Collapsed && _dto != null) w = CollapsedWidth(_dto);
        UiKit.Place(_window, x, y, w, height);
        _panelGroup.alpha = layout.Opacity / 100f;
        var hidden = layout.HeaderHidden && !layout.Collapsed; // title and buttons hidden: only the content is drawn and the game gets the clicks
        _panelGroup.blocksRaycasts = !layout.Pinned; // pinned: clicks go through to the game (also with the header hidden); not pinned: the window can be clicked
        _header.SetActive(!hidden);
        if (hidden && _bareGrip != null) UiKit.Place(_bareGrip, 0, 0, BareGrip, GripHeight);
        _headerGroup.alpha = Math.Max(0.5f, layout.Opacity / 100f);
        UiKit.Place(_header, 0, 0, w, Header);
        UiKit.Place(_title.gameObject, 8, 0, Math.Max(10, w - 16), Header);
        var top = hidden ? 4 : Header + 2;
        UiKit.Place(_body, Pad, top, Math.Max(10, w - Pad * 2), Math.Max(10, height - top - 4));
        UiKit.Place(_handle, w - 16, height - 16, 16, 16);
        if (_buttons.Count > 0) PlaceButtons(w);
    }

    // ---- header buttons

    void BuildButtons(WindowDto dto)
    {
        var signature = string.Join("|", dto.FrameButtons.ConvertAll(b => b.Slot + b.Glyph + b.OpensSettings + b.OpensPrompt)) + dto.Layout.Pinned + dto.Layout.Collapsed + dto.Title;
        if (signature == _buttonSignature) return;
        _buttonSignature = signature;
        foreach (var button in _buttons) UnityEngine.Object.Destroy(button);
        _buttons.Clear();

        var layout = dto.Layout;
        if (layout.Collapsed)
        {
            // folded: only the name and a tiny "펼침"
            Add("icon:expand", () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Collapse, Value = "false" }));
            PlaceButtons(CollapsedWidth(dto));
            return;
        }

        Add("icon:dim", () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Opacity, Value = Math.Max(20, (_dto?.Layout.Opacity ?? 90) - 10).ToString() }));
        Add("icon:bright", () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Opacity, Value = Math.Min(100, (_dto?.Layout.Opacity ?? 90) + 10).ToString() }));
        var settingsShown = false;
        string settingsCaption = null;
        foreach (var frame in dto.FrameButtons)
        {
            var slot = frame.Slot;
            var caption = Caption(frame);
            if (caption.Length == 0) continue; // no caption: the button is not shown
            if (frame.OpensSettings || frame.OpensPrompt)
            {
                // settings and the translation prompt are one "설정" window drawn in the game
                if (settingsShown) continue;
                settingsShown = true;
                settingsCaption = caption; // placed right before the pin, below
            }
            else
            {
                Add(caption, () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Press, Key = "frame:" + slot }));
            }
        }

        // opacity, then the plugin's own buttons (reset, clear), settings, the pin, and the fold button at the far right
        if (settingsCaption != null) Add(settingsCaption, () => _root.OpenSettings(Id));
        Add(Pin, () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Pin, Value = layout.Pinned ? "false" : "true" }), layout.Pinned ? PinOn : Button);
        Add("icon:collapse", () => _root.Send(new CommandDto { Window = Id, Action = CommandAction.Collapse, Value = "true" }));

        PlaceButtons(LayoutMath.ToReferencePixels(layout).W);
    }

    const string Pin = "icon:pin"; // drawn as an image: the game font has no emoji

    static readonly Color PinOn = new(0.2f, 0.5f, 0.38f, 1f);

    void Add(string caption, Action click, Color? color = null)
    {
        var icon = caption.StartsWith("icon:", StringComparison.Ordinal) ? caption.Substring(5) : null;
        var sprite = icon != null ? UiKit.Icon(icon) : null;
        if (icon != null && sprite == null) caption = IconWords(icon); // no image: fall back to words
        var label = UiKit.Button("Btn_" + caption, _header.transform, sprite != null ? "" : caption, color ?? Button, click);
        if (sprite != null) UiKit.SetIcon(label, sprite);
        if (_dto != null && _dto.Layout.Collapsed) label.fontSize = 11;
        _buttons.Add(label.transform.parent.gameObject);
    }

    static string IconWords(string icon) => icon switch
    {
        "pin" => "고정",
        "reset" => "초기화",
        "settings" => "설정",
        "trash" => "비움",
        "dim" => "투명-",
        "bright" => "투명+",
        "collapse" => "접기",
        "expand" => "펼침",
        _ => icon,
    };

    /// <summary>Width of a folded window: just the name and the small unfold button.</summary>
    static double CollapsedWidth(WindowDto dto) => Math.Min(LayoutMath.ToReferencePixels(dto.Layout).W, dto.Title.Length * 14 + 70);

    static string Caption(FrameButtonDto frame)
    {
        if (frame.OpensSettings || frame.OpensPrompt) return "icon:settings";
        return frame.Glyph switch
        {
            "\U0001F504" => "icon:reset", // the meter's reset button (its emoji is not in the font)
            "↻" => "icon:reset",
            "🗑️" or "🗑" => "icon:trash",
            _ => frame.Glyph,
        };
    }

    void PlaceButtons(double width)
    {
        // right to left; each button is as wide as its caption needs
        var x = width - 4;
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var label = _buttons[i].GetComponentInChildren<TextMeshProUGUI>();
            var w = Math.Max(34, label.text.Length * 13 + 14);
            x -= w;
            UiKit.Place(_buttons[i], x, 3, w, Header - 6);
            x -= 3;
        }

        // the title stops where the buttons start
        UiKit.Place(_title.gameObject, 8, 0, Math.Max(10, x - 8), Header);
    }

    // ---- body

    void FillBody(WindowDto dto, LayoutDto layout)
    {
        var (_, _, width, height) = LayoutMath.ToReferencePixels(layout);
        var bodyWidth = Math.Max(10, width - Pad * 2);
        var bodyHeight = Math.Max(10, height - (layout.HeaderHidden && !layout.Collapsed ? 8 : Header + 6));
        double y = 0;
        var rowIndex = 0;
        var labelIndex = 0;
        var chatShown = false;
        var iconsShown = false;
        var footerShown = false;

        if (dto.Id == UiRoot.RootId)
        {
            // the system window lists the plugins as toggles
            foreach (var plugin in dto.Settings)
            {
                var row = Row(rowIndex++);
                row.Place(0, y, bodyWidth, 24);
                row.ShowChoice(plugin.Label, plugin.Value, state => _root.Send(new CommandDto { Window = UiRoot.RootId, Action = CommandAction.Plugin, Key = plugin.Key, Value = state }));
                y += 26;
            }
        }

        foreach (var widget in dto.Widgets)
        {
            switch (widget.Kind)
            {
                case WidgetKind.Label:
                {
                    var label = Label(labelIndex++);
                    var lines = widget.Text.Split('\n').Length;
                    var h = Math.Max(20, lines * 19);
                    UiKit.Place(label.gameObject, 0, y, bodyWidth, h);
                    UiKit.SetText(label, widget.Text);
                    label.gameObject.SetActive(true);
                    y += h + 2;
                    break;
                }
                case WidgetKind.Meter:
                case WidgetKind.List:
                {
                    var step = widget.RowHeight > 0 ? widget.RowHeight : 26;
                    _rowStep = step;
                    if (widget.Rows.Count == 0 && widget.Empty.Length > 0)
                    {
                        var label = Label(labelIndex++);
                        UiKit.Place(label.gameObject, 0, y, bodyWidth, 22);
                        UiKit.SetText(label, "<color=#9ea9bb>" + widget.Empty + "</color>");
                        label.gameObject.SetActive(true);
                        y += 24;
                    }

                    // the mouse wheel scrolls the rows when there are more than fit
                    var footerHeight = widget.Place.Length > 0 ? 22 : 0; // place, channel, coordinates and the fight clock
                    var listHeight = bodyHeight - footerHeight;
                    var size = widget.TextSize > 0 ? widget.TextSize : 15;
                    var visible = Math.Max(1, (int)((listHeight - y) / step));
                    _maxScroll = Math.Max(0, widget.Rows.Count - visible);
                    _scroll = Math.Clamp(_scroll, 0, _maxScroll);
                    for (var i = _scroll; i < widget.Rows.Count; i++)
                    {
                        var data = widget.Rows[i];
                        if (y + step > listHeight) break;
                        var row = Row(rowIndex++);
                        row.Place(0, y, bodyWidth, step - 2);
                        row.ShowMeter(data, size, System.IO.Path.Combine(_root.AssetsRoot, Id, "assets"));
                        y += step;
                    }

                    if (footerHeight > 0)
                    {
                        footerShown = true;
                        _footPlace ??= UiKit.Text("FootPlace", _body.transform, 13, Muted, TextAlignmentOptions.MidlineLeft);
                        _footTime ??= UiKit.Text("FootTime", _body.transform, 13, Color.white, TextAlignmentOptions.MidlineRight);
                        _footPlace.fontSize = _footTime.fontSize = Math.Max(10, size - 2);
                        var timeWidth = Math.Max(60, widget.Elapsed.Length * size * 0.7);
                        UiKit.Place(_footPlace.gameObject, 4, bodyHeight - footerHeight, Math.Max(10, bodyWidth - timeWidth - GripRoom - 8), footerHeight);
                        UiKit.Place(_footTime.gameObject, bodyWidth - timeWidth - GripRoom, bodyHeight - footerHeight, timeWidth, footerHeight); // left of the resize triangle
                        UiKit.SetText(_footPlace, Escape(widget.Place));
                        UiKit.SetText(_footTime, widget.Elapsed);
                        _footPlace.gameObject.SetActive(true);
                        _footTime.gameObject.SetActive(true);
                    }

                    break;
                }
                case WidgetKind.Icons when !iconsShown:
                {
                    iconsShown = true;
                    _strip ??= new IconStrip(_body.transform);
                    y += _strip.Apply(widget, y, dto.Bare ? 100000 : bodyWidth);
                    break;
                }
                case WidgetKind.Chat when !chatShown:
                {
                    chatShown = true;
                    _chatPanel ??= new ChatPanel(_root, Id, _body.transform);
                    _chatPanel.Apply(widget, 0, y, bodyWidth, Math.Max(40, bodyHeight - y), layout.Channels, widget.LineGap);
                    break;
                }
            }
        }

        // hide what this snapshot does not use
        for (var i = rowIndex; i < _rows.Count; i++) _rows[i].Hide();
        for (var i = labelIndex; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false);
        if (!chatShown && _chatPanel != null) _chatPanel.Hide();
        if (!iconsShown && _strip != null) _strip.Hide();
        if (!footerShown && _footPlace != null)
        {
            _footPlace.gameObject.SetActive(false);
            _footTime.gameObject.SetActive(false);
        }

        if (_empty != null) _empty.gameObject.SetActive(false);
    }

    RowView Row(int index)
    {
        while (_rows.Count <= index) _rows.Add(new RowView(_root, _body.transform, Scrolled));
        return _rows[index];
    }

    TextMeshProUGUI Label(int index)
    {
        while (_labels.Count <= index)
            _labels.Add(UiKit.Text("Label", _body.transform, 15, Color.white, TextAlignmentOptions.TopLeft));
        var text = _labels[index];
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    // user text must not open TMP tags
    static string Escape(string text) => text.Replace("<", "<​");

    /// <summary>One reusable row: meter bar with rank, job icon, imagine icons, name and value (or a toggle line in the system window).</summary>
    sealed class RowView
    {
        readonly GameObject _root;
        readonly Image _fill;
        readonly TextMeshProUGUI _rank;
        readonly TextMeshProUGUI _left;
        readonly TextMeshProUGUI _right;
        readonly Image[] _icons = new Image[5]; // job, imagine 1, imagine 2, cuisine, syrup
        readonly string[] _tips = new string[5];
        readonly UiRoot _ui;
        UnityEngine.Events.UnityAction _action;
        readonly UnityEngine.UI.Button _button;
        double _w;
        double _h;

        public RowView(UiRoot ui, Transform parent, Action<int> scrolled)
        {
            _ui = ui;
            _root = UiKit.Node("Row", parent);
            UiKit.Box("Back", _root.transform, new Color(1f, 1f, 1f, 0.06f));
            UiKit.Stretch(_root.transform.Find("Back").gameObject);
            _fill = UiKit.Box("Fill", _root.transform, Color.gray);
            _rank = UiKit.Text("Rank", _root.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft);
            for (var i = 0; i < _icons.Length; i++)
            {
                var image = _icons[i] = UiKit.Box("Icon" + i, _root.transform, Color.white, raycast: true);
                var slot = i;
                var target = image.gameObject;
                UiKit.Hover(target, () => _ui.ShowTip(target, _tips[slot]), () => _ui.HideTip(target)); // the note appears while the cursor rests on the icon
                UiKit.Scroller(target, 24, scrolled); // a hover handler on the icon would swallow the wheel and the drag, so pass them on
            }
            _left = UiKit.Text("Left", _root.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft);
            _right = UiKit.Text("Right", _root.transform, 14, Color.white, TextAlignmentOptions.MidlineRight);
            _button = _root.AddComponent<UnityEngine.UI.Button>();
            _root.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            _button.interactable = false;
        }

        public void Place(double x, double y, double w, double h)
        {
            _w = w;
            _h = h;
            UiKit.Place(_root, x, y, w, h);
            UiKit.Stretch(_root.transform.Find("Back").gameObject);
            _root.SetActive(true);
        }

        public void ShowMeter(RowDto row, int size, string assets)
        {
            HideChoices();
            var bar = UiKit.Rt(_fill.gameObject);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2((float)Math.Clamp(row.Bar, 0, 1), 1f); // exactly the ratio: 0% damage is an empty bar
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            _fill.color = UiKit.Rgb(row.BarColor == 0 ? 0x4A90D9 : row.BarColor, row.Dead ? 0.25f : 0.55f);

            _rank.gameObject.SetActive(true);
            _left.gameObject.SetActive(true);
            var ink = row.TextColor != 0 ? UiKit.Rgb(row.TextColor) : row.Dead ? new Color(1f, 0.6f, 0.6f, 1f) : Color.white;
            var icon = Math.Max(14, _h - 8);
            var x = 4.0;
            _rank.fontSize = size;
            _rank.color = ink;
            UiKit.SetText(_rank, row.Rank);
            var rankWidth = size * 0.62 * Math.Max(3, row.Rank.Length);
            UiKit.Place(_rank.gameObject, x, 0, rankWidth, _h);
            x += rankWidth + 4;

            if (row.ProfessionId >= 0)
            {
                var sprite = row.ProfessionId == 0 ? null : UiKit.LoadSprite(System.IO.Path.Combine(assets, "professions", $"Profession_{row.ProfessionId}.png"));
                _tips[0] = null;
                x = Icon(0, x, icon, sprite, row.IconColor != 0 ? UiKit.Rgb(row.IconColor) : Color.white, row.ProfessionId == 0);
            }
            else
            {
                _icons[0].gameObject.SetActive(false);
            }

            for (var i = 0; i < 2; i++)
            {
                if (i < row.Imagines.Count)
                {
                    var file = System.IO.Path.GetFileName(row.Imagines[i].Icon.Replace('\\', '/'));
                    if (file.Length > 0 && !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) file += ".png";
                    var sprite = file.Length == 0 ? null : UiKit.LoadSprite(System.IO.Path.Combine(assets, "imagines", file));
                    var imagine = row.Imagines[i];
                    _tips[1 + i] = imagine.Name.Length == 0 ? null : imagine.Tier > 0 ? $"{imagine.Name} ({imagine.Tier})" : imagine.Name;
                    x = Icon(1 + i, x, icon, sprite, Color.white, file.Length == 0); // not equipped: an invisible placeholder keeps the columns aligned
                }
                else
                {
                    _icons[1 + i].gameObject.SetActive(false);
                }
            }

            _right.fontSize = Math.Max(8, size - 1);
            _right.color = ink;
            UiKit.SetText(_right, row.Value);
            var valueWidth = Math.Min(_w * 0.62, Math.Max(40, row.Value.Length * (size - 1) * 0.56 + 8));
            UiKit.Place(_right.gameObject, _w - valueWidth - 6, 0, valueWidth, _h);

            // cuisine and syrup icons sit left of the value
            var mealX = _w - valueWidth - 10;
            var meals = 0;
            if (row.Syrup)
            {
                mealX -= icon + 2;
                _tips[4] = row.SyrupEffect.Length > 0 ? row.SyrupEffect : "시럽";
                Icon(4, mealX, icon, UiKit.Icon("syrup"), Color.white, false);
                meals++;
            }
            else
            {
                _icons[4].gameObject.SetActive(false);
            }

            if (row.Cuisine)
            {
                mealX -= icon + 2;
                _tips[3] = row.CuisineEffect.Length > 0 ? row.CuisineEffect : "요리";
                Icon(3, mealX, icon, UiKit.Icon("food"), Color.white, false);
                meals++;
            }
            else
            {
                _icons[3].gameObject.SetActive(false);
            }
            _left.fontSize = size;
            _left.color = ink;
            UiKit.SetText(_left, Escape(row.Name));
            UiKit.Place(_left.gameObject, x, 0, Math.Max(8, _w - valueWidth - 10 - meals * (icon + 2) - x), _h);
            _left.alpha = row.Dead ? 0.55f : 1f;
            _button.interactable = false;
        }

        double Icon(int slot, double x, double size, Sprite sprite, Color tint, bool blank)
        {
            var image = _icons[slot];
            image.gameObject.SetActive(true);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite != null ? tint : blank ? new Color(1f, 1f, 1f, 0f) : new Color(tint.r, tint.g, tint.b, 0.35f); // blank = nothing to show: fully transparent
            UiKit.Place(image.gameObject, x, (_h - size) / 2, size, size);
            return x + size + 3;
        }

        public void ShowToggle(string label, bool on, Action click)
        {
            var bar = UiKit.Rt(_fill.gameObject);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(1f, 1f);
            bar.offsetMin = Vector2.zero;
            bar.offsetMax = Vector2.zero;
            _fill.color = on ? new Color(0.2f, 0.5f, 0.35f, 0.55f) : new Color(0.35f, 0.35f, 0.4f, 0.35f);
            _rank.gameObject.SetActive(false);
            foreach (var icon in _icons) icon.gameObject.SetActive(false);
            _left.fontSize = 15;
            _left.color = Color.white;
            _left.gameObject.SetActive(true);
            UiKit.Place(_left.gameObject, 6, 0, _w * 0.58, _h);
            UiKit.SetText(_left, (on ? "켜짐  " : "꺼짐  ") + Escape(label));
            UiKit.SetText(_right, "");
            _button.onClick.RemoveAllListeners();
            _action = (UnityEngine.Events.UnityAction)(() =>
            {
                try
                {
                    click();
                }
                catch (Exception e)
                {
                    Plugin.L.LogWarning("toggle: " + e.Message);
                }
            });
            _button.onClick.AddListener(_action);
            _button.interactable = true;
        }

        public void Hide() => _root.SetActive(false);

        static readonly string[] States = { "on", "hidden", "off" };
        static readonly string[] StateCaptions = { "켜기", "상단탭 숨김", "끄기" };
        readonly GameObject[] _stateButtons = new GameObject[3];
        readonly Image[] _stateBack = new Image[3];

        /// <summary>A plugin line of the system window: its name on the left and, on the right, three buttons: on, header hidden, off.</summary>
        public void ShowChoice(string label, string state, Action<string> pick)
        {
            ShowToggle(label, state != "off", () => { });
            UiKit.Place(_left.gameObject, 6, 0, Math.Max(40, _w - 172), _h);
            UiKit.SetText(_left, Escape(label));
            _button.interactable = false; // only the three buttons act
            var widths = new[] { 40.0, 76.0, 40.0 };
            var x = _w - 4;
            for (var i = 2; i >= 0; i--)
            {
                x -= widths[i];
                var index = i;
                if (_stateButtons[i] == null)
                {
                    var caption = UiKit.Button("State_" + States[i], _root.transform, StateCaptions[i], Color.gray, () => _statePick?.Invoke(States[index]));
                    _stateButtons[i] = caption.transform.parent.gameObject;
                    _stateBack[i] = _stateButtons[i].GetComponent<Image>();
                    caption.fontSize = 12;
                }

                _stateButtons[i].SetActive(true);
                _stateBack[i].color = States[i] == state ? (States[i] == "off" ? new Color(0.6f, 0.25f, 0.25f, 1f) : new Color(0.2f, 0.5f, 0.38f, 1f)) : new Color(0.22f, 0.3f, 0.45f, 0.75f);
                UiKit.Place(_stateButtons[i], x, 2, widths[i], Math.Max(10, _h - 4));
                x -= 3;
            }

            _statePick = pick;
        }

        Action<string> _statePick;

        void HideChoices()
        {
            foreach (var button in _stateButtons)
                if (button != null) button.SetActive(false);
        }
    }
}