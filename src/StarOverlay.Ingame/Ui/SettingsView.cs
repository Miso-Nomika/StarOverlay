using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The settings window of one plugin, drawn in the game (no separate process or window): a settings tab with toggles,
/// number steppers, choices and buttons, and, for plugins with fight records, a records tab (list, details, delete).
/// The content is rebuilt only when it changes, so drags and clicks are never interrupted.
/// </summary>
internal sealed partial class SettingsView
{
    const float Header = 30f;
    const float Pad = 8f;
    const float RowHeight = 30f;

    static readonly Color Panel = new(0.07f, 0.08f, 0.1f, 0.94f);
    static readonly Color Bar = new(0.12f, 0.14f, 0.18f, 1f);
    static readonly Color Button = new(0.22f, 0.3f, 0.45f, 1f);
    static readonly Color ButtonOn = new(0.2f, 0.5f, 0.38f, 1f);
    static readonly Color ButtonWarn = new(0.6f, 0.25f, 0.25f, 1f);
    static readonly Color Row = new(1f, 1f, 1f, 0.06f);
    static readonly Color RowSelected = new(0.35f, 0.5f, 0.8f, 0.35f);

    readonly UiRoot _root;
    readonly string _plugin;
    readonly GameObject _window;
    readonly GameObject _panel;
    readonly GameObject _header;
    readonly TextMeshProUGUI _title;
    readonly GameObject _body;
    readonly GameObject _handle;
    readonly List<GameObject> _buttons = new();
    WindowDto _dto;
    LayoutDto _live;
    string _signature = "";
    string _headerSignature = "";
    string _tab = "settings";
    List<RecordHeadDto> _heads = new();
    readonly HashSet<string> _checked = new(); // records ticked for the bulk delete
    string _comboOpen = "";            // the key of the choice list that is open (a combo box)
    (SettingDto Setting, double Y, double Right, double Width)? _comboRow; // where its box is, so the list can be drawn under it
    RecordDto _record;
    string _selectedPlayer = "";
    string _detail = "damage";
    int _rScroll;
    int _pScroll;
    int _sScroll;
    int _bScroll;
    int _scroll;

    public bool Dead;

    public SettingsView(UiRoot root, string plugin)
    {
        _root = root;
        _plugin = plugin;
        _window = UiKit.Node("Settings_" + plugin, root.SettingsParent);
        _panel = UiKit.Node("Panel", _window.transform);
        UiKit.Stretch(_panel);
        UiKit.Stretch(UiKit.Box("Background", _panel.transform, Panel, raycast: true).gameObject);
        UiKit.Scroll(_panel, steps =>
        {
            if (_dto == null || _tab != "settings") return;
            var next = Math.Clamp(_scroll + steps, 0, MaxSettingsScroll(_dto));
            if (next == _scroll) return;
            _scroll = next;
            Dirty();
        });
        _probe = UiKit.Text("Probe", _window.transform, 14, new Color(0f, 0f, 0f, 0f), TextAlignmentOptions.TopLeft);
        _probe.enableWordWrapping = true;
        _probe.overflowMode = TextOverflowModes.Overflow;
        UiKit.Place(_probe.gameObject, 0, 0, 10, 10);
        _body = UiKit.Node("Body", _panel.transform);
        _body.AddComponent<RectMask2D>();
        _header = UiKit.Node("Header", _window.transform);
        var bar = UiKit.Box("Bar", _header.transform, Bar, raycast: true);
        UiKit.Stretch(bar.gameObject);
        _title = UiKit.Text("Title", _header.transform, 15, Color.white, TextAlignmentOptions.MidlineLeft);
        _handle = UiKit.Node("Resize", _window.transform);
        var grip = UiKit.Box("Grip", _handle.transform, new Color(0.62f, 0.62f, 0.66f, 0.9f), raycast: true);
        grip.sprite = UiKit.Icon("grip");
        UiKit.Stretch(grip.gameObject);

        UiKit.Drag(bar.gameObject, delta =>
        {
            if (_dto == null) return;
            _live = LayoutMath.Moved(_live ?? _dto.SettingsLayout, delta.x, delta.y);
            ApplyLayout(_live);
        }, () =>
        {
            if (_live == null) return;
            _root.Send(new CommandDto { Window = _plugin + ":settings", Action = CommandAction.Move, X = _live.X, Y = _live.Y });
            _live = null;
        });
        UiKit.Drag(grip.gameObject, delta =>
        {
            if (_dto == null) return;
            _live = LayoutMath.Resized(_live ?? _dto.SettingsLayout, delta.x, delta.y);
            ApplyLayout(_live);
        }, () =>
        {
            if (_live == null) return;
            _root.Send(new CommandDto { Window = _plugin + ":settings", Action = CommandAction.Resize, W = _live.W, H = _live.H });
            _live = null;
            Dirty(); // the body is laid out for the new size
        });
    }

    /// <summary>Moves the window under another canvas (once the shared camera canvas exists); a window already there stays.</summary>
    public void MoveTo(Transform canvas)
    {
        if (_window == null || _window.transform.parent == canvas) return;
        _window.transform.SetParent(canvas, false);
        UiKit.SetLayer(_window, canvas.gameObject.layer);
    }

    public void Destroy()
    {
        Dead = true;
        if (_window != null) UnityEngine.Object.Destroy(_window);
    }

    public void Apply(WindowDto dto)
    {
        _dto = dto;
        var layout = _live ?? dto.SettingsLayout;
        ApplyLayout(layout);
        UiKit.SetText(_title, dto.Title + (_tab == "records" ? " 기록" : _tab == "editor" ? " 편집" : " 설정"));
        BuildHeaderButtons(dto);

        var signature = Signature(dto, layout);
        if (signature == _signature) return;
        _signature = signature;
        Rebuild(dto, layout);
    }

    /// <summary>Redraws at once (snapshots only arrive when plugin data changes, so a click must not wait for one).</summary>
    bool _redraw;

    /// <summary>Marks the window for a redraw in the next frame (many wheel / drag steps in one frame draw once).</summary>
    void Redraw() => _redraw = true;

    /// <summary>Called every frame by the root.</summary>
    public void Tick()
    {
        if (!_redraw) return;
        _redraw = false;
        Dirty();
    }

    void Dirty()
    {
        _signature = "";
        if (_dto != null) Apply(_dto);
    }

    public void OnRecords(List<RecordHeadDto> heads)
    {
        _heads = heads ?? new List<RecordHeadDto>();
        _checked.IntersectWith(_heads.Select(h => h.Id));
        _rScroll = Math.Clamp(_rScroll, 0, Math.Max(0, _heads.Count - 1));
        Dirty();
    }

    public void OnRecord(RecordDto record)
    {
        _record = record;
        _selectedPlayer = record != null && record.Players.Count > 0 ? record.Players[0].Name : "";
        _pScroll = _sScroll = _bScroll = 0;
        Dirty();
    }

    string Signature(WindowDto dto, LayoutDto layout)
    {
        var sb = new StringBuilder();
        sb.Append(GameImages.Version).Append('|').Append(_tab).Append('|').Append(Math.Round(layout.W, 3)).Append('|').Append(Math.Round(layout.H, 3)).Append('|').Append(_scroll).Append('|');
        if (_tab == "settings")
        {
            foreach (var s in dto.Settings) sb.Append(s.Key).Append('=').Append(s.Value).Append(';');
        }
        else if (_tab == "editor")
        {
            sb.Append(EditorSignature(dto));
        }
        else
        {
            foreach (var h in _heads) sb.Append(h.Id).Append(',');
            sb.Append('#').Append(string.Join(",", _checked.OrderBy(id => id))).Append('#');
            sb.Append('|').Append(_rScroll).Append('|').Append(_record?.Id).Append('|').Append(_selectedPlayer).Append('|').Append(_detail).Append('|').Append(_pScroll).Append(',').Append(_sScroll).Append(',').Append(_bScroll);
        }

        return sb.ToString();
    }

    void ApplyLayout(LayoutDto layout)
    {
        var (x, y, w, h) = LayoutMath.ToReferencePixels(layout);
        UiKit.Place(_window, x, y, w, h);
        UiKit.Place(_header, 0, 0, w, Header);
        UiKit.Place(_body, Pad, Header + 4, Math.Max(10, w - Pad * 2), Math.Max(10, h - Header - 10));
        UiKit.Place(_handle, w - 16, h - 16, 16, 16);
        if (_buttons.Count > 0) PlaceHeaderButtons(w);
    }

    // ---- header: tabs and close

    void BuildHeaderButtons(WindowDto dto)
    {
        var signature = dto.HasRecords + "|" + _tab + "|" + (EditorOf(dto) != null);
        if (signature == _headerSignature) return;
        _headerSignature = signature;
        foreach (var button in _buttons) UnityEngine.Object.Destroy(button);
        _buttons.Clear();
        // a plugin with records shows the other tab as a button ("기록" in the settings, "설정" in the records); the rest only close
        if (_tab != "settings") AddHeader("설정", Button, () => SwitchTab("settings"));
        if (EditorOf(dto) != null && _tab != "editor") AddHeader("편집", Button, () => SwitchTab("editor"));
        if (dto.HasRecords && _tab != "records") AddHeader("기록", Button, () => SwitchTab("records"));
        AddHeader("닫기", ButtonWarn, () => _root.CloseSettings(_plugin));
        PlaceHeaderButtons(LayoutMath.ToReferencePixels(dto.SettingsLayout).W);
    }

    void AddHeader(string caption, Color color, Action click)
    {
        var label = UiKit.Button("Hdr_" + caption, _header.transform, caption, color, click);
        _buttons.Add(label.transform.parent.gameObject);
    }

    void PlaceHeaderButtons(double width)
    {
        var x = width - 4;
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var w = 56;
            x -= w;
            UiKit.Place(_buttons[i], x, 3, w, Header - 6);
            x -= 3;
        }

        UiKit.Place(_title.gameObject, 8, 0, Math.Max(10, x - 8), Header);
    }

    public void ShowSettingsTab() => SwitchTab("settings");

    void SwitchTab(string tab)
    {
        if (_tab == tab) return;
        _tab = tab;
        _scroll = 0;
        _comboOpen = "";
        Dirty();
        _headerSignature = "";
        if (tab == "records") _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Records });
    }

    // ---- body

    void Rebuild(WindowDto dto, LayoutDto layout)
    {
        for (var i = _body.transform.childCount - 1; i >= 0; i--)
        {
            var child = _body.transform.GetChild(i).gameObject;
            if (_regions.ContainsValue(child)) child.SetActive(false); // kept; drawn again (and shown) only when this tab needs it
            else UnityEngine.Object.Destroy(child);
        }

        var (_, _, width, height) = LayoutMath.ToReferencePixels(layout);
        var bw = Math.Max(10, width - Pad * 2);
        var bh = Math.Max(10, height - Header - 10);
        if (_tab == "settings") BuildSettings(dto, bw, bh);
        else if (_tab == "editor") BuildEditor(dto, bw, bh);
        else BuildRecords(bw, bh);
    }

    double _listW, _listH; // size of the settings list when it was last drawn

    /// <summary>The height of one row of the settings list (a long text gets as many lines as it has, up to what the window can show).</summary>
    double SettingHeight(SettingDto setting, double bw, double bh) =>
        setting.Multiline ? Math.Min(TextHeight(setting.Value, bw) + 64, Math.Max(RowHeight, bh)) : setting.Kind == "tool" || setting.Hint.Length > 0 ? RowHeight + 22 : RowHeight;

    /// <summary>
    /// How far the list can be scrolled: the first row from which everything below still fits in the window. When all rows fit there is
    /// nothing to scroll (the wheel must not push the list up and leave empty space under it).
    /// </summary>
    int MaxSettingsScroll(WindowDto dto) =>
        ListScroll.Max(dto.Settings.Select(setting => SettingHeight(setting, _listW, _listH)).ToList(), 4, _listH);

    void BuildSettings(WindowDto dto, double bw, double bh)
    {
        _listW = bw;
        _listH = bh;
        _scroll = Math.Min(_scroll, MaxSettingsScroll(dto)); // a window that grew may have room for everything now
        var rowW = MaxSettingsScroll(dto) > 0 ? bw - 8 : bw; // a list that scrolls keeps a strip free for its bar
        double y = 0;
        var skip = _scroll;
        var shown = 0;
        foreach (var setting in dto.Settings)
        {
            // a long text (the translation prompt) gets as many lines as it has, up to what the window can show
            var h = SettingHeight(setting, bw, bh);
            if (skip > 0)
            {
                skip--;
                continue;
            }

            if (y + h > bh) break;
            SettingRow(setting, y, rowW, h);
            y += h + 4;
            shown++;
        }

        DrawComboList();
        ScrollBar(_body.transform, bw, 0, bh, dto.Settings.Count, shown, _scroll);
        if (dto.Settings.Count == 0)
            Text(_body.transform, "이 플러그인에는 설정이 없습니다.", 0, 0, bw, 24, 15, new Color(0.62f, 0.68f, 0.76f, 1f));
        if (_scroll > 0) Small("▲", bw - 30, 0, () => { _scroll = Math.Max(0, _scroll - 1); Dirty(); });
        if (_scroll + shown < dto.Settings.Count) Small("▼", bw - 30, bh - 26, () => { _scroll++; Dirty(); });
    }

    void SettingRow(SettingDto setting, double y, double bw, double h)
    {
        var back = UiKit.Box("Row_" + setting.Key, _body.transform, Row);
        UiKit.Place(back.gameObject, 0, y, bw, h);
        Text(back.transform, setting.Label, 8, 0, bw * 0.46, 30, 15, Color.white);
        var right = bw - 8;
        if (setting.Hint.Length > 0)
            Text(back.transform, Esc(setting.Hint), 8, 28, bw - 16, 22, 12, new Color(0.55f, 0.6f, 0.68f, 1f)); // grey, under the control
        if (setting.Kind == "toggle")
        {
            var on = setting.Value == "true";
            Btn(back.transform, on ? "켜짐" : "꺼짐", right - 80, 3, 80, 24, on ? ButtonOn : Button, () =>
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Toggle, Key = setting.Key, Value = on ? "false" : "true" }));
        }
        else if (setting.Kind == "tool")
        {
            // the translation tool: one row for its state and both actions (download, delete the model in use)
            var live = setting.Value.StartsWith("받는 중", StringComparison.Ordinal);
            Text(back.transform, Esc(setting.Value), 8, 28, bw - 16, 22, 13, live ? new Color(1f, 0.85f, 0.54f, 1f) : new Color(0.8f, 0.85f, 0.92f, 1f));
            // a model is there: the delete button; something is missing (or a download runs): the download button
            var buttonRight = right;
            if (setting.CanDelete)
            {
                Btn(back.transform, "모델 삭제", buttonRight - 96, 3, 96, 24, ButtonWarn, () =>
                    _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Press, Key = setting.Default }));
                buttonRight -= 96 + 8;
            }

            if (setting.CanDownload)
                Btn(back.transform, live ? "받는 중" : "받기", buttonRight - 80, 3, 80, 24, live ? ButtonOn : Button, () =>
                    _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Press, Key = setting.Key }));
        }
        else if (setting.Kind == "button")
        {
            Btn(back.transform, "실행", right - 80, 3, 80, 24, Button, () =>
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Press, Key = setting.Key }));
        }
        else if (setting.Choices.Count > 0)
        {
            // a combo box: the box shows the current choice, a click opens the list under it (short choices read as capitals;
            // a file name, the translation model, is shown without ".gguf")
            var choiceLabel = Short(setting.Value);
            var choiceWidth = Math.Clamp(Math.Max(choiceLabel.Length * 8.5, setting.Choices.Select(c => Short(c).Length * 8.5).DefaultIfEmpty(0).Max()) + 40, 110, Math.Max(110, bw * 0.54 - 8));
            var open = _comboOpen == setting.Key;
            Btn(back.transform, choiceLabel + (open ? "  ▲" : "  ▼"), right - choiceWidth, 3, choiceWidth, 24, open ? ButtonOn : Button, () =>
            {
                _comboOpen = open ? "" : setting.Key;
                Dirty();
            });
            if (open) _comboRow = (setting, y, right, choiceWidth);
        }
        else if (setting.Max > setting.Min)
        {
            int.TryParse(setting.Value, out var current);
            var step = setting.Step > 0 ? setting.Step : Math.Max(1, (setting.Max - setting.Min) / 20);
            Btn(back.transform, "+", right - 28, 3, 28, 24, Button, () => Step(setting, current + step));
            Text(back.transform, setting.Value, right - 90, 0, 56, 30, 15, Color.white, TextAlignmentOptions.MidlineRight);
            Btn(back.transform, "-", right - 120, 3, 28, 24, Button, () => Step(setting, current - step));
        }
        else if (setting.Multiline)
        {
            var preview = Text(back.transform, Esc(setting.Value), 8, 30, bw - 16, Math.Max(20, h - 64), 14, new Color(0.8f, 0.85f, 0.92f, 1f));
            preview.enableWordWrapping = true;
            preview.overflowMode = TextOverflowModes.Ellipsis;
            preview.alignment = TextAlignmentOptions.TopLeft;
            Btn(back.transform, "입력하기", right - 110, h - 28, 110, 22, Button, () =>
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Prompt }));
        }
        else
        {
            Text(back.transform, setting.Value, bw * 0.46, 0, bw * 0.54 - 8, 30, 15, new Color(0.8f, 0.85f, 0.92f, 1f), TextAlignmentOptions.MidlineRight);
        }
    }

    /// <summary>The text of a choice as shown: a file name without ".gguf", very short ones (cpu / gpu) in capitals.</summary>
    static string Short(string value) =>
        value.Length <= 4 ? value.ToUpperInvariant() : value.Replace(".gguf", "", StringComparison.OrdinalIgnoreCase);

    /// <summary>The open choice list of a combo box, drawn last so it lies over the rows below it.</summary>
    void DrawComboList()
    {
        if (_comboOpen.Length == 0 || _comboRow is null) return;
        var (setting, y, right, width) = _comboRow.Value;
        _comboRow = null;
        var list = UiKit.Box("ComboList", _body.transform, new Color(0.05f, 0.06f, 0.08f, 0.98f), raycast: true);
        const double item = 26;
        UiKit.Place(list.gameObject, right - width, y + 28, width, setting.Choices.Count * item + 6); // lies over the hint line and the rows below
        for (var i = 0; i < setting.Choices.Count; i++)
        {
            var choice = setting.Choices[i];
            var current = choice == setting.Value;
            Btn(list.transform, Short(choice), 3, 3 + i * item, width - 6, item - 2, current ? ButtonOn : Button, () =>
            {
                _comboOpen = "";
                if (!current) _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Set, Key = setting.Key, Value = choice });
                Dirty();
            });
        }
    }

    TextMeshProUGUI _probe; // an invisible label the text is measured with (it has to be active for the text engine)
    readonly Dictionary<string, double> _textHeights = new();

    /// <summary>The height the text really needs when wrapped to the row width (measured with the same font and size it is drawn with).</summary>
    double TextHeight(string text, double width)
    {
        var shown = Esc(text);
        var key = shown.GetHashCode() + "|" + shown.Length + "|" + (int)width;
        if (_textHeights.TryGetValue(key, out var known)) return known;
        _probe.fontSize = 14;
        var height = Math.Max(18, _probe.GetPreferredValues(shown, (float)(width - 16), 0f).y + 2);
        if (_textHeights.Count > 200) _textHeights.Clear();
        _textHeights[key] = height;
        return height;
    }

    void Step(SettingDto setting, int value) =>
        _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Set, Key = setting.Key, Value = Math.Clamp(value, setting.Min, setting.Max).ToString() });

    void BuildRecords(double bw, double bh)
    {
        var muted = new Color(0.62f, 0.68f, 0.76f, 1f);
        // top to bottom: record choice, the people of that record, the damage curve, then the damage / buff tabs
        const double rowH = 22;
        const double listRowH = 26;
        var w = bw;
        var visibleRecords = Math.Clamp(_heads.Count, 1, 6);
        var recordsH = 22 + visibleRecords * listRowH;

        // 1. record choice (a list that scrolls with the wheel and by dragging)
        Text(_body.transform, $"기록 선택 ({_heads.Count})", 0, 0, 200, 22, 13, muted);
        // delete at once: no second question. Ticked records go together; with none ticked the opened record goes.
        var infoRight = 0.0;
        if (_checked.Count > 0 || _record != null)
        {
            Btn(_body.transform, _checked.Count > 0 ? $"삭제 ({_checked.Count})" : "삭제", w - 80, 0, 80, 22, ButtonWarn, () =>
            {
                var ids = _checked.Count > 0 ? string.Join(",", _checked) : _record!.Id;
                if (_record != null && ids.Split(',').Contains(_record.Id)) _record = null;
                _checked.Clear();
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.RecordDelete, Key = ids });
                Dirty();
            });
            infoRight = 88;
        }

        if (_heads.Count > 0)
        {
            Btn(_body.transform, "오래된 10개 삭제", w - 80 - 8 - 130 + (infoRight == 0 ? 80 + 8 : 0), 0, 130, 22, ButtonWarn, () =>
            {
                if (_record != null && _heads.OrderBy(h => h.Started).Take(10).Any(h => h.Id == _record.Id)) _record = null;
                _checked.Clear();
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.RecordDelete, Key = "oldest:10" });
                Dirty();
            });
            infoRight += 138;
        }

        if (_record != null)
            Text(_body.transform, $"{Esc(_record.Name)}  <color=#9ea9bb>{_record.Started}{(_record.EndReason.Length > 0 ? " · </color><color=" + ReasonColor(_record.EndReason) + ">" + _record.EndReason + "</color><color=#9ea9bb>" : "")} · {TimeSpan.FromSeconds(_record.Seconds):mm\\:ss}  ·  총 피해 {UnitFormat.Short(_record.TotalDamage)}</color>", 200, 0, Math.Max(40, w - 200 - infoRight - 8), 22, 13, Color.white);

        var maxRecordScroll = Math.Max(0, _heads.Count - visibleRecords);
        _rScroll = Math.Clamp(_rScroll, 0, maxRecordScroll);
        var recordRegion = Region("records", 0, 22, w, visibleRecords * listRowH, listRowH, steps =>
        {
            _rScroll = Math.Clamp(_rScroll + steps, 0, maxRecordScroll);
            Redraw();
        });
        if (_heads.Count == 0) Text(recordRegion.transform, "저장된 기록이 없습니다.", 8, 0, w - 16, listRowH, 14, muted);
        var rw = _heads.Count > visibleRecords ? w - 8 : w; // the bar has its own strip at the right edge: nothing is drawn under it
        double ry = 0;
        for (var i = _rScroll; i < Math.Min(_heads.Count, _rScroll + visibleRecords); i++)
        {
            var head = _heads[i];
            var selected = _record != null && _record.Id == head.Id;
            var item = UiKit.Button("Rec_" + head.Id, recordRegion.transform, "", selected ? RowSelected : Row, () =>
                _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.RecordOpen, Key = head.Id }));
            var box = item.transform.parent.gameObject;
            UiKit.Place(box, 0, ry, rw, listRowH - 2);
            item.text = "";
            // two columns without a line between them: why the fight ended (yellow, always the same width so the titles line up), then the title
            const double reasonColumn = 100;
            if (head.EndReason.Length > 0)
                Text(box.transform, "<color=" + ReasonColor(head.EndReason) + ">" + Esc(Fit(head.EndReason, reasonColumn - 8)) + "</color>", 8, 0, reasonColumn - 8, listRowH - 2, 13, Color.white);
            Text(box.transform, Esc(Fit(head.Name, rw - 350 - reasonColumn)), 8 + reasonColumn, 0, rw - 350 - reasonColumn, listRowH - 2, 13, Color.white);
            Text(box.transform, head.Started + (head.Current ? "  <color=#8fe0a8>진행 중</color>" : ""), rw - 322, 0, 270, listRowH - 2, 13, muted, TextAlignmentOptions.MidlineRight);
            if (!head.Current)
            {
                var ticked = _checked.Contains(head.Id);
                var tick = UiKit.Button("Tick_" + head.Id, box.transform, ticked ? "V" : "", ticked ? ButtonOn : new Color(0.16f, 0.19f, 0.25f, 1f), () =>
                {
                    if (!_checked.Add(head.Id)) _checked.Remove(head.Id);
                    Dirty();
                });
                UiKit.Place(tick.transform.parent.gameObject, rw - 32, (listRowH - 2 - 20) / 2, 24, 20);
            }

            ry += listRowH;
        }

        ScrollBar(recordRegion.transform, w, 0, visibleRecords * listRowH, _heads.Count, visibleRecords, _rScroll);
        if (maxRecordScroll > 0)
            Text(_body.transform, $"{_rScroll + 1}-{Math.Min(_heads.Count, _rScroll + visibleRecords)} / {_heads.Count}  (휠로 넘기기)", w - 260, 22 + visibleRecords * listRowH, 252, 16, 11, muted, TextAlignmentOptions.MidlineRight);

        var top = 22 + visibleRecords * listRowH + 16 + 8; // the page note sits in its own strip under the list
        if (_record == null)
        {
            Text(_body.transform, _heads.Count == 0 ? "" : "위에서 기록을 고르세요.", 0, top, w, 24, 15, muted);
            return;
        }

        // 2. the people of the record: UID, name, job, power, cuisine, syrup, damage, dps, share (the wheel scrolls when there are many)
        var shownPlayers = Math.Clamp(_record.Players.Count, 1, 10); // ten people at most; the rest of the height goes to the skill / buff rows below
        var maxPlayerScroll = Math.Max(0, _record.Players.Count - shownPlayers);
        _pScroll = Math.Clamp(_pScroll, 0, maxPlayerScroll);
        var playerRegion = Region("players", 0, top, w, 20 + shownPlayers * rowH + 16, rowH, steps =>
        {
            _pScroll = Math.Clamp(_pScroll + steps, 0, maxPlayerScroll);
            Redraw();
        });
        var pw = _record.Players.Count > shownPlayers ? w - 8 : w;
        var pc = Columns(pw, 9, 16, 8, 8, 15, 5, 5, 12, 12, 9);
        var ph = new[] { "UID", "이름", "직업", "이매진", "전투력", "요리", "시럽", "총 피해", "DPS", "지분" };
        for (var c = 0; c < ph.Length; c++)
            Text(playerRegion.transform, ph[c], pc[c].X + 4, 0, pc[c].W - 8, 20, 12, muted, c is 3 or 5 or 6 ? TextAlignmentOptions.Midline : c >= 4 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft); // cuisine and syrup: header and icon both centred
        if (maxPlayerScroll > 0)
            Text(playerRegion.transform, $"{_pScroll + 1}-{Math.Min(_record.Players.Count, _pScroll + shownPlayers)} / {_record.Players.Count}  (휠로 넘기기)", w - 260, 20 + shownPlayers * rowH, 252, 16, 11, muted, TextAlignmentOptions.MidlineRight);
        double py = 20;
        for (var i = _pScroll; i < Math.Min(_record.Players.Count, _pScroll + shownPlayers); i++)
        {
            var player = _record.Players[i];
            var selected = player.Name == _selectedPlayer;
            var row = UiKit.Button("P_" + i, playerRegion.transform, "", selected ? RowSelected : Row, () =>
            {
                _selectedPlayer = player.Name;
                _sScroll = 0;
                _bScroll = 0;
                Dirty();
            });
            var box = row.transform.parent.gameObject;
            UiKit.Place(box, 0, py, pw, rowH - 2);
            row.text = "";
            var share = UiKit.Box("Share", box.transform, UiKit.Rgb(player.BarColor == 0 ? 0x4A90D9 : player.BarColor, 0.55f));
            var rt = UiKit.Rt(share.gameObject);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2((float)Math.Clamp(player.Bar, 0, 1), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            share.transform.SetAsFirstSibling();
            var cells = new[]
            {
                player.CharId == 0 ? "" : player.CharId.ToString(),
                (player.Self ? "★ " : "") + Esc(Fit(player.Name, pc[1].W - 8 - (player.FirstAttacker ? 20 : 0))),
                Esc(Fit(player.Profession, pc[2].W - 8)),
                "", // the imagines are icons (below), like in the meter
                player.FightPoint == 0 ? "" : player.FightPoint.ToString("N0") + (player.SeasonStrength > 0 ? "+" + player.SeasonStrength : ""), // power+season strength, like the meter
                "", // the cuisine and syrup are icons (below), like in the meter
                "",
                UnitFormat.Short(player.Total),
                UnitFormat.Short(player.PerSecond),
                UnitFormat.Percent(player.Share),
            };
            for (var c = 0; c < cells.Length; c++)
                Text(box.transform, cells[c], pc[c].X + 4, 0, pc[c].W - 8, rowH, 13, player.TextColor != 0 ? UiKit.Rgb(player.TextColor) : Color.white, c is 3 or 5 or 6 ? TextAlignmentOptions.Midline : c >= 4 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            if (player.FirstAttacker) FirstFlag(box.transform, pc[1], rowH);
            ImagineIcons(box.transform, pc[3], rowH, player.Imagines);
            MealIcon(box.transform, "food", pc[5], rowH, player.Cuisine, player.CuisineEffect, "요리");
            MealIcon(box.transform, "syrup", pc[6], rowH, player.Syrup, player.SyrupEffect, "시럽");
            py += rowH;
        }

        ScrollBar(playerRegion.transform, w, 20, shownPlayers * rowH, _record.Players.Count, shownPlayers, _pScroll);
        var chosen = _record.Players.FirstOrDefault(p => p.Name == _selectedPlayer);
        top += 20 + shownPlayers * rowH + 16 + 8;
        if (chosen == null) return;

        // 3. damage per second over the fight
        var graphH = 70.0; // fixed, so a taller window shows more skill / buff rows instead of a taller graph
        DrawGraph(0, top, w, graphH, chosen.Samples);
        top += graphH + 8;

        // 4. one area for the player's skills and buffs, picked with the tabs at its top left
        var lowerH = bh - top;
        if (lowerH < 60) return;
        Btn(_body.transform, "피해", 0, top, 70, 24, _detail == "damage" ? ButtonOn : Button, () => SwitchDetail("damage"));
        Btn(_body.transform, "버프", 74, top, 70, 24, _detail == "buff" ? ButtonOn : Button, () => SwitchDetail("buff"));
        Text(_body.transform, Esc(chosen.Name), 156, top, w - 156, 24, 14, new Color(1f, 0.85f, 0.54f, 1f));
        top += 28;
        if (_detail == "damage") DrawSkills(0, top, w, bh - top, chosen);
        else DrawBuffs(0, top, w, bh - top, chosen);
    }
    /// <summary>A small flag at the right end of the name cell of the one who attacked first, with a note on hover.</summary>
    void FirstFlag(Transform row, (double X, double W) column, double rowH)
    {
        const double size = 16;
        var image = UiKit.Box("FirstFlag", row, new Color(1f, 0.8f, 0.3f, 1f), raycast: true);
        image.sprite = UiKit.Icon("flag");
        image.preserveAspect = true;
        UiKit.Place(image.gameObject, column.X + column.W - size - 4, (rowH - 2 - size) / 2, size, size);
        var target = image.gameObject;
        UiKit.Hover(target, () => _root.ShowTip(target, "가장 먼저 공격"), () => _root.HideTip(target));
        UiKit.Scroller(target, rowH, steps => _regionScroll["players"]?.Invoke(steps));
    }

    /// <summary>The two imagines worn in a record's fight as pictures, with name (tier) shown while the cursor rests on one, like in the meter.</summary>
    void ImagineIcons(Transform row, (double X, double W) column, double rowH, List<ImagineDto> imagines)
    {
        var size = Math.Min(rowH - 6, (column.W - 6) / 2);
        if (size < 8) return;
        var left = column.X + (column.W - (size * 2 + 2)) / 2;
        for (var i = 0; i < Math.Min(2, imagines.Count); i++)
        {
            var imagine = imagines[i];
            var file = System.IO.Path.GetFileName((imagine.Icon ?? "").Replace('\\', '/'));
            if (file.Length == 0) continue; // nothing worn in that slot: leave it empty
            if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) file += ".png";
            var image = UiKit.Box("Imagine_" + i, row, Color.white, raycast: true);
            image.sprite = UiKit.LibraryPicture(System.IO.Path.Combine(_root.AssetsRoot, _plugin, "assets", "imagines", file));
            image.preserveAspect = true;
            if (image.sprite == null) image.color = new Color(1f, 1f, 1f, 0.35f);
            UiKit.Place(image.gameObject, left + i * (size + 2), (rowH - 2 - size) / 2, size, size);
            var tip = imagine.Name.Length == 0 ? null : imagine.Tier > 0 ? $"{imagine.Name} ({imagine.Tier})" : imagine.Name;
            var target = image.gameObject;
            if (tip != null) UiKit.Hover(target, () => _root.ShowTip(target, tip), () => _root.HideTip(target));
            UiKit.Scroller(target, rowH, steps => _regionScroll["players"]?.Invoke(steps));
        }
    }

    /// <summary>The cuisine / syrup icon of a person in a record, with what it did shown while the cursor rests on it (like in the meter).</summary>
    void MealIcon(Transform row, string icon, (double X, double W) column, double rowH, bool present, string effect, string fallback)
    {
        if (!present) return;
        var size = Math.Min(rowH - 6, 22);
        var image = UiKit.Box("Meal_" + icon, row, Color.white, raycast: true);
        image.sprite = UiKit.Icon(icon);
        image.preserveAspect = true;
        UiKit.Place(image.gameObject, column.X + (column.W - size) / 2, (rowH - 2 - size) / 2, size, size);
        var tip = effect.Length > 0 ? effect : fallback;
        var target = image.gameObject;
        UiKit.Hover(target, () => _root.ShowTip(target, tip), () => _root.HideTip(target));
        UiKit.Scroller(target, rowH, steps => _regionScroll["players"]?.Invoke(steps)); // the icon must not swallow the wheel and the drag
    }

    void SwitchDetail(string tab)
    {
        if (_detail == tab) return;
        _detail = tab;
        _sScroll = 0;
        _bScroll = 0;
        Dirty();
    }

    /// <summary>
    /// Column positions for a table of <paramref name="total"/> pixels. Every column gets its share of the width by the given
    /// ratio, so widening the window widens all of them in the same proportion (and narrowing squeezes all of them alike).
    /// </summary>
    static (double X, double W)[] Columns(double total, params double[] ratios)
    {
        var sum = ratios.Sum();
        var result = new (double X, double W)[ratios.Length];
        double x = 0;
        for (var i = 0; i < ratios.Length; i++)
        {
            var width = total * ratios[i] / sum;
            result[i] = (x, width);
            x += width;
        }

        return result;
    }
    void DrawGraph(double x, double y, double w, double h, List<RecordSampleDto> samples)
    {
        var back = UiKit.Box("Graph", _body.transform, new Color(1f, 1f, 1f, 0.05f));
        UiKit.Place(back.gameObject, x, y, w, h);
        if (samples.Count == 0)
        {
            Text(back.transform, "기록 없음", 0, 0, w, h, 14, new Color(0.62f, 0.68f, 0.76f, 1f), TextAlignmentOptions.Midline);
            return;
        }

        var peak = Math.Max(1, samples.Max(s => s.PerSecond));
        var last = Math.Max(1, samples.Max(s => s.Second));
        var plotX = 8.0;
        var plotW = w - 16;
        var plotH = h - 22;
        // continuous area: one column per few pixels, each with the damage rate at that moment (straight between the samples),
        // so seconds without a sample do not leave holes
        var ordered = samples.OrderBy(s => s.Second).ToList();
        var columns = Math.Clamp((int)(plotW / 3), 40, 400);
        var colW = plotW / columns;
        for (var c = 0; c < columns; c++)
        {
            var value = RateAt(ordered, last * (c + 0.5) / columns);
            var height = Math.Max(1, plotH * value / peak);
            var left = plotX + c * colW;
            var fill = UiKit.Box("C" + c, back.transform, new Color(0.31f, 0.86f, 0.47f, 0.45f));
            UiKit.Place(fill.gameObject, left, 16 + plotH - height, colW + 0.5, height);
            var edge = UiKit.Box("E" + c, back.transform, new Color(0.45f, 1f, 0.62f, 1f));
            UiKit.Place(edge.gameObject, left, 16 + plotH - height, colW + 0.5, Math.Min(2, height));
        }
        Text(back.transform, $"DPS 최고 {UnitFormat.Short(peak)}", 8, 0, w * 0.5, 16, 12, new Color(0.62f, 0.68f, 0.76f, 1f));
        Text(back.transform, last + "초", w - 68, h - 18, 60, 16, 12, new Color(0.62f, 0.68f, 0.76f, 1f), TextAlignmentOptions.MidlineRight);
    }

    /// <summary>The damage rate at <paramref name="second"/>: before the first sample the first value, after the last the last, in between a straight line.</summary>
    static double RateAt(List<RecordSampleDto> ordered, double second)
    {
        if (second <= ordered[0].Second) return ordered[0].PerSecond;
        for (var i = 1; i < ordered.Count; i++)
        {
            var right = ordered[i];
            if (second > right.Second) continue;
            var left = ordered[i - 1];
            var span = right.Second - left.Second;
            return span <= 0 ? right.PerSecond : left.PerSecond + (right.PerSecond - left.PerSecond) * (second - left.Second) / span;
        }

        return ordered[^1].PerSecond;
    }

    void DrawSkills(double x, double y, double w, double h, RecordPlayerDto player)
    {
        var muted = new Color(0.62f, 0.68f, 0.76f, 1f);
        const double rowH = 22;
        var visible = Math.Max(1, (int)((h - 22 - 16) / rowH));
        var maxScroll = Math.Max(0, player.Skills.Count - visible);
        _sScroll = Math.Clamp(_sScroll, 0, maxScroll);
        var region = Region("skills", x, y, w, h, rowH, steps =>
        {
            _sScroll = Math.Clamp(_sScroll + steps, 0, maxScroll);
            Redraw();
        });
        // UID | skill | damage | hits | crit | lucky | share
        var sw = player.Skills.Count > visible ? w - 8 : w;
        var cols = Columns(sw, 10, 30, 14, 10, 12, 12, 12);
        var names = new[] { "UID", "스킬", "피해", "타수", "치명", "행운", "지분" };
        for (var c = 0; c < names.Length; c++)
            Text(region.transform, names[c], cols[c].X + 4, 0, cols[c].W - 8, 20, 12, muted, c >= 2 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        if (maxScroll > 0)
            Text(region.transform, $"{_sScroll + 1}-{Math.Min(player.Skills.Count, _sScroll + visible)} / {player.Skills.Count}  (휠로 넘기기)", w - 260, h - 17, 252, 16, 11, muted, TextAlignmentOptions.MidlineRight);
        double yy = 22;
        var total = Math.Max(1, player.Total);
        for (var i = _sScroll; i < Math.Min(player.Skills.Count, _sScroll + visible); i++)
        {
            var s = player.Skills[i];
            var share = (double)s.Damage / total;
            var row = UiKit.Box("S" + i, region.transform, Row);
            UiKit.Place(row.gameObject, 0, yy, sw, rowH - 1);
            var bar = UiKit.Box("Bar", row.transform, new Color(0.9f, 0.6f, 0.2f, 0.35f));
            var rt = UiKit.Rt(bar.gameObject);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2((float)Math.Clamp(share, 0.005, 1), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hits = Math.Max(1, s.Hits);
            var cells = new[]
            {
                s.Id == 0 ? "-" : s.Id.ToString(),
                Esc(Fit(s.Name, cols[1].W - 8)),
                UnitFormat.Short(s.Damage),
                s.Hits.ToString(),
                UnitFormat.Percent((double)s.Crits / hits, 1),
                UnitFormat.Percent((double)s.Luckies / hits, 1),
                UnitFormat.Percent(share, 2),
            };
            for (var c = 0; c < cells.Length; c++)
                Text(row.transform, cells[c], cols[c].X + 4, 0, cols[c].W - 8, rowH, 13, c == 0 ? muted : Color.white, c >= 2 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            yy += rowH;
        }

        ScrollBar(region.transform, w, 22, visible * rowH, player.Skills.Count, visible, _sScroll);
        if (player.Skills.Count == 0) Text(region.transform, "사용한 스킬 기록이 없습니다.", 6, yy, w - 12, 22, 13, muted);
    }

    void DrawBuffs(double x, double y, double w, double h, RecordPlayerDto player)
    {
        var muted = new Color(0.62f, 0.68f, 0.76f, 1f);
        const double rowH = 22;
        var visible = Math.Max(1, (int)((h - 22 - 16) / rowH));
        var maxScroll = Math.Max(0, player.Buffs.Count - visible);
        _bScroll = Math.Clamp(_bScroll, 0, maxScroll);
        var region = Region("buffs", x, y, w, h, rowH, steps =>
        {
            _bScroll = Math.Clamp(_bScroll + steps, 0, maxScroll);
            Redraw();
        });
        // time | UID | buff | stacks | duration | cooldown | state
        var fw = player.Buffs.Count > visible ? w - 8 : w;
        var cols = Columns(fw, 8, 10, 30, 8, 12, 12, 8);
        var names = new[] { "시간", "UID", "버프", "중첩", "지속", "쿨타임", "상태" };
        for (var c = 0; c < names.Length; c++)
            Text(region.transform, names[c], cols[c].X + 4, 0, cols[c].W - 8, 20, 12, muted, c >= 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
        if (maxScroll > 0)
            Text(region.transform, $"{_bScroll + 1}-{Math.Min(player.Buffs.Count, _bScroll + visible)} / {player.Buffs.Count}  (휠로 넘기기)", w - 260, h - 17, 252, 16, 11, muted, TextAlignmentOptions.MidlineRight);
        double yy = 22;
        for (var i = _bScroll; i < Math.Min(player.Buffs.Count, _bScroll + visible); i++)
        {
            var b = player.Buffs[i];
            var row = UiKit.Box("F" + i, region.transform, b.Active ? new Color(0.2f, 0.5f, 0.38f, 0.22f) : Row, raycast: true);
            UiKit.Place(row.gameObject, 0, yy, fw, rowH - 1);
            var description = _record.BuffInfo.FirstOrDefault(info => info.Name == b.Name)?.Description ?? "";
            if (description.Length > 0)
            {
                var target = row.gameObject;
                UiKit.Hover(target, () => _root.ShowTip(target, description), () => _root.HideTip(target)); // what the buff does, while the cursor rests on the row
                // the hover handler would swallow the wheel and the drag, so the row scrolls the list itself
                UiKit.Scroller(target, rowH, steps => _regionScroll["buffs"]?.Invoke(steps));
            }

            var cells = new[]
            {
                b.Second + "초",
                b.Id == 0 ? "-" : b.Id.ToString(),
                Esc(Fit(b.Name, cols[2].W - 8)),
                b.Stacks > 0 ? b.Stacks.ToString() : "",
                b.Duration > 0 ? b.Duration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "초" : "",
                b.Cooldown > 0 ? b.Cooldown.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "초" : "",
                b.Active ? "<color=#8fe0a8>시작</color>" : "<color=#ff9a9a>종료</color>",
            };
            for (var c = 0; c < cells.Length; c++)
                Text(row.transform, cells[c], cols[c].X + 4, 0, cols[c].W - 8, rowH, 13, c == 1 ? muted : Color.white, c >= 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            yy += rowH;
        }

        ScrollBar(region.transform, w, 22, visible * rowH, player.Buffs.Count, visible, _bScroll);
        if (player.Buffs.Count == 0) Text(region.transform, "얻은 버프 기록이 없습니다.", 6, yy, w - 12, 22, 13, muted);
    }
    /// <summary>A panel inside the body that scrolls with the mouse wheel.</summary>
    // The list areas live as long as the window: a redraw in the middle of a drag must not destroy the object being dragged.
    readonly Dictionary<string, GameObject> _regions = new();
    readonly Dictionary<string, Action<int>> _regionScroll = new();

    const double ListBarWidth = 3; // the thin bar at the right edge of every list that scrolls (as thin as the chat window's)

    /// <summary>
    /// The thin bar at the right edge of a list: its thumb is as long as the share of the rows that shows and sits where the list is scrolled to.
    /// Nothing is drawn when every row fits. Call it after the rows are made, so the bar lies above them.
    /// </summary>
    void ScrollBar(Transform parent, double right, double y, double height, int total, int visible, int scroll)
    {
        if (total <= visible || height < 24) return;
        var track = UiKit.Box("ScrollTrack", parent, new Color(1f, 1f, 1f, 0.10f));
        var thumb = UiKit.Box("ScrollThumb", parent, new Color(1f, 1f, 1f, 0.5f));
        var length = Math.Max(18, height * visible / total);
        var max = total - visible;
        var offset = (height - length) * Math.Clamp(scroll, 0, max) / max;
        UiKit.Place(track.gameObject, right - ListBarWidth, y, ListBarWidth, height);
        UiKit.Place(thumb.gameObject, right - ListBarWidth, y + offset, ListBarWidth, length);
    }

    GameObject Region(string key, double x, double y, double w, double h, double rowHeight, Action<int> scroll)
    {
        if (!_regions.TryGetValue(key, out var go) || go == null)
        {
            go = UiKit.Box("Region_" + key, _body.transform, new Color(1f, 1f, 1f, 0.03f), raycast: true).gameObject;
            _regions[key] = go;
            UiKit.Scroller(go, rowHeight, steps => _regionScroll[key]?.Invoke(steps)); // wheel and drag
        }

        _regionScroll[key] = scroll; // the newest drawing knows the current limits
        for (var i = go.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
        UiKit.Place(go, x, y, w, h);
        go.SetActive(true);
        return go;
    }
    // ---- small builders

    TextMeshProUGUI _fitProbe;
    readonly Dictionary<string, string> _fitted = new();

    /// <summary>
    /// Shortens a text with an ellipsis only as far as it really has to be: the longest start of the text that is no wider than
    /// <paramref name="pixels"/>, measured with the same font and size it is drawn with (a guess of a fixed width per character
    /// cut names far too early, leaving empty space).
    /// </summary>
    string Fit(string text, double pixels, int fontSize = 13)
    {
        if (text.Length == 0) return text;
        var key = fontSize + "|" + (int)pixels + "|" + text;
        if (_fitted.TryGetValue(key, out var known)) return known;
        if (_fitProbe == null)
        {
            _fitProbe = UiKit.Text("FitProbe", _window.transform, fontSize, new Color(0f, 0f, 0f, 0f), TextAlignmentOptions.TopLeft); // invisible, but active so the text engine can measure
            _fitProbe.enableWordWrapping = false;
            _fitProbe.overflowMode = TextOverflowModes.Overflow;
            UiKit.Place(_fitProbe.gameObject, 0, 0, 10, 10);
        }

        _fitProbe.fontSize = fontSize;
        double Width(string s) => _fitProbe.GetPreferredValues(s, 100000f, 0f).x;
        string result;
        if (Width(text) <= pixels)
        {
            result = text;
        }
        else
        {
            int low = 0, high = text.Length - 1; // the longest prefix (plus the ellipsis) that fits
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (Width(text.Substring(0, mid) + "…") <= pixels) low = mid;
                else high = mid - 1;
            }

            result = low <= 0 ? "…" : text.Substring(0, low) + "…";
        }

        if (_fitted.Count > 3000) _fitted.Clear();
        _fitted[key] = result;
        return result;
    }
    /// <summary>Blue for a move to another place or a reset by hand, red for a failure, yellow for a clear (section, boss, dungeon) and the rest.</summary>
    static string ReasonColor(string reason) => reason switch
    {
        "장소 이동" or "직접 초기화" => "#6fb4ff",
        "전멸" or "던전 실패" => "#ff6b6b",
        _ => "#e8c27a",
    };

    static string Esc(string text) => text.Replace("<", "<​");

    TextMeshProUGUI Text(Transform parent, string text, double x, double y, double w, double h, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = UiKit.Text("Text", parent, size, color, align);
        UiKit.Place(t.gameObject, x, y, w, h);
        t.text = text;
        return t;
    }

    void Btn(Transform parent, string caption, double x, double y, double w, double h, Color color, Action click)
    {
        var label = UiKit.Button("Btn_" + caption, parent, caption, color, click);
        UiKit.Place(label.transform.parent.gameObject, x, y, w, h);
    }

    void Small(string caption, double x, double y, Action click) => Btn(_body.transform, caption, x, y, 26, 24, Button, click);
}
