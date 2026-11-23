using System;
using Panda.ZUi;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The game builds a chat bubble (with its card button) only for the few lines in view of its own chat list. For a person whose
/// line is further back, the hidden list is moved back step by step, one screenful at a time, until a bubble of that person is
/// built; the card button is pressed and the list goes back to the newest line. The list is invisible, so nothing is seen.
/// </summary>
static class CardSeeker
{
    static string _who = "";
    static ZLoopListView _list;
    static int _total;
    static int _next;
    static int _step;
    static int _wait;
    static float _deadline;

    public static bool Active => _who.Length > 0;

    public static void Start(string who)
    {
        try
        {
            var chat = GameChatUi.FindChat();
            var list = chat == null ? null : GameChatUi.Find(chat, "node_dialogue")?.GetComponent<ZLoopListView>();
            if (list == null)
            {
                return;
            }

            _list = list;
            _total = list.ItemTotalCount;
            _step = Math.Max(1, list.ShownItemCount - 1); // a little overlap between two looks
            _next = _total - 1 - _step;
            _who = who;
            _wait = 0;
            _deadline = Time.realtimeSinceStartup + 6f;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("card seek: " + e.Message);
            _who = "";
        }
    }

    /// <summary>Every frame while a search is on: look, then move one screenful back.</summary>
    public static void Poll()
    {
        if (_who.Length == 0) return;
        try
        {
            if (_list == null || Time.realtimeSinceStartup > _deadline)
            {
                Finish("gave up (time)");
                return;
            }

            if (_wait > 0)
            {
                _wait--;
                return;
            }

            if (GameChatUi.OpenCard(_who, quiet: true))
            {
                Finish("found");
                return;
            }

            if (_next < 0)
            {
                Finish("gave up (the start of the list)");
                return;
            }

            _list.MovePanelToItemIndex(_next, 0f);
            _next -= _step;
            _wait = 3; // the game builds the bubbles of the new place over the next frames
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("card seek: " + e.Message);
            Finish("failed");
        }
    }

    static void Finish(string how)
    {
        _who = "";
        try
        {
            if (_list != null)
            {
                _list.MovePanelToItemIndex(Math.Max(0, _total - 1), 0f); // back to the newest line
                if (_list.ScrollRect != null) _list.ScrollRect.verticalNormalizedPosition = 0f;
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("card seek: could not go back to the newest line: " + e.Message);
        }

        _list = null;
    }
}
