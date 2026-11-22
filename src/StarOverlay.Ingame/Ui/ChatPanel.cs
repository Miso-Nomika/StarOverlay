using System;
using System.Collections.Generic;
using System.Text;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The chat window's content, drawn like the original overlay: a rail of channel buttons on the left (only the chosen
/// channels are shown) and one rounded bubble per message with the name in white and the text in the channel's colour.
/// </summary>
internal sealed class ChatPanel
{
    // the channel rail grows and shrinks with the window: seven square buttons share its height
    double RailWidth = 52;
    double ButtonSide = 44;
    double ButtonGap = 6;
    const double StickerSize = 72;
    const double Pad = 5;
    const int ChannelCount = 6;
    const double TextScale = 1.6;
    const double PlusWidth = 10; // the thin brim with the ">" / "<" right of the scroll bar: hardly any room left and right of the sign
    const double CellGap = 3;

    // the original overlay's colours
    static readonly Color Local = new(1f, 1f, 1f, 1f);
    static readonly Color Party = Rgb(147, 190, 255);
    static readonly Color Guild = Rgb(184, 222, 138);
    static readonly Color World = Rgb(210, 184, 250);
    static readonly Color Newbie = Rgb(166, 222, 255);
    static readonly Color Notice = Rgb(255, 214, 120); // loot and system notices
    static readonly Color Whisper = Rgb(255, 190, 214);
    static readonly Color Master = Rgb(78, 132, 204);
    static readonly Color BubbleFill = Rgb(52, 52, 60);
    static readonly Color BubbleEdge = Rgb(78, 78, 88);
    static readonly Color Off = Rgb(46, 46, 54);
    static readonly Color Muted = new(0.62f, 0.68f, 0.76f, 1f);

    static readonly string[] RailText = { "로컬", "월드", "길드", "귓속말", "파티\n콘텐츠", "초보자" };
    static readonly Color[] RailColor = { Local, World, Guild, Whisper, Party, Newbie };

    readonly UiRoot _ui;
    readonly string _window;
    readonly GameObject _root;
    readonly GameObject _rail;
    readonly GameObject[] _railButtons = new GameObject[ChannelCount + 1]; // [0] = all
    readonly TextMeshProUGUI[] _railLabels = new TextMeshProUGUI[ChannelCount + 1];
    readonly Image[] _railBack = new Image[ChannelCount + 1];
    readonly Image[] _railIcon = new Image[ChannelCount + 1];
    int _tabSeen;
    // rail button -> the game's tab picture (all, world, guild, party, local, newbie); whispers have none of their own
    static readonly int[] RailToTab = { 0, 4, 1, 2, -1, 3, 5 };
    float _nextLine;
    int _world; // the world chat channel number the game is on now (0 = not known yet)
    readonly GameObject _list;
    readonly GameObject _plus;
    readonly GameObject _picker;
    readonly TextMeshProUGUI _plusLabel;
    readonly RawImage[] _pickerStarArt = new RawImage[RecentStickers.Slots];
    readonly GameObject[] _pickerCells = new GameObject[RecentStickers.Slots];
    readonly RawImage[] _pickerIcons = new RawImage[RecentStickers.Slots];
    readonly GameObject[] _pickerStars = new GameObject[RecentStickers.Slots];
    bool _pickerOpen;
    int _pickerSeen = -1;
    int _stickerVersionSeen = -1;
    float _pickerNext;
    readonly List<Bubble> _bubbles = new();
    readonly TextMeshProUGUI _probe;
    readonly Dictionary<string, double> _heights = new();

    int _mask = 63;
    DateTime _clicked = DateTime.MinValue;
    double _offset;    // pixels scrolled back from the newest message (0 = the newest at the bottom)
    long _anchorId;        // the message at the lower edge of a scrolled-back view
    double _anchorBottom;  // and where its lower edge is on the screen
    bool _userScrolled;    // the last move of the view was made by the user (wheel or drag), not by new messages
    Image _barTrack;
    Image _barThumb;
    int _lastCount;
    long _lastTail;
    string _signature = "";
    WidgetDto _widget;
    double _w;
    double _h;
    int _size = 15;
    int _gap;

    public ChatPanel(UiRoot ui, string window, Transform parent)
    {
        _ui = ui;
        _window = window;
        _root = UiKit.Node("Chat", parent);
        _rail = UiKit.Node("Rail", _root.transform);
        UiKit.Box("RailBack", _rail.transform, new Color(0.07f, 0.08f, 0.1f, 0.9f));
        UiKit.Stretch(_rail.transform.Find("RailBack").gameObject);
        for (var i = 0; i <= ChannelCount; i++)
        {
            var index = i;
            var label = UiKit.Button("Ch" + i, _rail.transform, i == 0 ? "전체" : RailText[i - 1], Master, () => Toggle(index));
            _railLabels[i] = label;
            _railButtons[i] = label.transform.parent.gameObject;
            _railBack[i] = _railButtons[i].GetComponent<Image>();
            var icon = UiKit.Box("Icon", _railButtons[i].transform, Color.white);
            icon.preserveAspect = true;
            icon.gameObject.SetActive(false);
            _railIcon[i] = icon;
            label.fontSize = 12;
            label.enableWordWrapping = true;
        }

        _list = UiKit.Node("List", _root.transform);
        _list.AddComponent<RectMask2D>();
        var back = UiKit.Box("ListBack", _list.transform, new Color(0.1f, 0.11f, 0.14f, 0.9f), raycast: true);
        UiKit.Stretch(back.gameObject);
        // wheel and drag move through older messages (drag down = older)
        UiKit.Scroller(back.gameObject, RowPixels, rows => ScrollBy(-rows * RowPixels));
        _barTrack = UiKit.Box("BarTrack", _list.transform, new Color(1f, 1f, 1f, 0.06f));
        _barThumb = UiKit.Box("BarThumb", _list.transform, new Color(1f, 1f, 1f, 0.35f));
        _barTrack.gameObject.SetActive(false);
        _barThumb.gameObject.SetActive(false);
        // the ">": a thin strip at the right edge that opens the box with the stickers (the starred and the ones sent last); it is the "<" that closes the box again
        _plusLabel = UiKit.Button("Plus", _root.transform, ">", PlusIdle, TogglePicker);
        _plusLabel.fontSize = 12;
        _plus = _plusLabel.transform.parent.gameObject;
        _picker = UiKit.Node("Picker", _root.transform);
        UiKit.Box("PickerBack", _picker.transform, new Color(0.07f, 0.08f, 0.1f, 0.96f), raycast: true);
        UiKit.Stretch(_picker.transform.Find("PickerBack").gameObject);
        for (var i = 0; i < RecentStickers.Slots; i++)
        {
            var index = i;
            var cell = UiKit.Button("Sticker" + i, _picker.transform, "", FilledCell, () => SendRecent(index));
            _pickerCells[i] = cell.transform.parent.gameObject;
            var icon = new GameObject("Icon");
            icon.transform.SetParent(_pickerCells[i].transform, false);
            icon.layer = _pickerCells[i].layer;
            _pickerIcons[i] = icon.AddComponent<RawImage>();
            _pickerIcons[i].raycastTarget = false;
            UiKit.Stretch(icon);
            icon.SetActive(false);
            // the star in the corner of the place: pressing it pins the sticker (or lets it go); it does not send
            var star = UiKit.Button("Star" + i, _pickerCells[i].transform, "", new Color(0f, 0f, 0f, 0.4f), () => PressStar(index));
            _pickerStars[i] = star.transform.parent.gameObject;
            var art = new GameObject("Art");
            art.transform.SetParent(_pickerStars[i].transform, false);
            art.layer = _pickerStars[i].layer;
            _pickerStarArt[i] = art.AddComponent<RawImage>();
            _pickerStarArt[i].raycastTarget = false;
            UiKit.Stretch(art);
        }

        _picker.SetActive(false);
        _probe = UiKit.Text("Probe", _root.transform, 15, Color.white, TextAlignmentOptions.TopLeft);
        _probe.enableWordWrapping = true;
        _probe.overflowMode = TextOverflowModes.Overflow;
        _probe.color = new Color(0f, 0f, 0f, 0f); // invisible, but it has to be active for the text engine to measure with it
        UiKit.Place(_probe.gameObject, 0, 0, 10, 10);
    }

    static readonly Color FilledCell = new(0.16f, 0.17f, 0.21f, 1f);
    static readonly Color EmptyCell = new(0.11f, 0.12f, 0.15f, 1f);
    static readonly Color StarOn = new(1f, 0.82f, 0.25f, 1f);
    static readonly Color StarOff = new(0.85f, 0.88f, 0.94f, 0.9f);

    // the window cuts its body to its own rectangle (a RectMask2D); the box beside the window is outside it, so that cut is off while the box is open
    void ClipBody(bool on)
    {
        var mask = _root.GetComponentInParent<RectMask2D>();
        if (mask != null) mask.enabled = on;
    }

    void TogglePicker()
    {
        _pickerOpen = !_pickerOpen;
        ClipBody(!_pickerOpen);
        _picker.SetActive(_pickerOpen);
        _plusLabel.text = _pickerOpen ? "<" : ">"; // opening and closing stay on the one place of the brim
        _pickerSeen = -1;
        if (_pickerOpen) StickerLoader.Prepare(); // the pictures are loaded from the client when the box opens
    }

    static readonly Color PlusIdle = new(0.16f, 0.18f, 0.24f, 0.95f);

    /// <summary>The brim flashes red for a moment when a sticker could not be handed to the game (a send that worked shows nothing).</summary>
    void ShowSendResult()
    {
        var back = _plus.GetComponent<Image>();
        if (back == null) return;
        var failed = StickerSender.Result == 2 && Time.realtimeSinceStartup - StickerSender.ResultAt < 1.2f;
        back.color = failed ? new Color(0.7f, 0.2f, 0.2f, 0.95f) : PlusIdle;
    }

    void SendRecent(int index)
    {
        var items = RecentStickers.Display();
        if (index >= items.Count) return;
        StickerSender.Send(items[index].Code);
        _pickerOpen = false; // choosing one closes the box
        ClipBody(true);
        _picker.SetActive(false);
        _plusLabel.text = ">";
    }

    void PressStar(int index)
    {
        var items = RecentStickers.Display();
        if (index >= items.Count) return;
        if (RecentStickers.ToggleFavorite(items[index].Code)) return;
        StickerSender.Result = 2; // all places are starred already: the strip flashes red
        StickerSender.ResultAt = Time.realtimeSinceStartup;
        StickerSender.ResultVersion++;
    }

    /// <summary>
    /// Fills the box: two columns by five rows that share the height of the window, the starred stickers first, then the ones sent last, empty places
    /// where there are none yet (their pictures come from the cache, or are asked of the client).
    /// </summary>
    void PaintPicker()
    {
        var items = RecentStickers.Display();
        _pickerSeen = RecentStickers.Version;
        _stickerVersionSeen = StickerCache.Version;
        const int columns = 2;
        const int rows = 5;
        var side = Math.Clamp((_h - CellGap * (rows + 1)) / rows, 24, 110);
        var width = columns * side + (columns + 1) * CellGap;
        var height = rows * side + (rows + 1) * CellGap;
        // the box lies right of the brim (the ">" / "<" stays where it is, so quick clicks on it never hit a sticker), outside the window; the chat is not covered
        UiKit.Place(_picker, _w, 0, width, height);
        var starSide = Math.Clamp(side * 0.36, 14, 24);
        for (var i = 0; i < RecentStickers.Slots; i++)
        {
            var has = i < items.Count;
            UiKit.Place(_pickerCells[i], CellGap + (i % columns) * (side + CellGap), CellGap + (i / columns) * (side + CellGap), side, side);
            _pickerCells[i].GetComponent<Image>().color = has ? FilledCell : EmptyCell;
            _pickerStars[i].SetActive(has);
            if (!has)
            {
                _pickerIcons[i].gameObject.SetActive(false);
                continue;
            }

            UiKit.Place(_pickerStars[i], side - starSide - 2, 2, starSide, starSide);
            _pickerStarArt[i].texture = items[i].Favorite ? StarArt.Filled : StarArt.Outline;
            _pickerStarArt[i].color = items[i].Favorite ? StarOn : StarOff;
            var texture = StickerCache.Get(items[i].Code);
            if (texture == null) StickerLoader.Request(items[i].Code);
            _pickerIcons[i].texture = texture;
            _pickerIcons[i].gameObject.SetActive(texture != null);
        }
    }

    public void Hide()
    {
        _pickerOpen = false;
        ClipBody(true);
        _picker.SetActive(false);
        _plusLabel.text = ">";
        _root.SetActive(false);
        GameChatInput.Anchor = null;
    }

    public void Apply(WidgetDto widget, double x, double y, double w, double h, int mask, int gap)
    {
        _root.SetActive(true);
        UiKit.Place(_root, x, y, w, h);
        // the stored mask wins unless the user has just clicked (the answer from the service is a moment behind)
        if ((DateTime.UtcNow - _clicked).TotalSeconds > 1.5) _mask = mask;
        _widget = widget;
        if (widget.WorldChannel > 0 && widget.WorldChannel != _world)
        {
            _world = widget.WorldChannel; // from the game's own answer to the server
            _signature = "";
        }
        GameChatInput.WorldNumber = _world;

        GameChatUi.Lines = widget.Lines; // the sticker copier reads the same lines
        GameChatInput.Anchor = _root.GetComponent<RectTransform>(); // the game input bar trial sits under this panel
        _w = w;
        _h = h;
        var pitch = Math.Max(24, (h - 8) / (ChannelCount + 1)); // place of one button from the top of the next
        ButtonGap = Math.Max(2, pitch * 0.12);
        ButtonSide = Math.Max(18, pitch - ButtonGap);
        RailWidth = Math.Max(30, ButtonSide + 8);
        // the setting counts points like the original overlay; on the 1080p game canvas a point is about 1.5 pixels, so 9 reads as 14 px
        _size = (int)Math.Round((widget.TextSize > 0 ? widget.TextSize : 9) * TextScale);
        _gap = Math.Clamp(gap, 0, 24);
        UiKit.Place(_rail, 0, 0, RailWidth, h);
        UiKit.Place(_list, RailWidth + 4, 0, Math.Max(40, w - RailWidth - 4 - PlusWidth), h);
        UiKit.Place(_plus, w - PlusWidth, 0, PlusWidth, h);
        RecentStickers.Observe(widget.Lines);
        PaintRail();
        Redraw();
    }

    // ---- the channel rail

    void Toggle(int index)
    {
        if (index == 0) _mask = _mask == 63 ? 0 : 63;
        else _mask ^= 1 << (index - 1);
        _clicked = DateTime.UtcNow;
        _offset = 0;
        _ui.Send(new CommandDto { Window = _window, Action = CommandAction.Channels, Value = _mask.ToString() });
        PaintRail();
        Redraw();
    }

    // the buttons are stacked like the game's tab column: all, world, guild, party, whisper (ours), local, newbie
    static readonly int[] RailSlot = { 0, 5, 1, 2, 4, 3, 6 }; // rail index -> place from the top

    /// <summary>
    /// The game's own tab picture for a rail button: a white picture on a plate that has the channel's colour while the channel is on,
    /// and the plain dimmed picture while it is off. False while the pictures are not there yet.
    /// </summary>
    bool PaintTabIcon(int i, bool on, Color tint)
    {
        var tab = RailToTab[i];
        var whisper = tab < 0;
        var shape = whisper ? 0 : tab; // whispers borrow the plate of the first tab
        if (!TabIcons.Has(shape)) return false;
        var picture = whisper ? UiKit.Icon("whisper") : (on ? TabIcons.On(tab) : TabIcons.Off(tab));
        if (picture == null) return false;
        _railLabels[i].gameObject.SetActive(false);
        _railBack[i].sprite = TabIcons.Back(shape);
        _railBack[i].preserveAspect = false;
        _railBack[i].color = on ? tint : new Color(0.11f, 0.12f, 0.16f, 0.9f);
        var icon = _railIcon[i];
        icon.gameObject.SetActive(true);
        icon.sprite = picture;
        icon.color = on ? (Brightness(tint) < 0.62f ? Color.white : Rgb(24, 24, 28)) : new Color(0.7f, 0.74f, 0.82f, 0.75f);
        var inner = ButtonSide * 0.72;
        UiKit.Place(icon.gameObject, (ButtonSide - inner) / 2, (ButtonSide - inner) / 2, inner, inner);
        UiKit.Place(_railButtons[i], (RailWidth - ButtonSide) / 2, 4 + RailSlot[i] * (ButtonSide + ButtonGap), ButtonSide, ButtonSide);
        return true;
    }

    void PaintRail()
    {
        var total = ChannelCount + 1;
        var gap = 3.0;
        var each = Math.Max(20, (_h - gap * (total + 1)) / total);
        for (var i = 0; i < total; i++)
        {
            var on = i == 0 ? _mask == 63 : (_mask & (1 << (i - 1))) != 0;
            var tint = i == 0 ? Master : RailColor[i - 1];
            if (PaintTabIcon(i, on, tint)) continue;
            _railIcon[i].gameObject.SetActive(false);
            _railBack[i].sprite = null;
            _railLabels[i].gameObject.SetActive(true);
            _railBack[i].color = on ? tint : Off;
            _railLabels[i].color = !on ? Muted : Brightness(tint) < 0.62f ? Color.white : Rgb(24, 24, 28);
            UiKit.Place(_railButtons[i], 3, gap + RailSlot[i] * (each + gap), RailWidth - 6, each);
        }
    }

    // ---- the messages

    static int ChannelBit(string channel) => channel switch
    {
        "필드" or "로컬" => 0,
        "월드" => 1,
        "길드" => 2,
        "귓속말" => 3,
        "파티" or "그룹" or "콘텐츠" => 4,
        "초보자" => 5,
        _ => -1, // unknown channels are always shown
    };

    static Color ChannelColor(string channel) => channel switch
    {
        "필드" or "로컬" => Local,
        "파티" or "그룹" or "콘텐츠" => Party,
        "길드" => Guild,
        "월드" => World,
        "귓속말" => Whisper,
        "초보자" => Newbie,
        "시스템" or "공지" => Notice,
        _ => Local,
    };

    const double RowPixels = 18; // one wheel notch is three of these; a drag moves by the pixels the pointer moved

    /// <summary>Moves the view by pixels (positive = towards older messages).</summary>
    void ScrollBy(double pixels)
    {
        if (_widget == null) return;
        var next = Math.Max(0, _offset + pixels);
        if (Math.Abs(next - _offset) < 0.01) return;
        _offset = next;
        _signature = "";
        _userScrolled = true;
        Redraw();
    }

    List<ChatLineDto> Shown()
    {
        var shown = new List<ChatLineDto>();
        foreach (var line in _widget.Lines)
        {
            var bit = ChannelBit(line.Channel);
            if (bit < 0 || (_mask & (1 << bit)) != 0) shown.Add(line);
        }

        return shown;
    }

    void Redraw()
    {
        if (_widget == null) return;
        var shown = Shown();
        var tail = shown.Count == 0 ? 0 : shown[^1].Id;
        var arrived = tail != _lastTail && shown.Count > _lastCount ? shown.Count - _lastCount : 0;
        _lastCount = shown.Count;
        _lastTail = tail;

        var signature = $"{StickerCache.Version}|{_world}|{_mask}|{(int)_offset}|{(int)_w}|{(int)_h}|{_size}|{_gap}|{shown.Count}|{tail}|{(shown.Count == 0 ? "" : shown[^1].Translation.Length + ":" + shown[^1].Roman.Length)}|{JoinGloss(shown)}";
        if (signature == _signature && arrived == 0) return;
        _signature = signature;

        var listWidth = Math.Max(40, _w - RailWidth - 4 - PlusWidth);
        // exactly the width the body text is laid in (Lay: listWidth - 12 - 16), minus one pixel: a bigger margin made a line that just fits count as wrapped, and the bubble got one empty line too many
        var textWidth = Math.Max(20, listWidth - 12 - 16 - 1);
        var used = 0;
        double content = 4;
        var bar = false;
        if (shown.Count == 0)
        {
            var empty = Take(used++);
            var height = Measure("e", _widget.Empty, textWidth, _size);
            Lay(empty, null, _widget.Empty, Local, 4, listWidth, height, "", "");
            _offset = 0;
        }
        else
        {
            // every message gets its box height; the view is a window of pixels over all of them
            var entries = new List<(ChatLineDto Line, string Body, double BodyHeight, double Box)>(shown.Count);
            for (var i = shown.Count - 1; i >= 0; i--) // newest first
            {
                var line = shown[i];
                var body = BodyText(line);
                var isSticker = StickerCache.NameOf(line.Text) != null;
                var bodyHeight = isSticker && StickerCache.Get(StickerCache.NameOf(line.Text)) != null ? StickerSize : Measure(line.Id + "|" + line.Roman.Length + "|" + line.Translation.Length, body, textWidth, _size);
                var box = Pad + (IsSystem(line) ? 0 : _size * 1.3) + bodyHeight + ItemRowHeight(line.Links) + Pad;
                entries.Add((line, body, bodyHeight, box));
                content += box + _gap + 4;
            }

            if (content <= _h)
            {
                // everything fits: the messages start at the top, oldest first, like the original overlay
                _offset = 0;
                _anchorId = 0;
                _userScrolled = false;
                var y = 4.0;
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var e = entries[i];
                    Lay(Take(used++), e.Line.Name, e.Body, Color.white, y, listWidth, e.BodyHeight, NameLine(e.Line), IsSystem(e.Line) ? "" : e.Line.Text, e.Line.Links, IsSystem(e.Line) ? Brief : Color.white, e.Line.Self, e.Line);
                    y += e.Box + _gap + 4;
                }
            }
            else
            {
                // a view scrolled back stays on the same messages while new ones arrive (and while old ones fall out of the list):
                // the message that was at the lower edge of the view keeps its place on the screen
                var movedByUser = _userScrolled;
                _userScrolled = false;
                if (!movedByUser && _anchorId != 0 && _offset > 0)
                {
                    var natural = _h - 4.0; // the lower edge of each message when nothing is scrolled
                    foreach (var e in entries)
                    {
                        if (e.Line.Id == _anchorId)
                        {
                            _offset = _anchorBottom - natural;
                            break;
                        }

                        natural -= e.Box + _gap + 4;
                    }
                }

                var maxOffset = content - _h;
                _offset = Math.Clamp(_offset, 0, maxOffset);
                _anchorId = 0;
                if (_offset > 0)
                {
                    var lower = _h - 4.0 + _offset;
                    foreach (var e in entries)
                    {
                        if (lower <= _h)
                        {
                            _anchorId = e.Line.Id;
                            _anchorBottom = lower;
                            break;
                        }

                        lower -= e.Box + _gap + 4;
                    }
                }
                bar = true;
                // the newest at the bottom (shifted up by the scroll offset), older ones above it; only those in view are drawn
                var bottom = _h - 4 + _offset;
                foreach (var e in entries)
                {
                    var top = bottom - e.Box;
                    if (top < _h && bottom > 0)
                        Lay(Take(used++), e.Line.Name, e.Body, Color.white, top, listWidth, e.BodyHeight, NameLine(e.Line), IsSystem(e.Line) ? "" : e.Line.Text, e.Line.Links, IsSystem(e.Line) ? Brief : Color.white, e.Line.Self, e.Line);
                    bottom = top - _gap - 4;
                    if (bottom < -2) break;
                }
            }
        }

        for (var i = used; i < _bubbles.Count; i++) _bubbles[i].Root.SetActive(false);
        PaintScrollBar(bar, content, listWidth);
    }

    /// <summary>A very thin scroll bar at the right edge: its length shows how much of the chat is in view, its place where.</summary>
    void PaintScrollBar(bool show, double content, double listWidth)
    {
        _barTrack.gameObject.SetActive(show);
        _barThumb.gameObject.SetActive(show);
        if (!show) return;
        const double width = 3;
        var x = listWidth - width - 2;
        var thumb = Math.Max(18, _h * _h / content);
        var maxOffset = Math.Max(1, content - _h);
        var top = (_h - thumb) * (1 - Math.Min(1, _offset / maxOffset)); // offset 0 = the newest = the thumb at the bottom
        UiKit.Place(_barTrack.gameObject, x, 2, width, _h - 4);
        UiKit.Place(_barThumb.gameObject, x, top, width, thumb);
    }

    static string JoinGloss(List<ChatLineDto> shown)
    {
        // a translation arriving later changes a line in the middle: include the visible lines' gloss lengths
        var sb = new StringBuilder();
        for (var i = Math.Max(0, shown.Count - 30); i < shown.Count; i++) sb.Append(shown[i].Translation.Length).Append(shown[i].Roman.Length).Append(',');
        return sb.ToString();
    }

    static readonly string NameColor = "FFD98A"; // the orange of the first design

    /// <summary>"[월드] 이름": the channel tag in the channel's colour, the name in orange.</summary>
    static readonly Color Brief = new(0.62f, 0.66f, 0.73f, 1f); // the grey of the IME line

    static bool IsSystem(ChatLineDto line) => line.Channel == "시스템";

    string NameLine(ChatLineDto line)
    {
        if (IsSystem(line)) return ""; // loot and notices have no name line, no time and no copy button: just small grey text
        var tag = line.Channel.Length == 0 ? "" : $"<color=#{Hex(ChannelColor(line.Channel))}>[{Escape(line.Channel)}]</color> ";
        var time = line.Time.Length == 0 ? "" : $" <size={Math.Max(8, _size - 3)}><color=#8A909C>{line.Time}</color></size>"; // small and grey, after the name
        if (line.Self) // the player's own lines sit on the right, so the header puts the time first: time, [channel], name
        {
            var own = line.Time.Length == 0 ? "" : $"<size={Math.Max(8, _size - 3)}><color=#8A909C>{line.Time}</color></size> ";
            var channel = line.Channel.Length == 0 ? "" : $"<color=#{Hex(ChannelColor(line.Channel))}>[{Escape(line.Channel)}]</color> ";
            return own + channel + $"<color=#{NameColor}>{Escape(line.Name)}</color>";
        }

        return tag + $"<color=#{NameColor}>{Escape(line.Name)}</color>" + time;
    }

    static string Hex(Color c) => $"{(int)Math.Round(c.r * 255):X2}{(int)Math.Round(c.g * 255):X2}{(int)Math.Round(c.b * 255):X2}";

    string BodyText(ChatLineDto line)
    {
        if (StickerCache.NameOf(line.Text) != null) return "[스티커]"; // shown as a picture once it is copied; never as the raw code, and never translated
        var sb = new StringBuilder();
        var system = line.Channel == "시스템"; // loot and notices are drawn small, like the IME line
        if (system) sb.Append("<size=").Append(Math.Max(8, _size - 2)).Append('>');
        // each linked item is its "[name]" inside the text, drawn in the colour of its quality and clickable (one not found in the text goes to the end)
        var at = 0;
        for (var i = 0; i < line.Links.Count; i++)
        {
            var link = line.Links[i];
            var token = link.Token.Length > 0 ? link.Token : "[" + link.Name + "]";
            var found = line.Text.IndexOf(token, at, StringComparison.Ordinal);
            if (found < 0)
            {
                sb.Append(Escape(line.Text.Substring(at))).Append(' ');
                at = line.Text.Length;
                found = -1;
            }
            else
            {
                sb.Append(Escape(line.Text.Substring(at, found - at)));
                at = found + token.Length;
            }

            sb.Append("<link=").Append(i).Append("><color=#").Append(Hex(QualityColor(link.Quality))).Append("><u>").Append(Escape(token)).Append("</u></color></link>");
        }

        sb.Append(Escape(line.Text.Substring(at)));
        if (system) sb.Append("</size>");
        // original, then the IME line in grey, then the translation
        if (line.Roman.Length > 0) sb.Append("\n<size=").Append(Math.Max(8, _size - 2)).Append("><color=#9EA9BB>").Append(Escape(line.Roman)).Append("</color></size>");
        if (line.Translation.Length > 0) sb.Append("\n<size=").Append(Math.Max(8, _size - 1)).Append("><color=#7FE0C0>").Append(Escape(line.Translation)).Append("</color></size>");
        return sb.ToString();
    }

    double Measure(string key, string text, double width, int size)
    {
        var cacheKey = key + "|" + (int)width + "|" + size;
        if (_heights.TryGetValue(cacheKey, out var known)) return known;
        _probe.fontSize = size;
        var height = Math.Max(size * 1.3, _probe.GetPreferredValues(text, (float)width, 0f).y + 2); // +2: rounding room
        if (_heights.Count > 4000) _heights.Clear();
        _heights[cacheKey] = height;
        return height;
    }

    static readonly List<ChatLinkDto> NoLinks = new();

    const int MaxItemIcons = 6;
    double ItemIconSize => Math.Max(28, _size * 2.0);

    Sprite ItemSprite(ChatLinkDto link) =>
        link.Icon.Length == 0 ? null : UiKit.LoadSprite(System.IO.Path.Combine(_ui.AssetsRoot, _window, "assets", "items", link.Icon + ".png"));

    /// <summary>The height of the row of item pictures under a message (0 when none of its items has a picture).</summary>
    double ItemRowHeight(List<ChatLinkDto> links)
    {
        for (var i = 0; i < links.Count && i < MaxItemIcons; i++)
            if (ItemSprite(links[i]) != null) return ItemIconSize + 4;
        return 0;
    }

    /// <summary>The pictures of the items of a message, left to right under its text.</summary>
    void PlaceItemIcons(Bubble bubble, double top, double rowHeight)
    {
        var size = ItemIconSize;
        for (var i = 0; i < MaxItemIcons; i++)
        {
            var sprite = rowHeight > 0 && i < bubble.Links.Count ? ItemSprite(bubble.Links[i]) : null;
            var image = bubble.Icons[i];
            image.gameObject.SetActive(sprite != null);
            if (sprite == null) continue;
            image.sprite = sprite;
            UiKit.Place(image.gameObject, 6 + 8 + i * (size + 4), top, size, size);
        }
    }

    static Color QualityColor(int quality) => quality switch
    {
        <= 1 => Rgb(190, 190, 190),
        2 => Rgb(120, 214, 120),
        3 => Rgb(100, 170, 255),
        4 => Rgb(190, 140, 255),
        5 => Rgb(255, 180, 80),
        _ => Rgb(255, 110, 110),
    };

    /// <summary>A click on a linked item in a message: the game's own item card opens, as when its chat link is clicked.</summary>
    void ItemClicked(Bubble bubble, Vector2 screenPoint)
    {
        var canvas = bubble.Body.canvas == null ? null : bubble.Body.canvas.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; // the chat is drawn by the game's camera
        var index = TMP_TextUtilities.FindIntersectingLink(bubble.Body, new Vector3(screenPoint.x, screenPoint.y, 0f), camera);
        if (index < 0 || index >= bubble.Links.Count) return;
        // the game's own link in its chat bubble is clicked when that bubble is still in the game's list (the card then carries the item's own
        // data); otherwise the card is asked for by the item id
        var link = bubble.Links[index];
        var name = link.Token.Length > 0 ? link.Token : link.Name;
        // best: the game's own data of this message (kept in its chat data whatever channel its windows show), opened the way its chat link does
        if (bubble.Source != null && ItemLink.Open(bubble.Source.Channel, bubble.Source.MsgId, link.ItemId))
        {
            ItemCardMover.Request(screenPoint);
            return;
        }

        if (GameChatUi.ClickItemLink(bubble.Who, name))
        {
            ItemCardMover.Request(screenPoint); // the game opens the card at its own bubble: it is moved to where the user pressed
            return;
        }

        // not among the bubbles the game keeps: look back through its list (a line without a person, like loot, has no bubble to find)
        if (bubble.Who.Length > 0) CardSeeker.StartItem(bubble.Who, name, link.ItemId, screenPoint);
        else GameItemTips.Show(link.ItemId, screenPoint);
    }

    Bubble Take(int index)
    {
        while (_bubbles.Count <= index) _bubbles.Add(new Bubble(_list.transform, Copy, OpenCard, ItemClicked, rows => ScrollBy(-rows * RowPixels)));
        return _bubbles[index];
    }

    int _stickerSeen;
    /// <summary>A click on a name: the game's own card of that character opens, by the character's id (a line without an id falls back to the game's chat bubble of that person).</summary>
    void OpenCard(Bubble bubble)
    {
        if (bubble.CharId != 0 && IdCard.Open(bubble.CharId)) return;
        if (bubble.Who.Length > 0) GameChatUi.OpenCard(bubble.Who);
    }

    Bubble _copied;
    DateTime _copiedAt;
    static readonly Color CopyIdle = new(0.22f, 0.3f, 0.45f, 0.9f);
    static readonly Color CopyDone = new(0.2f, 0.55f, 0.38f, 1f);

    /// <summary>The copy button of a bubble: the original text goes to the clipboard and the button turns green for a moment.</summary>
    void Copy(Bubble bubble)
    {
        if (bubble.Original.Length == 0) return;
        _ui.Send(new CommandDto { Window = _window, Action = CommandAction.Copy, Value = bubble.Original });
        if (_copied != null) _copied.CopyBack.color = CopyIdle;
        _copied = bubble;
        _copiedAt = DateTime.UtcNow;
        bubble.CopyBack.color = CopyDone;
    }

    /// <summary>Every frame: the green of a pressed copy button fades back after a second.</summary>
    public void Tick()
    {
        if (Time.realtimeSinceStartup >= _nextLine)
        {
            _nextLine = Time.realtimeSinceStartup + 1f;
            if (_world == 0)
            {
                var number = GameChatUi.WorldChannelNumber(); // only until the packets told the number
                if (number > 0)
                {
                    _world = number;
                    GameChatInput.WorldNumber = number;
                    _signature = "";
                    Redraw();
                }
            }

        }

        DevTools.Tick();
        ShowSendResult();
        if (_pickerOpen && (_pickerSeen != RecentStickers.Version || _stickerVersionSeen != StickerCache.Version || Time.realtimeSinceStartup >= _pickerNext))
        {
            _pickerNext = Time.realtimeSinceStartup + 0.5f; // also while the client's list of pictures is still being read
            PaintPicker();
        }

        if (_tabSeen != TabIcons.Version)
        {
            _tabSeen = TabIcons.Version; // the game's tab pictures just arrived
            PaintRail();
        }

        if (_stickerSeen != StickerCache.Version)
        {
            _stickerSeen = StickerCache.Version; // a picture was just copied: draw it now, not with the next chat message
            Redraw();
        }

        if (_copied == null || (DateTime.UtcNow - _copiedAt).TotalSeconds < 1) return;
        _copied.CopyBack.color = CopyIdle;
        _copied = null;
    }

    void Lay(Bubble bubble, string name, string body, Color color, double top, double listWidth, double bodyHeight, string nameText, string original, List<ChatLinkDto> links = null, Color? ink = null, bool mine = false, ChatLineDto source = null)
    {
        bubble.Source = source;
        var charId = source?.CharId ?? 0;
        bubble.Links = links ?? NoLinks;
        var nameHeight = string.IsNullOrEmpty(nameText) ? 0 : _size * 1.3;
        var itemRow = ItemRowHeight(bubble.Links);
        var boxHeight = Pad + nameHeight + bodyHeight + itemRow + Pad;
        var width = listWidth - 12;
        bubble.Root.SetActive(true);
        UiKit.Place(bubble.Edge.gameObject, 5, top - 1, width + 2, boxHeight + 2);
        UiKit.Place(bubble.Fill.gameObject, 6, top, width, boxHeight);
        bubble.Name.fontSize = _size;
        bubble.Name.gameObject.SetActive(nameHeight > 0);
        // what the player said is shown on the right and has no copy button (they typed it themself)
        var align = mine ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        var copyRoom = mine ? 0 : 28;
        bubble.Name.alignment = align;
        bubble.Body.alignment = align;
        UiKit.SetText(bubble.Name, nameText);
        UiKit.Place(bubble.Name.gameObject, 6 + 8, top + Pad, width - 16 - copyRoom, nameHeight);
        bubble.Who = name ?? "";
        bubble.NameHit.SetActive(nameHeight > 0 && (bubble.Who.Length > 0 || charId != 0));
        UiKit.Place(bubble.NameHit, 6 + 8, top + Pad, width - 16 - copyRoom, nameHeight);
        var stickerName = StickerCache.NameOf(original);
        var stickerTexture = StickerCache.Get(stickerName);
        if (stickerName != null || mine) original = ""; // nothing to copy from a picture, and the player's own words get no copy button
        bubble.Original = original;
        bubble.CopyBox.SetActive(original.Length > 0);
        if (!ReferenceEquals(_copied, bubble)) bubble.CopyBack.color = CopyIdle; // a reused bubble starts plain
        UiKit.Place(bubble.CopyBox, 6 + width - 8 - 22, top + Pad - 1, 22, 22); // right of the original text
        bubble.Body.fontSize = _size;
        bubble.Body.color = ink ?? color;
        UiKit.SetText(bubble.Body, body);
        UiKit.Place(bubble.Body.gameObject, 6 + 8, top + Pad + nameHeight, width - 16, bodyHeight);
        bubble.Sticker.gameObject.SetActive(stickerTexture != null);
        if (stickerTexture != null)
        {
            bubble.Sticker.texture = stickerTexture;
            var side = Math.Min(StickerSize, bodyHeight);
            UiKit.Place(bubble.Sticker.gameObject, mine ? 6 + width - 8 - side : 6 + 8, top + Pad + nameHeight, side, side);
            bubble.Body.gameObject.SetActive(false);
        }
        else bubble.Body.gameObject.SetActive(true);
        PlaceItemIcons(bubble, top + Pad + nameHeight + bodyHeight, itemRow);
    }

    sealed class Bubble
    {
        public readonly GameObject Root;
        public readonly Image Edge;
        public readonly Image Fill;
        public readonly TextMeshProUGUI Name;
        public readonly TextMeshProUGUI Body;
        public readonly RawImage Sticker;
        public readonly GameObject CopyBox;
        public readonly GameObject NameHit; // a click on the name opens the character's card
        public string Who = "";
        public long CharId => Source?.CharId ?? 0;
        public ChatLineDto Source;
        public readonly Image CopyBack;
        public string Original = "";
        public List<ChatLinkDto> Links = NoLinks;
        public readonly Image[] Icons = new Image[MaxItemIcons];

        public Bubble(Transform parent, Action<Bubble> copy, Action<Bubble> open, Action<Bubble, Vector2> item, Action<int> scroll)
        {
            Root = UiKit.Node("Bubble", parent);
            UiKit.Stretch(Root); // the parts below are placed from this object's top-left, so it must cover the whole list (a bare object is 100x100 around the centre)
            var sprite = UiKit.NineSlice("bubble", 20f);
            Edge = UiKit.Box("Edge", Root.transform, BubbleEdge);
            Fill = UiKit.Box("Fill", Root.transform, BubbleFill);
            if (sprite != null)
            {
                Edge.sprite = sprite;
                Edge.type = Image.Type.Sliced;
                Fill.sprite = sprite;
                Fill.type = Image.Type.Sliced;
            }

            Name = UiKit.Text("Name", Root.transform, 15, Color.white, TextAlignmentOptions.TopLeft);
            var hit = UiKit.Button("NameHit", Root.transform, "", new Color(1f, 1f, 1f, 0.004f), () => open(this));
            NameHit = hit.transform.parent.gameObject;
            UiKit.Scroller(NameHit, RowPixels, scroll); // the name line must not swallow the wheel and the drag
            Body = UiKit.Text("Body", Root.transform, 15, Color.white, TextAlignmentOptions.TopLeft);
            Body.enableWordWrapping = true;
            Body.overflowMode = TextOverflowModes.Overflow;
            Body.raycastTarget = true; // a click on a linked item in the text opens its card
            UiKit.Scroller(Body.gameObject, RowPixels, scroll); // the text must not swallow the wheel and the drag
            var click = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
            click.callback.AddListener((UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData>)(data =>
            {
                try
                {
                    var pointer = data.TryCast<UnityEngine.EventSystems.PointerEventData>();
                    if (pointer != null && Links.Count > 0 && !pointer.dragging) item(this, pointer.position);
                }
                catch (Exception e)
                {
                    Plugin.L.LogWarning("item link: " + e.Message);
                }
            }));
            Body.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>().triggers.Add(click);

            for (var i = 0; i < Icons.Length; i++)
            {
                Icons[i] = UiKit.Box("ItemIcon" + i, Root.transform, Color.white);
                Icons[i].preserveAspect = true;
                Icons[i].gameObject.SetActive(false);
            }

            var sticker = new GameObject("Sticker");
            sticker.transform.SetParent(Root.transform, false);
            sticker.layer = Root.layer;
            Sticker = sticker.AddComponent<RawImage>();
            Sticker.raycastTarget = false;
            sticker.SetActive(false);

            // copy button at the right end of the name line
            var label = UiKit.Button("Copy", Root.transform, "복사", CopyIdle, () => copy(this));
            CopyBox = label.transform.parent.gameObject;
            UiKit.Scroller(CopyBox, RowPixels, scroll);
            CopyBack = CopyBox.GetComponent<Image>();
            var icon = UiKit.Icon("copy");
            if (icon != null) UiKit.SetIcon(label, icon);
            else label.fontSize = 10;
        }
    }

    static string Escape(string text) => text.Replace("<", "<​");

    static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    static float Brightness(Color c) => (Math.Max(c.r, Math.Max(c.g, c.b)) + Math.Min(c.r, Math.Min(c.g, c.b))) / 2f;
}
