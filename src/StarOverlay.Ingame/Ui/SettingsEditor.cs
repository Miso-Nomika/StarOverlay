using System;
using System.Linq;
using System.Text;
using StarOverlay.Ingame.Protocol;
using TMPro;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The "편집" tab of a plugin's settings window: the list of edited rows (picture, name, field buttons, up / down / delete), the candidates with a "+" button
/// and a number keypad. Everything is done with the mouse (no text input, so game keys are not mixed in). Every press is one <c>edit</c> command;
/// the plugin changes its data and the next snapshot redraws the tab.
/// </summary>
internal sealed partial class SettingsView
{
    const double EditRowHeight = 34;
    string _digits = "";
    int _eScroll;
    int _cScroll;
    string _pickKey = "";   // the row whose picture is being picked (empty: the list is shown)
    string _pickTab = "Modules";
    int _pickScroll;
    static readonly System.Collections.Generic.Dictionary<string, string[]> PictureFiles = new();

    WidgetDto EditorOf(WindowDto dto) => dto?.Widgets.FirstOrDefault(w => w.Kind == WidgetKind.Editor);

    string EditorSignature(WindowDto dto)
    {
        var editor = EditorOf(dto);
        if (editor == null) return "";
        var sb = new StringBuilder();
        sb.Append(GameImages.Version).Append(_pickKey).Append(_pickTab).Append(_pickScroll).Append('|').Append(_digits).Append('|').Append(_eScroll).Append('|').Append(_cScroll).Append('|');
        foreach (var row in editor.EditRows)
        {
            sb.Append(row.Key).Append(':').Append(row.Name).Append(':').Append(row.Image);
            foreach (var field in row.Fields) sb.Append(',').Append(field.Name).Append('=').Append(field.Value);
            sb.Append(';');
        }

        foreach (var candidate in editor.Candidates) sb.Append(candidate.Key).Append(candidate.Detail).Append(';');
        return sb.ToString();
    }

    void Edit(WidgetDto editor, string rowKey, string value) =>
        _root.Send(new CommandDto { Window = _plugin, Action = CommandAction.Edit, Key = editor.Id + ":" + rowKey, Value = value });

    void BuildEditor(WindowDto dto, double bw, double bh)
    {
        var editor = EditorOf(dto);
        if (editor == null)
        {
            Text(_body.transform, "이 플러그인에는 편집할 항목이 없습니다.", 0, 0, bw, 24, 15, new Color(0.62f, 0.68f, 0.76f, 1f));
            return;
        }

        if (_pickKey.Length > 0)
        {
            BuildPicker(editor, bw, bh);
            return;
        }

        var listH = Math.Max(EditRowHeight * 2, Math.Floor(bh * 0.5 / EditRowHeight) * EditRowHeight);
        Text(_body.transform, $"{editor.Title} ({editor.EditRows.Count})", 0, 0, bw - 70, 22, 14, new Color(1f, 0.85f, 0.54f, 1f));
        var top = 24.0;
        var visible = Math.Max(1, (int)((listH - 24) / EditRowHeight));
        _eScroll = Math.Clamp(_eScroll, 0, Math.Max(0, editor.EditRows.Count - visible));
        if (editor.EditRows.Count == 0)
            Text(_body.transform, "추적 중인 항목이 없습니다. 아래에서 추가하세요.", 0, top, bw, 24, 14, new Color(0.62f, 0.68f, 0.76f, 1f));
        if (_eScroll > 0) Small("▲", bw - 56, 0, () => { _eScroll--; Dirty(); });
        if (_eScroll + visible < editor.EditRows.Count) Small("▼", bw - 28, 0, () => { _eScroll++; Dirty(); });

        // the rows sit in a scroll region: the wheel and a drag over them move the list
        var maxRows = Math.Max(0, editor.EditRows.Count - visible);
        var rowRegion = Region("edit-rows", 0, top, bw, visible * EditRowHeight, EditRowHeight, steps =>
        {
            _eScroll = Math.Clamp(_eScroll + steps, 0, maxRows);
            Redraw();
        });
        var ew = editor.EditRows.Count > visible ? bw - 8 : bw;
        var rowTop = 0.0;
        for (var i = _eScroll; i < Math.Min(editor.EditRows.Count, _eScroll + visible); i++)
        {
            EditorRow(rowRegion.transform, editor, editor.EditRows[i], rowTop, ew, i > 0, i < editor.EditRows.Count - 1);
            rowTop += EditRowHeight;
        }

        ScrollBar(rowRegion.transform, bw, 0, visible * EditRowHeight, editor.EditRows.Count, visible, _eScroll);

        var lower = listH + 6;
        var lowerH = bh - lower;
        if (lowerH < 80) return;
        var split = Math.Floor(bw * 0.58);
        Text(_body.transform, "최근 본 버프", 0, lower, split - 40, 22, 14, new Color(1f, 0.85f, 0.54f, 1f));
        Candidates(editor, 0, lower + 24, split - 8, lowerH - 24);
        Keypad(editor, split, lower, bw - split, lowerH);
    }

    const int ShortValue = 6; // the longest value shown on a field button (a longer picture name ends with "…")

    void EditorRow(Transform parent, WidgetDto editor, EditRowDto row, double y, double bw, bool canUp, bool canDown)
    {
        var back = UiKit.Box("Edit_" + row.Key, parent, Row);
        UiKit.Place(back.gameObject, 0, y, bw, EditRowHeight - 2);
        Picture(back.transform, row.Image, 4, 3, EditRowHeight - 8);
        var right = bw - 4;
        double Put(string caption, double width, Color color, Action click)
        {
            right -= width;
            Btn(back.transform, caption, right, 4, width, EditRowHeight - 10, color, click);
            right -= 2;
            return right;
        }

        Put("삭제", 38, ButtonWarn, () => Edit(editor, row.Key, "delete"));
        Put("▼", 26, canDown ? Button : new Color(0.16f, 0.19f, 0.25f, 1f), () => { if (canDown) Edit(editor, row.Key, "down"); });
        Put("▲", 26, canUp ? Button : new Color(0.16f, 0.19f, 0.25f, 1f), () => { if (canUp) Edit(editor, row.Key, "up"); });
        for (var f = row.Fields.Count - 1; f >= 0; f--)
        {
            var field = row.Fields[f];
            var name = field.Name;
            if (field.Name == "image")
            {
                var shown = field.Value.Length > ShortValue ? field.Value.Substring(0, ShortValue - 1) + "…" : field.Value; // a long picture name would make the button wide
                Put(field.Label + " " + shown, 12 + (field.Label.Length + 1 + shown.Length) * 13, Button, () => OpenPicker(row.Key));
            }
            else if (field.Toggle)
            {
                var caption = (field.Value == "on" ? "V " : "") + field.Label;
                Put(caption, 12 + caption.Length * 13, field.Value == "on" ? ButtonOn : Button, () => Edit(editor, row.Key, "field:" + name));
            }
            else
            {
                var caption = field.Label + " " + field.Value;
                Put(caption, 12 + caption.Length * 13, Button, () => Edit(editor, row.Key, "field:" + name));
            }
        }

        Text(back.transform, Esc(row.Name), EditRowHeight, 0, Math.Max(20, right - EditRowHeight), EditRowHeight - 2, 14, Color.white);
    }

    void OpenPicker(string rowKey)
    {
        _pickKey = rowKey;
        _pickScroll = 0;
        PictureFiles.Remove(PictureDirectory("custom")); // the own pictures may have changed
        Dirty();
    }

    string PictureDirectory(string tab) =>
        System.IO.Path.Combine(_root.AssetsRoot, _plugin, "assets", tab == "custom" ? "custom" : System.IO.Path.Combine("library", tab));

    string[] PicturesOf(string tab)
    {
        var dir = PictureDirectory(tab);
        if (PictureFiles.TryGetValue(dir, out var cached)) return cached;
        string[] files;
        try
        {
            // the bundled pictures, and the pictures the game has (their names are in _game.txt next to them)
            var bundled = System.IO.Directory.Exists(dir)
                ? System.IO.Directory.GetFiles(dir, "*.png").Select(System.IO.Path.GetFileName)
                : Enumerable.Empty<string>();
            var list = System.IO.Path.Combine(dir, "_game.txt");
            var fromGame = System.IO.File.Exists(list)
                ? System.IO.File.ReadAllLines(list).Select(line => line.Trim()).Where(line => line.Length > 0).Select(line => line + ".png")
                : Enumerable.Empty<string>();
            files = bundled.Concat(fromGame).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception)
        {
            files = Array.Empty<string>();
        }

        PictureFiles[dir] = files;
        return files;
    }

    /// <summary>The picture picker: no picture (an empty picture), or one of the bundled buff / skill pictures, or one of the user's own.</summary>
    void BuildPicker(WidgetDto editor, double bw, double bh)
    {
        var row = editor.EditRows.FirstOrDefault(r => r.Key == _pickKey);
        Text(_body.transform, "이미지 선택" + (row == null ? "" : " - " + Esc(row.Name)), 0, 0, bw - 390, 26, 14, new Color(1f, 0.85f, 0.54f, 1f));
        var x = bw - 4;
        void Tab(string caption, string tab, double width)
        {
            x -= width;
            Btn(_body.transform, caption, x, 0, width, 26, _pickTab == tab ? ButtonOn : Button, () => { _pickTab = tab; _pickScroll = 0; Dirty(); });
            x -= 3;
        }

        x -= 0;
        Btn(_body.transform, "닫기", x - 46, 0, 46, 26, ButtonWarn, () => { _pickKey = ""; Dirty(); });
        x -= 49;
        Tab("내 그림", "custom", 62);
        Tab("스킬", "Skills", 46);
        Tab("이매진", "Imagines", 56);
        Tab("모듈", "Modules", 46);
        Btn(_body.transform, "그림 없음", x - 130, 0, 130, 26, Button, () =>
        {
            Edit(editor, _pickKey, "set:image=none");
            _pickKey = "";
            Dirty();
        });

        const double cell = 54;
        var files = PicturesOf(_pickTab);
        var cols = Math.Max(1, (int)((bw - 12) / cell)); // leaves a strip at the right edge for the bar
        var gridH = bh - 32;
        var visibleRows = Math.Max(1, (int)(gridH / cell));
        var totalRows = (files.Length + cols - 1) / cols;
        var maxScroll = Math.Max(0, totalRows - visibleRows);
        _pickScroll = Math.Clamp(_pickScroll, 0, maxScroll);
        var region = Region("picker", 0, 32, bw, gridH, cell, steps =>
        {
            _pickScroll = Math.Clamp(_pickScroll + steps, 0, maxScroll);
            Redraw();
        });
        if (files.Length == 0)
        {
            Text(region.transform, _pickTab == "custom" ? "플러그인 폴더의 assets\\custom 폴더에 PNG를 넣으면 여기에 나옵니다." : "그림이 없습니다.", 6, 6, bw - 12, 24, 13, new Color(0.62f, 0.68f, 0.76f, 1f));
            return;
        }

        var current = row?.Fields.FirstOrDefault(f => f.Name == "image")?.Value ?? "";
        for (var i = _pickScroll * cols; i < Math.Min(files.Length, (_pickScroll + visibleRows) * cols); i++)
        {
            var file = files[i];
            var at = i - _pickScroll * cols;
            var path = System.IO.Path.Combine(PictureDirectory(_pickTab), file);
            var selected = System.IO.Path.GetFileNameWithoutExtension(file) == current || file == current;
            var label = UiKit.Button("Pic_" + i, region.transform, "", selected ? ButtonOn : new Color(1f, 1f, 1f, 0.08f), () =>
            {
                Edit(editor, _pickKey, "set:image=" + (_pickTab == "custom" ? "file:" : "lib:" + _pickTab + "/") + file);
                _pickKey = "";
                Dirty();
            });
            var holder = label.transform.parent.gameObject;
            UiKit.Place(holder, at % cols * cell + 2, at / cols * cell + 2, cell - 4, cell - 4);
            var picture = UiKit.Box("Thumb", holder.transform, Color.white);
            picture.sprite = UiKit.LibraryPicture(path);
            picture.preserveAspect = true;
            UiKit.Stretch(picture.gameObject, 3, 3, 3, 3);
        }

        ScrollBar(region.transform, bw, 0, gridH, totalRows, visibleRows, _pickScroll);
        if (totalRows > visibleRows)
            Text(_body.transform, $"{_pickScroll + 1}-{Math.Min(totalRows, _pickScroll + visibleRows)} / {totalRows}줄 (휠로 넘기기)", bw - 260, bh - 16, 256, 16, 11, new Color(0.62f, 0.68f, 0.76f, 1f), TextAlignmentOptions.MidlineRight);
    }

    void Picture(Transform parent, string path, double x, double y, double size)
    {
        var image = UiKit.Box("Picture", parent, Color.white);
        image.sprite = UiKit.Picture(path);
        image.preserveAspect = true;
        if (image.sprite == null) image.color = new Color(1f, 1f, 1f, 0.15f);
        UiKit.Place(image.gameObject, x, y, size, size);
    }

    void Candidates(WidgetDto editor, double x, double y, double w, double h)
    {
        const double rowH = 30;
        var visible = Math.Max(1, (int)(h / rowH));
        _cScroll = Math.Clamp(_cScroll, 0, Math.Max(0, editor.Candidates.Count - visible));
        if (editor.Candidates.Count == 0)
            Text(_body.transform, "최근 5분 안에 걸린 내 버프가 없습니다.", x, y, w, 24, 13, new Color(0.62f, 0.68f, 0.76f, 1f));
        if (_cScroll > 0) Small("▲", x + w - 56, y - 24, () => { _cScroll--; Dirty(); });
        if (_cScroll + visible < editor.Candidates.Count) Small("▼", x + w - 28, y - 24, () => { _cScroll++; Dirty(); });
        var maxCandidates = Math.Max(0, editor.Candidates.Count - visible);
        var region = Region("edit-candidates", x, y, w, visible * rowH, rowH, steps =>
        {
            _cScroll = Math.Clamp(_cScroll + steps, 0, maxCandidates);
            Redraw();
        });
        var cw = editor.Candidates.Count > visible ? w - 8 : w;
        for (var i = _cScroll; i < Math.Min(editor.Candidates.Count, _cScroll + visible); i++)
        {
            var candidate = editor.Candidates[i];
            var row = UiKit.Box("Cand_" + candidate.Key, region.transform, Row);
            UiKit.Place(row.gameObject, 0, (i - _cScroll) * rowH, cw, rowH - 2);
            Picture(row.transform, candidate.Image, 3, 2, rowH - 6);
            Btn(row.transform, "+", cw - 30, 2, 28, rowH - 6, ButtonOn, () => Edit(editor, candidate.Key, "add"));
            Text(row.transform, Esc($"{candidate.Name} ({candidate.Key})"), rowH, 0, cw - rowH - 34, rowH - 2, 13, Color.white);
            if (candidate.Detail.Length > 0)
                Text(row.transform, Esc(candidate.Detail), cw * 0.5, 0, cw * 0.5 - 34, rowH - 2, 12, new Color(0.55f, 0.6f, 0.68f, 1f), TextAlignmentOptions.MidlineRight);
        }

        ScrollBar(region.transform, w, 0, visible * rowH, editor.Candidates.Count, visible, _cScroll);
    }

    void Keypad(WidgetDto editor, double x, double y, double w, double h)
    {
        Text(_body.transform, "UID 입력", x, y, w, 22, 14, new Color(1f, 0.85f, 0.54f, 1f));
        var display = UiKit.Box("Digits", _body.transform, new Color(0.04f, 0.05f, 0.07f, 1f));
        UiKit.Place(display.gameObject, x, y + 24, w, 30);
        Text(display.transform, _digits.Length == 0 ? "번호를 누르세요" : _digits, 8, 0, w - 16, 30, 17, _digits.Length == 0 ? new Color(0.5f, 0.55f, 0.62f, 1f) : Color.white);
        var cellW = (w - 6) / 3;
        var cellH = Math.Clamp((h - 62) / 5, 22, 34);
        var top = y + 60;
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "←", "0", "추가" };
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            var color = key == "추가" ? ButtonOn : Button;
            Btn(_body.transform, key, x + i % 3 * (cellW + 3), top + i / 3 * (cellH + 3), cellW, cellH, color, () => Press(editor, key));
        }
    }

    void Press(WidgetDto editor, string key)
    {
        if (key == "←") _digits = _digits.Length > 0 ? _digits.Substring(0, _digits.Length - 1) : "";
        else if (key == "추가")
        {
            if (_digits.Length == 0) return;
            Edit(editor, _digits, "add");
            _digits = "";
        }
        else if (_digits.Length < 10) _digits += key;
        Dirty();
    }
}
