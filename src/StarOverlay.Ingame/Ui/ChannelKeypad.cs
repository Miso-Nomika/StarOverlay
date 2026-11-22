using System;
using TMPro;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The number pad that opens above our "월드 ch" badge, like the game's own: 1-9, DEL, 0, OK. The digits pressed so far are shown on top; OK moves to that world channel.
/// </summary>
static class ChannelKeypad
{
    const float Key = 32f;
    const float Gap = 4f;
    const float Pad = 6f;
    const float DisplayHeight = 28f;
    const int MaxDigits = 4;
    static readonly string[] Keys = { "1", "2", "3", "DEL", "4", "5", "6", "0", "7", "8", "9", "OK" };

    static GameObject _panel;
    static TextMeshProUGUI _display;
    static string _digits = "";
    static bool _open;

    internal static float Width => 4 * Key + 3 * Gap + 2 * Pad;

    internal static float Height => Pad + DisplayHeight + Gap + 3 * Key + 2 * Gap + Pad;

    internal static bool IsOpen => _open;

    /// <summary>Opens the pad (empty), or closes it when it is open.</summary>
    internal static void Toggle(Transform root)
    {
        if (_panel == null) Build(root);
        _open = !_open;
        _digits = "";
        _panel.SetActive(_open);
        Show();
    }

    internal static void Close()
    {
        _open = false;
        if (_panel != null) _panel.SetActive(false);
    }

    /// <summary>Keeps the pad above the badge (its left edge x and top edge y in reference pixels counted from the top left of the screen).</summary>
    internal static void Follow(float badgeX, float badgeY)
    {
        if (!_open || _panel == null) return;
        _panel.transform.SetAsLastSibling();
        UiKit.Place(_panel, badgeX, Math.Max(0f, badgeY - Height - 4f), Width, Height);
    }

    static void Build(Transform root)
    {
        _panel = UiKit.Node("WorldChannelPad", root);
        UiKit.Box("Back", _panel.transform, new Color(0.12f, 0.13f, 0.17f, 0.97f), raycast: true);
        UiKit.Stretch(_panel.transform.Find("Back").gameObject);
        var shown = UiKit.Box("Display", _panel.transform, new Color(0.05f, 0.06f, 0.08f, 1f));
        UiKit.Place(shown.gameObject, Pad, Pad, Width - 2 * Pad, DisplayHeight);
        _display = UiKit.Text("DisplayText", shown.transform, 16, Color.white, TextAlignmentOptions.Center);
        UiKit.Stretch(_display.gameObject);
        for (var i = 0; i < Keys.Length; i++)
        {
            var name = Keys[i];
            var tint = name == "OK" ? new Color(0.36f, 0.45f, 0.2f, 1f) : name == "DEL" ? new Color(0.34f, 0.3f, 0.3f, 1f) : new Color(0.24f, 0.26f, 0.33f, 1f);
            var label = UiKit.Button("Key" + name, _panel.transform, name, tint, () => Press(name));
            label.fontSize = name.Length > 1 ? 13 : 16;
            UiKit.Place(label.transform.parent.gameObject, Pad + (i % 4) * (Key + Gap), Pad + DisplayHeight + Gap + (i / 4) * (Key + Gap), Key, Key);
        }

        _panel.SetActive(false);
    }

    static void Press(string key)
    {
        if (key == "DEL")
        {
            if (_digits.Length > 0) _digits = _digits[..^1];
        }
        else if (key == "OK")
        {
            if (int.TryParse(_digits, out var number) && number > 0 && !WorldChannelSwitch.Switch(number)) GameChatInput.FlashBadgeRed();
            Close();
            return;
        }
        else if (_digits.Length < MaxDigits && !(_digits.Length == 0 && key == "0"))
        {
            _digits += key;
        }

        Show();
    }

    // "월드 ch3": what has been pressed so far, so the pressed number can be seen
    static void Show()
    {
        if (_display == null) return;
        _display.color = _digits.Length == 0 ? new Color(0.62f, 0.64f, 0.7f, 1f) : new Color(1f, 0.78f, 0.45f, 1f);
        UiKit.SetText(_display, "월드 ch" + (_digits.Length == 0 ? "_" : _digits));
    }
}
