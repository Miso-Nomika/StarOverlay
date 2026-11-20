using System;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The game's own buff time list at the lower left (the blue bars with a buff icon, its name and its time). While the buff tracker runs it
/// shows the same buffs in our icons, so the game's list is made invisible; when the tracker is off it comes back at once.
/// It is hidden by squeezing its node to scale 0 (the game switches the node on and off itself, and does not touch its scale).
/// </summary>
static class GameBuffBar
{
    // where the list is in the game's screen tree (measured in the game: node_bufftime_list holds the bars, buffNotice80 are the rows)
    const string ListPath = "UILayerMain/main_main_pc(Clone)/anim/node_main/node_fight_canvas_node/group_lower_left/anim_fighter/node_fighter/battle_main_node_window_pc(Clone)/anim_battle/node_bufftime_list";

    static bool _hide;
    static Transform _screenRoot;
    static Transform _node;
    static Vector3 _original = Vector3.one;
    static float _nextPass;

    /// <summary>The tracker is running: keep the game's list invisible.</summary>
    internal static void Hide(bool hide)
    {
        if (_hide == hide) return;
        _hide = hide;
        _nextPass = 0f;
        if (!hide) Restore();
    }

    /// <summary>Called every frame; the work is done ten times a second (the game builds its main screen again after full-screen windows).</summary>
    internal static void Poll()
    {
        if (!_hide || Time.realtimeSinceStartup < _nextPass) return;
        _nextPass = Time.realtimeSinceStartup + 0.1f;
        try
        {
            if (_screenRoot == null) _screenRoot = FindScreenRoot();
            if (_screenRoot == null) return;
            var node = _screenRoot.Find(ListPath);
            if (node == null) return;
            if (node != _node)
            {
                Restore(); // a new node (the screen was rebuilt): the old one is gone or about to be
                _node = node;
                _original = node.localScale;
            }

            if (node.localScale != Vector3.zero) node.localScale = Vector3.zero;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("game buff bar: " + e.Message);
        }
    }

    static Transform FindScreenRoot()
    {
        foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (canvas != null && canvas.isRootCanvas && canvas.name == "zuiroot") return canvas.transform;
        return null;
    }

    static void Restore()
    {
        try
        {
            if (_node != null) _node.localScale = _original;
        }
        catch (Exception)
        {
        }

        _node = null;
    }
}
