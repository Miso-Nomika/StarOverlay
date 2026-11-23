using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>Runs on the game's main thread: takes what the pipe thread received and draws it.</summary>
public class AgentBehaviour : MonoBehaviour
{
    public AgentBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    internal static AgentBehaviour Instance;
    internal PipeLink Link;

    UiRoot _ui;
    List<WindowDto> _pending;
    int _errors;
    float _nextHud;

    void Update()
    {
        try
        {
            if (Link == null) return;
            _ui ??= new UiRoot(Link);
            if (Time.realtimeSinceStartup >= _nextHud)
            {
                _nextHud = Time.realtimeSinceStartup + 0.1f; // ten times a second
                // our windows are drawn only while the game shows its main screen (not on the server selection, not while loading)
                var shown = GameChatUi.MainScreenShown();
                _ui.SetVisible(shown);
                TableDump.Visible = shown;
                Presets.PresetBridge.Shown(shown);
            }

            TableDump.Tick();
            Skills.HeldSkillRepeater.Tick();
            Presets.PresetBridge.Tick();
            Party.PartyBridge.Tick();
            GameImages.Poll();
            _ui.Tick();
            GameChatInput.Poll();
            StickerCache.Poll();
            ItemCardMover.Poll();
            TabIcons.Poll();
            GameBuffBar.Poll();
            CardSeeker.Poll();

            var budget = 20;
            while (budget-- > 0 && Link.TryDequeue(out var message))
            {
                if (message.T == MessageType.Ui && message.Windows != null) _pending = message.Windows; // only the newest matters
                else if (message.T == MessageType.Assets && message.Root != null) _ui.AssetsRoot = message.Root;
                else if (message.T == MessageType.Records) _ui.OnRecords(message.Window, message.Records);
                else if (message.T == MessageType.Record) _ui.OnRecord(message.Window, message.Record);
                else if (message.T == MessageType.Tables && message.Root != null) TableDump.Request(Link, message.Root);
                else if (message.T == MessageType.Repeat && message.Repeat != null) Skills.HeldSkillRepeater.Apply(message.Repeat, Link);
                else if (message.T == MessageType.Preset && message.Preset != null) Presets.PresetBridge.Apply(message.Preset, Link);
                else if (message.T == MessageType.Party && message.Party != null) Party.PartyBridge.Apply(message.Party, Link);
                else if (message.T == MessageType.Err) Plugin.L.LogWarning("service: " + message.Error);
            }

            if (_pending != null)
            {
                var windows = _pending;
                _pending = null;
                _ui.Apply(windows);
            }

        }
        catch (Exception e)
        {
            // a failure here must not reach the game; log a few and carry on
            if (_errors++ < 10) Plugin.L.LogError("agent update: " + e);
        }
    }
}
