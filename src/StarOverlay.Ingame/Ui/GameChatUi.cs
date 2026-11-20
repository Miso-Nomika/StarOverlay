using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>Finding the game's own chat window and our newest chat lines; shared by the game-chat helpers.</summary>
static class GameChatUi
{
    static Transform _chat;
    static Transform _lastChat; // the last chat window found, kept while it is switched off

    /// <summary>The newest lines the chat window received (name, original, translation).</summary>
    internal static List<ChatLineDto> Lines;

    static Transform _bar;

    /// <summary>
    /// Whether the game shows its chat input bar now: its chat window exists and is switched on, the bar is on, and nothing above it
    /// has faded it out. During loading, with the inventory open and so on the game hides its main screen (and the chat with it),
    /// which is when our windows should not be drawn either.
    /// </summary>
    internal static bool InputBarVisible()
    {
        try
        {
            if (LoadingScreenOn()) return false;
            var chat = FindChat();
            if (chat == null)
            {
                _bar = null;
                return false;
            }

            if (_bar == null || !_bar.gameObject.activeInHierarchy) _bar = Find(chat, "img_channel");
            if (_bar == null || !_bar.gameObject.activeInHierarchy) return false;
            var alpha = 1f;
            foreach (var group in _bar.GetComponentsInParent<CanvasGroup>())
            {
                if (group == null) continue;
                alpha *= group.alpha;
                if (group.ignoreParentGroups) break;
            }

            if (alpha < 0.1f) return false;
            // a parent canvas switched off or squeezed to nothing hides the bar without deactivating it
            for (var node = _bar; node != null; node = node.parent)
            {
                var canvas = node.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled) return false;
                var s = node.localScale;
                if (Math.Abs(s.x) < 0.001f || Math.Abs(s.y) < 0.001f) return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static Transform _hint;
    static bool _hintFound; // the key hint line was found at least once in this session
    static float _nextHintSearch;

    /// <summary>
    /// Whether the game shows its main screen now. The main screen has a line of key hints at the bottom ("[I] 파티  [5] 내비게이션 길게 누르기"):
    /// our windows are shown exactly while that line is shown (the chat input bar fades out after a while, which is no reason to hide them).
    /// Until that line was found once (another game version, another language) the chat input bar decides, as before.
    /// </summary>
    static string _hintSuffix; // the last path segments of the hint label, once it was found: later searches use the place, not the words

    internal static bool MainScreenShown()
    {
        string reason;
        try
        {
            if (LoadingScreenOn()) reason = "loading screen";
            else
            {
                var hint = FindHint();
                // shown while either is on screen: the key hint line at the bottom or the chat input bar (the bar fades out after a while, the hint line
                // is gone in some screens; the windows stay as long as one of them is there)
                var why = hint == null ? (_hintFound ? "not found" : "none") : WhyHidden(hint);
                reason = why == null || InputBarVisible() ? "" : "hint " + why + ", no chat bar";
            }
        }
        catch (Exception e)
        {
            reason = "error " + e.Message;
        }

        return reason.Length == 0;
    }

    /// <summary>
    /// The label of the key hint line. It is first found by its words ("내비게이션" and "길게"), later by its place in the screen tree.
    /// The game builds the main screen again after a full-screen window (inventory) and the old one may stay switched off, so a label
    /// that is not on screen is no reason to stay with it: every second the active labels are searched again and one on screen wins.
    /// </summary>
    static Transform FindHint()
    {
        if (_hint != null && WhyHidden(_hint) == null) return _hint; // the usual case: a cheap check
        if (Time.realtimeSinceStartup < _nextHintSearch) return _hint;
        _nextHintSearch = Time.realtimeSinceStartup + (_hint == null ? 0.25f : 1f);
        Transform first = null;
        foreach (var text in UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>())
        {
            if (text == null || text.transform.root.name.StartsWith("StarOverlay", StringComparison.Ordinal)) continue;
            // by its place once that is known, or by its words: the dungeon and boss screens may build the line under another name
            if (text.name != "lab_key") continue; // the line's label is always called lab_key: the words are only read on those (reading every label's text of the game allocates a lot)
            var byPlace = _hintSuffix != null && PathOf(text.transform).EndsWith(_hintSuffix, StringComparison.Ordinal);
            if (!byPlace)
            {
                var content = text.text;
                if (string.IsNullOrEmpty(content) || !content.Contains("내비게이션") || !content.Contains("길게")) continue;
            }

            first ??= text.transform;
            if (WhyHidden(text.transform) != null) continue;
            first = text.transform;
            break;
        }

        if (first != null && first != _hint)
        {
            _hint = first;
            _hintFound = true;
            var path = PathOf(first).Split('/');
            _hintSuffix ??= string.Join("/", path, Math.Max(0, path.Length - 3), Math.Min(3, path.Length));
        }

        return _hint;
    }

    static string PathOf(Transform node)
    {
        var path = node.name;
        for (var parent = node.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    /// <summary>Why a screen element is not on screen (switched off, faded out by a group, a canvas switched off, squeezed to nothing); null when it is.</summary>
    static string WhyHidden(Transform part)
    {
        if (!part.gameObject.activeInHierarchy) return "inactive";
        var alpha = 1f;
        foreach (var group in part.GetComponentsInParent<CanvasGroup>())
        {
            if (group == null) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }

        if (alpha < 0.1f) return "alpha " + alpha.ToString("0.00");
        for (var node = part; node != null; node = node.parent)
        {
            var canvas = node.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) return "canvas off at " + node.name;
            var s = node.localScale;
            if (Math.Abs(s.x) < 0.001f || Math.Abs(s.y) < 0.001f) return "scale 0 at " + node.name;
        }

        return null;
    }

    static Transform _tipLayer;
    static float _nextLayerSearch;

    /// <summary>
    /// Whether the game's loading screen ("loading_window_pc", drawn in the UILayerSystemTip layer) is on. The game builds its chat
    /// again at about 75 percent of a channel change or map move while that screen is still up, so the chat alone is not enough.
    /// </summary>
    static bool LoadingScreenOn()
    {
        if (_tipLayer == null)
        {
            if (Time.realtimeSinceStartup < _nextLayerSearch) return false;
            _nextLayerSearch = Time.realtimeSinceStartup + 0.5f;
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isRootCanvas || canvas.name != "zuiroot") continue;
                _tipLayer = Find(canvas.transform, "UILayerSystemTip");
                break;
            }

            if (_tipLayer == null) return false;
        }

        for (var i = 0; i < _tipLayer.childCount; i++)
        {
            var child = _tipLayer.GetChild(i);
            if (child.gameObject.activeSelf && child.name.StartsWith("loading_window", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    static float _nextSearch;

    /// <summary>The game's chat window (the active "main_chat_pc" object), or null.</summary>
    internal static Transform FindChat()
    {
        if (_chat != null && _chat.gameObject.activeInHierarchy) return _chat; // the usual case: a cheap check
        // the game only switches its chat off while a screen covers it: when it is switched on again, it is found at once
        if (_lastChat != null && _lastChat.gameObject.activeInHierarchy)
        {
            _chat = _lastChat;
            return _chat;
        }

        _chat = null;
        // the whole search walks every screen element of the game, so while the chat is not there (loading, inventory) it is done
        // twice a second at most
        if (Time.realtimeSinceStartup < _nextSearch) return null;
        _nextSearch = Time.realtimeSinceStartup + 0.5f;
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (canvas == null || !canvas.isRootCanvas || canvas.name.StartsWith("StarOverlay", StringComparison.Ordinal)) continue;
            var found = FindActiveByPrefix(canvas.transform, "main_chat_pc", 0);
            if (found != null)
            {
                _chat = found;
                _lastChat = found;
                return found;
            }
        }

        return null;
    }

    static Transform FindActiveByPrefix(Transform node, string prefix, int depth)
    {
        if (node == null || depth > 8) return null;
        for (var i = 0; i < node.childCount; i++)
        {
            var child = node.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            if (child.name.StartsWith(prefix, StringComparison.Ordinal)) return child;
            var deeper = FindActiveByPrefix(child, prefix, depth + 1);
            if (deeper != null) return deeper;
        }

        return null;
    }

    static Transform _worldBox;
    static float _nextWorldSearch;

    /// <summary>
    /// The world chat channel number (the "1" of "월드 1") from the game's big chat window, or 0 while that window is not open
    /// (the number is only drawn there).
    /// </summary>
    internal static int WorldChannelNumber()
    {
        try
        {
            if (_worldBox == null || !_worldBox.gameObject.activeInHierarchy)
            {
                _worldBox = null;
                if (Time.realtimeSinceStartup < _nextWorldSearch) return 0;
                _nextWorldSearch = Time.realtimeSinceStartup + 2f;
                foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (canvas == null || !canvas.isRootCanvas || canvas.name.StartsWith("StarOverlay", StringComparison.Ordinal)) continue;
                    var found = Find(canvas.transform, "node_keyboard_channel");
                    if (found != null)
                    {
                        _worldBox = found;
                        break;
                    }
                }
            }

            var shown = _worldBox == null ? null : Find(Find(_worldBox, "input_channel"), "lab_2");
            var text = shown == null ? null : shown.GetComponent<TMPro.TMP_Text>();
            if (text == null) return 0;
            var digits = System.Text.RegularExpressions.Regex.Replace(text.text ?? "", @"[^0-9]", "");
            return int.TryParse(digits, out var number) && number > 0 ? number : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Opens the game's card (info window) of the character called <paramref name="who"/>: the game's chat list draws a bubble per
    /// recent line with a card button, and that button is pressed. Only people with a recent line in that list can be opened.
    /// </summary>
    internal static bool OpenCard(string who, bool quiet = false)
    {
        try
        {
            var chat = FindChat();
            var content = chat == null ? null : Find(Find(chat, "node_dialogue"), "node_content");
            if (content == null)
            {
                return false;
            }

            var want = Squash(who);
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                var bubble = content.GetChild(i);
                if (!bubble.gameObject.activeSelf || !bubble.name.StartsWith("main_bubble_other", StringComparison.Ordinal)) continue;
                var name = Find(bubble, "lab_name")?.GetComponent<TMPro.TMP_Text>();
                if (name == null || Squash(System.Text.RegularExpressions.Regex.Replace(name.text ?? "", "<[^>]+>", "")) != want) continue;
                var button = Find(bubble, "btn_card");
                if (button == null) continue;
                var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                UnityEngine.EventSystems.ExecuteEvents.Execute(button.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                return true;
            }

            if (!quiet)
            {
                CardSeeker.Start(who);
            }

            return false;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("open card: " + e.Message);
            return false;
        }
    }

    static string Squash(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\s+", "");

    /// <summary>The first descendant with this exact name, or null.</summary>
    // What was found under a node by name. The same few nodes are looked up many times a second (every frame while our chat window is up);
    // each search walks the whole subtree with one interop call per node, which is what made the game stutter. A found node is kept until
    // it is destroyed; a miss is kept for a second.
    static readonly Dictionary<(int, string), (Transform Node, float Until)> Found = new();

    internal static Transform Find(Transform node, string name)
    {
        if (node == null) return null;
        var key = (node.GetInstanceID(), name);
        var now = Time.realtimeSinceStartup;
        if (Found.TryGetValue(key, out var hit))
        {
            if (hit.Node != null) return hit.Node;
            if (now < hit.Until) return null;
        }

        var result = FindDeep(node, name);
        if (Found.Count > 600) Found.Clear();
        Found[key] = (result, result == null ? now + 1f : 0f);
        return result;
    }

    static Transform FindDeep(Transform node, string name)
    {
        for (var i = 0; i < node.childCount; i++)
        {
            var child = node.GetChild(i);
            if (child.name == name) return child;
            var deeper = FindDeep(child, name);
            if (deeper != null) return deeper;
        }

        return null;
    }
}
