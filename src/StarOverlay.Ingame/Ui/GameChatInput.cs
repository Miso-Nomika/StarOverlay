using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// While our chat window is shown: the game's own chat list, bubbles and channel buttons are made invisible, its input bar (channel,
/// input field, cooldown) is placed right under our window, and its channel tab stays on "전체". Typing and sending are still done by
/// the game itself. When our window goes away everything is put back. Nothing is changed in the game's data, only where and whether
/// its pieces are drawn.
/// </summary>
static class GameChatInput
{
    static readonly string[] BarParts = { "img_channel", "input_field", "lab_cd", "node_channel" };
    // the parts that decide where the bar is: the channel pop-up list (node_channel) opens upward and is far taller than the bar,
    // so it moves with the bar but is left out of its measured box
    static readonly string[] BoxParts = { "img_channel", "input_field" };
    static readonly Dictionary<Transform, Vector3> _moved = new();
    static readonly List<CanvasGroup> _hidden = new();
    static bool _listHidden;
    static bool _barMoved;
    static int _logged;

    /// <summary>Where our chat window is (set by the chat panel); the game's input bar goes right under its lower edge.</summary>
    internal static RectTransform Anchor;

    /// <summary>The world chat channel (0 = not known); shown as a badge at the right end of the input bar.</summary>
    internal static int WorldNumber;

    const float BadgeWidth = 66f; // in reference pixels
    static GameObject _badge;
    static Image _badgeBack;
    static TextMeshProUGUI _badgeText;

    /// <summary>
    /// Our chat window is on, not counting the switch of the whole canvas (that one only follows whether the game shows its chat bar):
    /// the game's own chat stays hidden and its bar stays in place the whole time our window is switched on.
    /// </summary>
    static bool Shown(RectTransform part)
    {
        for (var node = part.transform; node != null && node.parent != null; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }

    public static void Poll()
    {
        try
        {
            // only while our chat window is really there: when it is switched off (the plugin is turned off in the system window,
            // or the window is hidden) the game's own chat comes back at once
            var ours = Anchor != null && Shown(Anchor);
            var wantHide = ours;
            var wantInput = ours;
            if (!wantHide && _listHidden)
            {
                ShowList();
                _listHidden = false;
            }

            if (!wantInput && _barMoved)
            {
                RestoreBar();
                _barMoved = false;
            }

            if (!wantHide && !wantInput) return;
            var chat = GameChatUi.FindChat();
            if (chat == null) return;
            if (wantHide)
            {
                HideList(chat);
                _listHidden = true;
                KeepAllTab(chat);
            }

            if (wantInput)
            {
                PlaceBar(chat);
                ChannelCycle.Poll(chat);
            }
        }
        catch (Exception e)
        {
            if (_logged++ < 5) Plugin.L.LogWarning("game chat input: " + e.Message);
        }
    }

    // ---- the message list

    static void HideList(Transform chat)
    {
        Hide(GameChatUi.Find(chat, "node_dialogue"));
        Hide(GameChatUi.Find(chat, "node_left_btn")); // the channel buttons on the left
        var root = chat.parent; // the always-on chat (bubbles and the short list) sits next to the full chat
        if (root == null) return;
        for (var i = 0; i < root.childCount; i++)
        {
            var other = root.GetChild(i);
            if (!other.name.StartsWith("main_chat_tpl_pc", StringComparison.Ordinal)) continue;
            Hide(GameChatUi.Find(other, "node_bullet_chat"));
            Hide(GameChatUi.Find(other, "anim_chat_content"));
        }
    }

    static void Hide(Transform node)
    {
        if (node == null) return;
        var group = node.GetComponent<CanvasGroup>();
        if (group == null) group = node.gameObject.AddComponent<CanvasGroup>(); // some nodes (the left channel buttons) have none
        if (!_hidden.Contains(group)) _hidden.Add(group);
        group.alpha = 0f; // the game's animations may set it back, so it is set every frame
        group.blocksRaycasts = false;
    }

    static void ShowList()
    {
        foreach (var group in _hidden)
        {
            if (group == null) continue;
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }

        _hidden.Clear();
    }

    // ---- the channel tab of the game's chat: always "전체"

    static float _nextTab;
    static int _tabClicks;

    /// <summary>
    /// The game shows only the chosen channel's lines (and the pictures in them); the up/down keys change that channel. While our
    /// chat window is used the first tab ("전체") is kept selected, so every channel's lines and pictures are drawn by the game.
    /// </summary>
    static void KeepAllTab(Transform chat)
    {
        if (Time.realtimeSinceStartup < _nextTab) return;
        _nextTab = Time.realtimeSinceStartup + 0.5f;
        var content = GameChatUi.Find(GameChatUi.Find(chat, "loop_channel"), "content");
        if (content == null) return;
        var tabs = new List<(Transform Node, float Y)>();
        for (var i = 0; i < content.childCount; i++)
        {
            var node = content.GetChild(i);
            if (node.gameObject.activeSelf && node.name.StartsWith("main_chat_channel_tpl_pc", StringComparison.Ordinal)) tabs.Add((node, node.position.y));
        }

        if (tabs.Count == 0) return;
        tabs.Sort((a, b) => b.Y.CompareTo(a.Y)); // the top one is "전체"
        var selected = -1;
        for (var i = 0; i < tabs.Count; i++)
        {
            var on = GameChatUi.Find(tabs[i].Node, "group_on");
            var group = on == null ? null : on.GetComponent<CanvasGroup>();
            var shown = on != null && on.gameObject.activeInHierarchy && (group == null || group.alpha > 0.5f);
            if (shown && selected < 0) selected = i;
        }

        if (selected == 0)
        {
            _tabClicks = 0; // it worked: the safety below counts clicks that did not
            return;
        }

        if (_tabClicks++ >= 40) return; // never loop forever if the click does not work
        var first = tabs[0].Node.gameObject;
        var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        UnityEngine.EventSystems.ExecuteEvents.Execute(first, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }

    // ---- the input bar

    /// <summary>The box of a rect on the screen in pixels. (GetWorldCorners does not hand its result back on the game's IL2CPP objects, so the corners are computed.)</summary>
    static (float Left, float Bottom, float Right, float Top) ScreenBox(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        var root = canvas == null ? null : canvas.rootCanvas;
        var camera = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        var box = rect.rect;
        var low = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(box.xMin, box.yMin, 0f)));
        var high = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(box.xMax, box.yMax, 0f)));
        return (Math.Min(low.x, high.x), Math.Min(low.y, high.y), Math.Max(low.x, high.x), Math.Max(low.y, high.y));
    }

    /// <summary>The window frame around a panel part (our windows are named "Window_..."), or the part itself.</summary>
    static RectTransform WindowOf(RectTransform part)
    {
        for (var up = part.parent; up != null; up = up.parent)
            if (up.name.StartsWith("Window_", StringComparison.Ordinal)) return up.GetComponent<RectTransform>() ?? part;
        return part;
    }

    static readonly Dictionary<RectTransform, Vector2> _sizes = new();

    /// <summary>
    /// The grey bar (img_channel) is as wide as our window and the input field fills what is left of it after the channel label.
    /// </summary>
    static void FitWidth(List<RectTransform> parts, (float Left, float Bottom, float Right, float Top) ours, float badgePixels, float insetPixels)
    {
        RectTransform back = null, input = null;
        foreach (var part in parts)
        {
            if (part.name == "img_channel") back = part;
            else if (part.name == "input_field") input = part;
        }

        if (input == null || back == null) return;
        var backBox = ScreenBox(back);
        var inputBox = ScreenBox(input);
        var wantBack = Math.Max(160f, ours.Right - ours.Left - insetPixels); // a little shorter on the left; the right end stays with our window
        var haveBack = backBox.Right - backBox.Left;
        Resize(back, haveBack, wantBack);
        var offset = inputBox.Left - backBox.Left; // the channel label in front of the field
        var wantInput = Math.Max(60f, wantBack - offset - 4f - badgePixels - 4f); // the badge takes the right end
        Resize(input, inputBox.Right - inputBox.Left, wantInput);
    }

    /// <summary>Sets the width of a rect (measured in screen pixels) by changing its size, remembering the game's own size.</summary>
    static void Resize(RectTransform rect, float havePx, float wantPx)
    {
        if (havePx <= 1f || Math.Abs(wantPx - havePx) < 1f) return;
        var perPixel = rect.rect.width / havePx; // the game canvas's units per screen pixel
        if (!_sizes.ContainsKey(rect)) _sizes[rect] = rect.sizeDelta;
        var size = rect.sizeDelta;
        size.x += (wantPx - havePx) * perPixel;
        rect.sizeDelta = size;
    }

    /// <summary>"월드 3": a bright badge at the right end of the grey input bar, so the world chat channel can be read at a glance.</summary>
    static void ShowBadge(List<RectTransform> parts, (float Left, float Bottom, float Right, float Top) ours, float scale, RectTransform anchor)
    {
        RectTransform back = null;
        foreach (var part in parts)
            if (part.name == "img_channel") back = part;
        if (back == null) return;
        if (_badge == null)
        {
            var root = anchor.GetComponentInParent<Canvas>().rootCanvas.transform;
            _badge = UiKit.Node("WorldBadge", root);
            _badgeBack = UiKit.Box("Back", _badge.transform, Color.white);
            UiKit.Stretch(_badgeBack.gameObject);
            _badgeText = UiKit.Text("Text", _badge.transform, 18, Color.white, TextAlignmentOptions.Center);
            _badgeText.enableWordWrapping = false;
            UiKit.Stretch(_badgeText.gameObject);
        }

        _badge.SetActive(true);
        _badge.transform.SetAsLastSibling();
        var known = WorldNumber > 0;
        // small and quiet: a dark chip with soft orange letters (grey while the number is not known)
        _badgeBack.color = new Color(0.17f, 0.15f, 0.12f, 0.92f);
        _badgeText.color = known ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.62f, 0.64f, 0.7f, 1f);
        UiKit.SetText(_badgeText, known ? $"월드 ch{WorldNumber}" : "월드 ch?");
        var box = ScreenBox(back);
        var heightRef = Math.Max(16f, (box.Top - box.Bottom) / scale - 2f);
        _badgeText.fontSize = Math.Clamp(heightRef * 0.6f, 10f, 15f);
        var barTop = ours.Bottom - Gap; // where the bar's top edge is
        var x = (ours.Right - 3f * scale - BadgeWidth * scale) / scale;
        var y = (Screen.height - (barTop - 1f * scale)) / scale;
        UiKit.Place(_badge, x, y, BadgeWidth, heightRef);
    }

    const float Gap = 4f; // a few pixels of air between our window and the bar
    const float SideInset = 10f; // the bar is this much shorter on the left (reference pixels)

    static void PlaceBar(Transform chat)
    {
        var anchor = Anchor;
        if (anchor == null || !Shown(anchor)) return;
        var parts = new List<RectTransform>();
        foreach (var name in BarParts)
        {
            var node = GameChatUi.Find(chat, name);
            var rect = node == null ? null : node.GetComponent<RectTransform>(); // "is RectTransform" does not work on the game's IL2CPP objects
            if (rect != null && node.gameObject.activeInHierarchy) parts.Add(rect);
        }

        if (parts.Count == 0) return;
        var canvas = chat.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var canvasRect = canvas.GetComponent<RectTransform>();

        // our window: the bar is as wide as it, and sits right under it
        var ours = ScreenBox(WindowOf(anchor)); // the whole window (its lower edge), not just the chat area inside it
        var scale = Math.Max(0.1f, anchor.GetComponentInParent<Canvas>().rootCanvas.scaleFactor);
        FitWidth(parts, ours, BadgeWidth * scale, SideInset * scale);

        // the bar's current box on the screen (the grey bar and the input field; the channel pop-up list is left out)
        float left = float.MaxValue, bottom = float.MaxValue, top = float.MinValue;
        foreach (var part in parts)
        {
            if (Array.IndexOf(BoxParts, part.name) < 0) continue;
            var box = ScreenBox(part);
            left = Math.Min(left, box.Left);
            bottom = Math.Min(bottom, box.Bottom);
            top = Math.Max(top, box.Top);
        }

        // the bar's top-left goes to our window's lower left corner, a few pixels below it
        var dx = ours.Left + SideInset * scale - left; // the bar starts a little to the right of our window's left edge
        var dy = ours.Bottom - Gap - top;
        if (Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f) { _barMoved = true; ShowBadge(parts, ours, scale, anchor); return; }

        RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, new Vector2(left, top), camera, out var worldFrom);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(canvasRect, new Vector2(left + dx, top + dy), camera, out var worldTo);
        var delta = worldTo - worldFrom;
        foreach (var part in parts)
        {
            if (!_moved.ContainsKey(part)) _moved[part] = part.position; // where the game had it, to put it back
            part.position += delta;
        }

        ShowBadge(parts, ours, scale, anchor);

        _barMoved = true;
    }

    static void RestoreBar()
    {
        if (_badge != null) _badge.SetActive(false);
        foreach (var pair in _moved)
            if (pair.Key != null) pair.Key.position = pair.Value;
        _moved.Clear();
        foreach (var pair in _sizes)
            if (pair.Key != null) pair.Key.sizeDelta = pair.Value;
        _sizes.Clear();
    }
}
