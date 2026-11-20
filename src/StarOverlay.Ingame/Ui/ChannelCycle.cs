using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// While typing in the game's chat input, the Tab key moves the sending channel one step on (world, local, guild, party ... and
/// around). It opens the game's own channel list, takes the entry after the current one and presses it, then puts the cursor back
/// into the input. Only works while our chat window is in use.
/// </summary>
static class ChannelCycle
{
    static readonly Regex Tags = new(@"<[^>]+>|[\[\]\s]", RegexOptions.Compiled);
    static TMP_InputField _input;
    static bool _waiting;
    static float _deadline;
    static bool _keyBroken;

    public static void Poll(Transform chat)
    {
        try
        {
            RemoveTab(chat);
            if (_waiting)
            {
                if (Time.realtimeSinceStartup > _deadline)
                {
                    _waiting = false;
                    return;
                }

                if (Step(chat, open: false)) _waiting = false;
                return;
            }

            if (_keyBroken || !TabPressed()) return;
            var input = InputField(chat);
            if (input == null || !input.isFocused) return; // only while typing
            if (Step(chat, open: true)) return;
            _waiting = true; // the list was opened: wait until its entries exist
            _deadline = Time.realtimeSinceStartup + 1f;
        }
        catch (Exception)
        {
            _waiting = false; // a failed step must never reach the game
        }
    }

    static bool TabPressed()
    {
        try
        {
            return Input.GetKeyDown(KeyCode.Tab);
        }
        catch (Exception e)
        {
            _keyBroken = true;
            Plugin.L.LogWarning("channel cycle: the Tab key cannot be read here: " + e.Message);
            return false;
        }
    }

    static TMP_InputField InputField(Transform chat)
    {
        if (_input == null) _input = GameChatUi.Find(chat, "input_field")?.GetComponent<TMP_InputField>();
        return _input;
    }

    /// <summary>The Tab key also types a tab character into the input; it is taken out again at once, so Tab only switches the channel.</summary>
    static void RemoveTab(Transform chat)
    {
        var input = InputField(chat);
        if (input == null) return;
        var text = input.text;
        if (string.IsNullOrEmpty(text) || text.IndexOf('	') < 0) return;
        var caret = input.caretPosition;
        var cleaned = text.Replace("	", "");
        input.text = cleaned;
        input.caretPosition = Math.Clamp(caret - (text.Length - cleaned.Length), 0, cleaned.Length);
    }

    /// <summary>Presses the entry after the current channel. When the list is not open yet and <paramref name="open"/> is set, opens it and returns false.</summary>
    static bool Step(Transform chat, bool open)
    {
        var content = GameChatUi.Find(GameChatUi.Find(chat, "node_channel"), "node_content");
        var entries = new List<Transform>();
        if (content != null)
            for (var i = 0; i < content.childCount; i++)
                if (content.GetChild(i).gameObject.activeSelf && content.GetChild(i).name.StartsWith("main_channel_tpl", StringComparison.Ordinal)) entries.Add(content.GetChild(i));

        if (entries.Count == 0)
        {
            if (!open) return false;
            var button = GameChatUi.Find(chat, "btn_channel");
            if (button == null)
            {
                return true;
            }

            Press(button.gameObject);
            return false;
        }

        entries.Sort((a, b) => b.position.y.CompareTo(a.position.y)); // top to bottom, as the list shows them
        var names = new List<string>();
        foreach (var entry in entries) names.Add(NameOf(entry));
        var current = Clean(GameChatUi.Find(GameChatUi.Find(chat, "img_channel"), "lab_channel")?.GetComponent<TMP_Text>()?.text);

        // the order of the cycle: world, local, guild, party, then whatever else the list has (the list itself is in another order)
        var order = new List<int>();
        for (var i = 0; i < entries.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            var byRank = Rank(names[a]).CompareTo(Rank(names[b]));
            return byRank != 0 ? byRank : a.CompareTo(b);
        });
        var position = order.FindIndex(i => names[i] == current);
        var next = order[(position + 1) % order.Count]; // unknown current: starts with the first of the cycle
        Press(entries[next].gameObject);
        InputField(chat)?.ActivateInputField(); // the cursor goes back into the input
        return true;
    }

    static int Rank(string name) => name switch
    {
        "월드" => 0,
        "로컬" or "필드" => 1,
        "길드" => 2,
        "파티" or "팀" => 3,
        _ => 10,
    };

    static string NameOf(Transform entry) => Clean(GameChatUi.Find(entry, "lab_name")?.GetComponent<TMP_Text>()?.text);

    static string Clean(string text) => Tags.Replace(text ?? "", "");

    static void Press(GameObject target)
    {
        var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        UnityEngine.EventSystems.ExecuteEvents.Execute(target, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }
}
