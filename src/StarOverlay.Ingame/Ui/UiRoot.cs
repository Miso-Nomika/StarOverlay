using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace StarOverlay.Ingame.Ui;

/// <summary>The canvas that holds every overlay window. Created once, kept across scene loads.</summary>
internal sealed class UiRoot
{
    public const string RootId = "root";
    const int MaxFailures = 3;

    readonly PipeLink _link;
    readonly Dictionary<string, WindowView> _windows = new();
    readonly HashSet<string> _off = new();
    GameObject _canvasObject;

    public UiRoot(PipeLink link) => _link = link;

    public Canvas Canvas { get; private set; }

    /// <summary>Folder with one subfolder per plugin (their assets\ icons live there); sent by the service.</summary>
    public string AssetsRoot { get; set; } = "";

    /// <summary>Screen pixels per reference pixel (the canvas scaler's factor); used to turn drag distances into reference pixels.</summary>
    public static float Scale { get; private set; } = 1f;

    public void Send(CommandDto command) => _link.Send(new Envelope { T = MessageType.Cmd, V = Protocol.Protocol.Version, Cmd = command });

    /// <summary>Shows or hides every window of ours at once (the whole canvas): they are drawn only while the game shows its chat input bar.</summary>
    public void SetVisible(bool visible)
    {
        // hidden by a group (alpha 0, no clicks), never by switching the canvas object off and on: the game walks through the canvases it
        // finds and switches them as it likes, and a canvas that is off at that moment is not brought back by it
        foreach (var group in new[] { _group, _chatGroup, _windowsGroup, _settingsGroup })
        {
            if (group == null) continue;
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        if (!visible) _wasVisible = false;
        else if (_canvasObject != null && (!_wasVisible || Time.realtimeSinceStartup >= _nextWatch)) KeepOnTop();
    }

    bool _wasVisible;
    CanvasGroup _group;
    float _nextWatch;

    /// <summary>
    /// While the windows are shown, our canvas must be switched on and drawn above the game's screens. The game switches its own canvases
    /// off and on and gives each screen it opens a higher draw order, so a canvas that was above everything once can end up below.
    /// </summary>
    void KeepOnTop()
    {
        _wasVisible = true;
        _nextWatch = Time.realtimeSinceStartup + 3f;
        try
        {
            if (!Canvas.enabled) Canvas.enabled = true;
            if (ChatCanvas != null && !ChatCanvas.enabled) ChatCanvas.enabled = true;
            if (WindowsCanvas != null && !WindowsCanvas.enabled) WindowsCanvas.enabled = true;
            if (SettingsCanvas != null && !SettingsCanvas.enabled) SettingsCanvas.enabled = true;

            var top = 0;
            foreach (var other in UnityEngine.Object.FindObjectsOfType<Canvas>())
                if (other != null && other != Canvas && other != ChatCanvas && other != WindowsCanvas && other != SettingsCanvas && other.isRootCanvas && other.enabled && other.sortingOrder > top) top = other.sortingOrder;
            if (Canvas.sortingOrder <= top) Canvas.sortingOrder = top + 1;

        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("canvas check: " + e.Message);
        }
    }

    public void Ensure()
    {
        if (_canvasObject != null) return;
        _canvasObject = MakeCanvas("StarOverlayCanvas", 5000, out var canvas, out _group);
        Canvas = canvas;
    }

    // Our windows are drawn by the game's own camera, over the game's main screen (300: the boss bar and the rest of the HUD) and under its function
    // layer (600: the character card and the other windows our clicks open; the item card at 2710 is over everything). The game's invisible click areas
    // in the main layer do not catch the clicks meant for our windows. The channel tab, which opens inside the main layer, is shown by sending the
    // chat just under that layer while it is open.
    // An overlay canvas is always drawn over every camera canvas, whatever its order, which is why the chat could not be lowered while it lived on
    // the overlay canvas.
    const int ChatOrder = 590;
    const int ChatOrderTabOpen = 299;
    // All the other windows (meter, tracker, party join, settings, the system window, notes) are drawn by the same camera on a canvas of their own,
    // just over the chat, while the chat alone goes under the main layer when the channel tab is open (the other windows keep their place, and
    // their clicks, meanwhile).
    const int WindowsOrder = 591;
    GameObject _windowsObject;
    CanvasGroup _windowsGroup;
    // The settings windows (and the notes) are over everything of the game, the item card at 2710 included: a window to be worked in must not be
    // covered by the game's screens. Own canvas, same camera.
    const int SettingsOrder = 3000;
    GameObject _settingsObject;
    CanvasGroup _settingsGroup;
    float _nextTabCheck;
    bool _tabOpen;
    GameObject _chatObject;
    CanvasGroup _chatGroup;
    float _nextChatSearch;

    /// <summary>
    /// While the channel tab is open the chat is under the game's main layer, whose invisible click catcher takes the clicks, so a click in the
    /// chat is read from the mouse itself: a click in the chat window that is not on the tab's list closes the tab (the game's channel button is pressed,
    /// as the user would to close it).
    /// </summary>
    void CloseTabOnChatClick(WindowView chat)
    {
        try
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var camera = ChatCanvas.worldCamera;
            var mouse = Input.mousePosition;
            var at = new Vector2(mouse.x, mouse.y);
            var rect = chat.WindowObject.GetComponent<RectTransform>();
            if (rect == null || !RectTransformUtility.RectangleContainsScreenPoint(rect, at, camera)) return;
            if (GameChatUi.ChannelTabContains(at, camera)) return; // a click on a channel of the list is the game's own
            GameChatUi.CloseChannelTab();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("channel tab close: " + e.Message);
        }
    }

    public Canvas ChatCanvas { get; private set; }

    /// <summary>The camera canvas of every window but the chat (null until the game's camera is known).</summary>
    public Canvas WindowsCanvas { get; private set; }

    /// <summary>The camera canvas of the settings windows and the notes, over every screen of the game (null until the game's camera is known).</summary>
    public Canvas SettingsCanvas { get; private set; }

    /// <summary>Where the settings windows are drawn: their canvas once the game's camera is known, before that the overlay canvas.</summary>
    public Transform SettingsParent => SettingsCanvas != null ? SettingsCanvas.transform : Canvas.transform;

    /// <summary>Where the window of a plugin is drawn: on the camera canvases once the game's camera is known (the chat on its own), before that on the overlay canvas.</summary>
    public Transform ParentOf(string id) => id == "chat" ? (ChatCanvas != null ? ChatCanvas.transform : Canvas.transform) : SharedParent;

    /// <summary>The parent of the windows that share a canvas (everything but the chat).</summary>
    public Transform SharedParent => WindowsCanvas != null ? WindowsCanvas.transform : Canvas.transform;

    /// <summary>Makes the camera canvases (the chat's and the other windows') as soon as the game's root canvas (and its camera) exists; keeps them on that camera.</summary>
    void EnsureChatCanvas()
    {
        if (ChatCanvas != null && ChatCanvas.worldCamera != null && WindowsCanvas != null && WindowsCanvas.worldCamera != null && SettingsCanvas != null && SettingsCanvas.worldCamera != null) return;
        if (Time.realtimeSinceStartup < _nextChatSearch) return;
        _nextChatSearch = Time.realtimeSinceStartup + 0.5f;
        try
        {
            foreach (var game in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (game == null || !game.isRootCanvas || game.name != "zuiroot" || game.renderMode == RenderMode.ScreenSpaceOverlay || game.worldCamera == null) continue;
                if (_chatObject == null)
                {
                    _chatObject = MakeCanvas("StarOverlayChatCanvas", ChatOrder, out var canvas, out _chatGroup);
                    ChatCanvas = canvas;
                    _chatObject.layer = game.gameObject.layer; // the game's camera draws only its UI layer
                }

                if (_windowsObject == null)
                {
                    _windowsObject = MakeCanvas("StarOverlayWindowsCanvas", WindowsOrder, out var canvas, out _windowsGroup);
                    WindowsCanvas = canvas;
                    _windowsObject.layer = game.gameObject.layer;
                }

                if (_settingsObject == null)
                {
                    _settingsObject = MakeCanvas("StarOverlaySettingsCanvas", SettingsOrder, out var canvas, out _settingsGroup);
                    SettingsCanvas = canvas;
                    _settingsObject.layer = game.gameObject.layer;
                }

                foreach (var ours in new[] { ChatCanvas, WindowsCanvas, SettingsCanvas })
                {
                    ours.renderMode = RenderMode.ScreenSpaceCamera;
                    ours.worldCamera = game.worldCamera;
                    ours.planeDistance = game.planeDistance;
                    ours.sortingLayerID = game.sortingLayerID;
                }

                return;
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("chat canvas: " + e.Message);
        }
    }

    static GameObject MakeCanvas(string name, int order, out Canvas canvas, out CanvasGroup group)
    {
        var host = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(host);
        canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = host.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2((float)LayoutMath.ReferenceWidth, (float)LayoutMath.ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        host.AddComponent<GraphicRaycaster>();
        group = host.AddComponent<CanvasGroup>();
        group.alpha = 0f; // hidden until the agent says the main screen is shown
        group.blocksRaycasts = false;
        return host;
    }

    readonly Dictionary<string, SettingsView> _settings = new();
    readonly HashSet<string> _settingsOpen = new();
    List<WindowDto> _last = new();

    /// <summary>Opens the in-game settings window of a plugin (or brings its settings tab back).</summary>
    public void OpenSettings(string plugin)
    {
        Send(new CommandDto { Window = plugin, Action = CommandAction.SettingsShown, Value = "true" });
        // pressing "설정" always shows the settings tab (the window is closed with its own 닫기 button)
        if (_settingsOpen.Add(plugin) && _last.Exists(w => w.Id == plugin && w.HasRecords))
            Send(new CommandDto { Window = plugin, Action = CommandAction.Records });
        Apply(_last);
        if (_settings.TryGetValue(plugin, out var view)) view.ShowSettingsTab();
    }

    public void CloseSettings(string plugin)
    {
        Send(new CommandDto { Window = plugin, Action = CommandAction.SettingsShown, Value = "false" });
        _settingsOpen.Remove(plugin);
        Apply(_last);
    }

    public void OnRecords(string plugin, List<RecordHeadDto> heads)
    {
        if (plugin != null && _settings.TryGetValue(plugin, out var view)) view.OnRecords(heads);
    }

    public void OnRecord(string plugin, RecordDto record)
    {
        if (plugin != null && _settings.TryGetValue(plugin, out var view)) view.OnRecord(record);
    }

    void ApplySettings(List<WindowDto> windows)
    {
        foreach (var plugin in new List<string>(_settings.Keys))
        {
            if (_settingsOpen.Contains(plugin) && windows.Exists(w => w.Id == plugin)) continue;
            _settings[plugin].Destroy();
            _settings.Remove(plugin);
        }

        foreach (var plugin in _settingsOpen)
        {
            var dto = windows.Find(w => w.Id == plugin);
            if (dto == null) continue;
            try
            {
                if (!_settings.TryGetValue(plugin, out var view)) _settings[plugin] = view = new SettingsView(this, plugin);
                view.Apply(dto);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"settings {plugin}: {e.Message}");
                if (_settings.TryGetValue(plugin, out var broken)) broken.Destroy();
                _settings.Remove(plugin);
                _settingsOpen.Remove(plugin);
                _link.Send(new Envelope { T = MessageType.Err, Error = $"설정 창 {plugin}을(를) 그리지 못해 닫았습니다: {e.Message}" });
                break; // the set changed while iterating
            }
        }
    }

    GameObject _tip;
    TMPro.TextMeshProUGUI _tipText;
    GameObject _tipOwner;

    /// <summary>Shows a small note under <paramref name="anchor"/> (an icon) while the cursor rests on it.</summary>
    public void ShowTip(GameObject anchor, string text)
    {
        if (anchor == null || string.IsNullOrEmpty(text) || Canvas == null) return;
        if (_tip == null)
        {
            _tip = UiKit.Node("Tooltip", SettingsParent);
            var back = UiKit.Box("Back", _tip.transform, new Color(0.04f, 0.05f, 0.07f, 0.96f));
            UiKit.Stretch(back.gameObject);
            _tipText = UiKit.Text("Text", _tip.transform, 14, Color.white, TMPro.TextAlignmentOptions.MidlineLeft);
            _tipText.enableWordWrapping = false;
            _tipText.overflowMode = TMPro.TextOverflowModes.Overflow;
            UiKit.Stretch(_tipText.gameObject, 8, 4, 8, 4);
        }

        _tipOwner = anchor;
        UiKit.SetText(_tipText, text.Replace("<", "<​"));
        var width = Math.Min(560.0, Math.Max(60.0, MeasureWidth(text) + 16));
        var lines = text.Split('\n').Length;
        var rt = UiKit.Rt(anchor);
        // the anchor's world position as screen pixels (on the overlay canvas they are the same; on a camera canvas the camera tells)
        var anchorCanvas = anchor.GetComponentInParent<Canvas>();
        var anchorRoot = anchorCanvas == null ? null : anchorCanvas.rootCanvas;
        var anchorCamera = anchorRoot != null && anchorRoot.renderMode != RenderMode.ScreenSpaceOverlay ? anchorRoot.worldCamera : null;
        var screen = RectTransformUtility.WorldToScreenPoint(anchorCamera, rt.position);
        var x = screen.x / Scale;
        var y = (Screen.height - screen.y) / Scale + rt.rect.height + 4;
        var maxX = Screen.width / Scale - width - 4;
        UiKit.Place(_tip, Math.Max(2, Math.Min(x, maxX)), y, width, 8 + lines * 20);
        _tip.transform.SetAsLastSibling();
        _tip.SetActive(true);
    }

    public void HideTip(GameObject anchor)
    {
        if (_tip == null || (anchor != null && _tipOwner != anchor)) return;
        _tip.SetActive(false);
        _tipOwner = null;
    }

    // about 8 reference pixels per character at size 14 for the longest line (Korean glyphs are wider)
    static double MeasureWidth(string text)
    {
        var longest = 0;
        foreach (var line in text.Split('\n')) longest = Math.Max(longest, line.Length);
        return longest * 13.0;
    }

    int _imagesSeen;

    public void Tick()
    {
        if (_imagesSeen != GameImages.Version)
        {
            _imagesSeen = GameImages.Version;
            Apply(_last); // a picture of the game just arrived: the windows are drawn again with it
        }

        foreach (var view in _settings.Values) view.Tick();
        foreach (var window in _windows.Values) window.Tick();
        if (_tip != null && _tip.activeSelf && (_tipOwner == null || !_tipOwner.activeInHierarchy)) _tip.SetActive(false); // the icon went away while hovered
        if (Canvas != null) Scale = Math.Max(0.1f, Canvas.scaleFactor);
        EnsureChatCanvas();
        WindowView chat = null;
        if (ChatCanvas != null && _windows.TryGetValue("chat", out chat)) chat.MoveTo(ChatCanvas.transform); // the camera canvas came after the window
        if (WindowsCanvas != null)
        {
            foreach (var pair in _windows) if (pair.Key != "chat") pair.Value.MoveTo(WindowsCanvas.transform);
        }

        if (SettingsCanvas != null)
        {
            foreach (var view in _settings.Values) view.MoveTo(SettingsCanvas.transform);
            if (_tip != null && _tip.transform.parent != SettingsCanvas.transform) // the notes are over the settings windows too
            {
                _tip.transform.SetParent(SettingsCanvas.transform, false);
                UiKit.SetLayer(_tip, SettingsCanvas.gameObject.layer);
            }
        }

        if (ChatCanvas != null && Time.realtimeSinceStartup >= _nextTabCheck)
        {
            _nextTabCheck = Time.realtimeSinceStartup + 0.1f;
            _tabOpen = GameChatUi.ChannelTabOpen();
            var order = _tabOpen ? ChatOrderTabOpen : ChatOrder;
            if (ChatCanvas.sortingOrder != order) ChatCanvas.sortingOrder = order;
        }
        if (_tabOpen && chat != null && ChatCanvas != null) CloseTabOnChatClick(chat);

        DropKeyboardFocus();
    }

    /// <summary>
    /// A clicked button stays "selected" in Unity's event system, and the game's Enter key (opening the chat input) is the UI's
    /// Submit key: it would press that button again with no mouse (e.g. the chat "전체" channel). Our buttons are mouse-only,
    /// so their selection is dropped at once.
    /// </summary>
    void DropKeyboardFocus()
    {
        var system = UnityEngine.EventSystems.EventSystem.current;
        var selected = system != null ? system.currentSelectedGameObject : null;
        if (selected != null && Canvas != null && (selected.transform.IsChildOf(Canvas.transform) || (ChatCanvas != null && selected.transform.IsChildOf(ChatCanvas.transform)) || (WindowsCanvas != null && selected.transform.IsChildOf(WindowsCanvas.transform)) || (SettingsCanvas != null && selected.transform.IsChildOf(SettingsCanvas.transform))))
            system.SetSelectedGameObject(null);
    }

    /// <summary>Applies a full snapshot: creates, updates and removes windows. One window failing three times is turned off.</summary>
    public void Apply(List<WindowDto> windows)
    {
        Ensure();
        EnsureChatCanvas();
        _last = windows;
        GameBuffBar.Hide(windows.Exists(w => w.Id == "buff.tracker")); // the tracker shows the buffs: the game's own bar goes away while it runs
        var ids = new HashSet<string>();
        foreach (var dto in windows)
        {
            ids.Add(dto.Id);
            if (_off.Contains(dto.Id)) continue;
            try
            {
                if (!_windows.TryGetValue(dto.Id, out var view))
                    _windows[dto.Id] = view = new WindowView(this, dto.Id);
                view.Apply(dto);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"window {dto.Id}: {e.Message}");
                if (_windows.TryGetValue(dto.Id, out var broken) && ++broken.Failures >= MaxFailures)
                {
                    broken.Destroy();
                    _windows.Remove(dto.Id);
                    _off.Add(dto.Id);
                    _link.Send(new Envelope { T = MessageType.Err, Error = $"창 {dto.Id}을(를) 3번 그리지 못해 껐습니다: {e.Message}" });
                }
            }
        }

        foreach (var id in new List<string>(_windows.Keys))
        {
            if (ids.Contains(id)) continue;
            _windows[id].Destroy();
            _windows.Remove(id);
        }

        ApplySettings(windows);
    }
}
