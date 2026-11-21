using System;
using System.Collections.Generic;
using HarmonyLib;
using Panda.ZGame;
using Panda.ZInput;
using StarOverlay.Ingame.Protocol;
using UnityEngine;

namespace StarOverlay.Ingame.Skills;

/// <summary>
/// Plugin "연속 입력": while the key of a skill the player chose is held, the game's own "fire this skill" call is made again at an interval.
/// The game decides every time whether the skill can be used (cooldown, animation) and keeps its own input cache, exactly as for a key press.
/// No key is simulated and nothing is sent to the server by us. It does nothing until the service says the plugin is loaded, and it stops
/// when the key is let go, the game loses the focus or a call fails.
/// Several keys can be held: the key pressed last decides. While it is held only it is repeated (nothing, when it is a skill that is not switched on);
/// when it is let go, the key held before it carries on.
/// </summary>
static class HeldSkillRepeater
{
    const float MaxHoldSeconds = 120f;
    const int MaxFailures = 3;

    static readonly object Gate = new();
    static HashSet<int> _skills = new();
    static int _interval = RepeatDto.DefaultInterval;
    static bool _active;
    static bool _patched;
    static bool _off;          // switched off after failures
    static int _failures;
    static PipeLink _link;

    static bool _pressInProgress;   // inside the game's handling of a real key press
    static SkillControlData _lastFire;
    static bool _repeating;         // our own call runs: the patches ignore it

    /// <summary>A skill key that is down now: what is needed to fire it again, and whether the player switched it on.</summary>
    sealed class Hold
    {
        public ZSkillInputMgr Mgr;
        public SkillControlData Data;
        public object Controller; // a PlayerInputController (kept as object: its base type lives in an assembly the agent does not reference); what the game was given for this key down: handing it over again makes the game find the skill the key means NOW
        public object Args;       // the InputActionEventArgs of that key down
        public int Slot;
        public int Skill;
        public bool Repeat;
        public float Since;
    }

    // what is learned from the keys that went through: the slot and the skill id (the game's real one, a combo changes it) of a skill's data object
    static readonly Dictionary<long, int> SlotOfData = new();
    static readonly Dictionary<long, int> SkillOfData = new();
    static object _pressController; // the controller and the key event of the key down that is being handled
    static object _pressArgs;
    static System.Reflection.MethodInfo _processAttacks;
    static bool _skipped;        // a repeat found that the slot now holds a skill that is not switched on
    static System.Reflection.PropertyInfo _rowProperty;
    static System.Reflection.PropertyInfo _idProperty;

    static readonly List<Hold> Held = new(); // in the order the keys were pressed: the last one decides
    static readonly List<Hold> PressHolds = new(); // the holds made while the game handled the key that was just pressed
    static Hold _target;                     // the key that was repeated at the last look
    static float _next;

    static readonly Dictionary<int, float> Reported = new();

    /// <summary>The service told the chosen skills (called on the game's main thread).</summary>
    public static void Apply(RepeatDto dto, PipeLink link)
    {
        lock (Gate)
        {
            _link = link;
            _active = dto.Active && !_off;
            _skills = new HashSet<int>(dto.Skills ?? new List<int>());
            _interval = Math.Clamp(dto.IntervalMs, 50, 500);
        }

        if (_active && !_patched) Patch();
        if (!_active) Stop();
        foreach (var hold in Held) hold.Repeat = _skills.Contains(hold.Skill); // a switch changes what a held key does at once
    }

    static void Patch()
    {
        _patched = true;
        try
        {
            var harmony = new Harmony("staroverlay.skillrepeat");
            Hook(harmony, typeof(PlayerInputController), "processAttacksPressed", nameof(BeforeKeyDown), nameof(AfterKeyDown));
            Hook(harmony, typeof(ZSkillInputMgr), "FirePlaySkillEvent", nameof(BeforeFire));
            Hook(harmony, typeof(ZSkillInputMgr), "updateSlotPress", nameof(BeforeSlotPress));
            Hook(harmony, typeof(ZSkillInputMgr), "cacheInput", nameof(BeforeRelease));
            Plugin.L.LogInfo("skill repeat: hooks are in");
        }
        catch (Exception e)
        {
            Fail("hooks failed: " + e.Message);
        }
    }

    static void Hook(Harmony harmony, Type type, string method, string prefix, string postfix = null)
    {
        var original = AccessTools.Method(type, method) ?? throw new InvalidOperationException(type.Name + "." + method + " not found");
        harmony.Patch(original,
            prefix: new HarmonyMethod(typeof(HeldSkillRepeater), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(HeldSkillRepeater), postfix));
    }

    // ---- what the game does when a key is pressed: key down -> fire the skill -> mark the slot as pressed; key up -> clear the slot, cache the input

    static void BeforeKeyDown(PlayerInputController __instance, InputActionEventArgs inputActionEventData)
    {
        _pressController = __instance;
        _pressArgs = inputActionEventData;
        _pressInProgress = true;
        PressHolds.Clear();
    }

    // the game says which slot the pressed key is (AttackPressIdx, the same number as the slot of updateSlotPress), also when it cannot use the skill now:
    // that is the key's identity for the release. A press that is no skill key (nothing pressed, no slot) does not hold anything.
    static void AfterKeyDown(PlayerInputController __instance)
    {
        _pressInProgress = false;
        try
        {
            var slot = __instance.AttackPressIdx;
            var pressing = __instance.IsAttackPress;
            foreach (var hold in PressHolds.ToArray())
            {
                if (!pressing || slot <= 0)
                {
                    Held.Remove(hold);
                    continue;
                }

                hold.Slot = slot;
                hold.Controller = __instance;
                hold.Args = _pressArgs;
                Held.RemoveAll(other => !ReferenceEquals(other, hold) && other.Slot == slot); // an older hold of the same key is replaced
            }
        }
        catch (Exception)
        {
        }

        PressHolds.Clear();
    }

    // the game fires the skill of a pressed key whether it can be used now or not (a cooldown, another skill still running): the key is held from
    // here on, so it keeps being fired like a fast clicking until the game takes it, also when the first input found no use
    static bool BeforeFire(ZSkillInputMgr __instance, SkillControlData skillEnt)
    {
        try
        {
            if (_repeating)
            {
                // our own repeat: the game found the skill the key means now; one that is not switched on is not fired
                var id = RowId(skillEnt);
                if (id > 0 && !_skills.Contains(id))
                {
                    _skipped = true;
                    return false;
                }

                return true;
            }

            if (!_pressInProgress || skillEnt == null) return true;
            _lastFire = skillEnt;
            if (!_active) return true;
            var key = skillEnt.Pointer.ToInt64();
            SlotOfData.TryGetValue(key, out var slot);
            if (!SlotOfData.ContainsKey(key)) slot = -1; // not known yet: it is learned when the game takes a press of it
            if (!SkillOfData.TryGetValue(key, out var skill)) skill = RowId(skillEnt);
            var hold = new Hold { Mgr = __instance, Data = skillEnt, Slot = slot, Skill = skill, Repeat = _skills.Contains(skill), Since = Time.realtimeSinceStartup };
            AddHold(hold);
            PressHolds.Add(hold);
        }
        catch (Exception e)
        {
            Fail("fire: " + e.Message);
        }

        return true;
    }

    static void AddHold(Hold hold)
    {
        Held.RemoveAll(h => ReferenceEquals(h.Data, hold.Data) || h.Data.Pointer == hold.Data.Pointer || (hold.Slot >= 0 && h.Slot == hold.Slot));
        Held.Add(hold);
    }

    /// <summary>The skill id in the data of a skill (its table row), 0 when it cannot be read.</summary>
    static int RowId(SkillControlData data)
    {
        try
        {
            _rowProperty ??= typeof(SkillControlData).GetProperty("SkillRow");
            var row = _rowProperty?.GetValue(data);
            if (row == null) return 0;
            _idProperty ??= row.GetType().GetProperty("Id");
            return _idProperty?.GetValue(row) is int id ? id : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    static void BeforeSlotPress(ZSkillInputMgr __instance, int slotId, int skillId)
    {
        try
        {
            if (_repeating)
            {
                return;
            }

            if (!_active || !_pressInProgress || _lastFire == null) return; // a replay of the game's input cache is no key press
            Report(skillId);
            var key = _lastFire.Pointer.ToInt64();
            if (SlotOfData.Count > 256) { SlotOfData.Clear(); SkillOfData.Clear(); } // the game makes new skill data now and then: the old pointers are not kept for ever
            SlotOfData[key] = slotId; // learned: the slot of this skill's data and the real skill id of this press
            SkillOfData[key] = skillId;
            var hold = new Hold { Mgr = __instance, Data = _lastFire, Slot = slotId, Skill = skillId, Repeat = _skills.Contains(skillId), Since = Time.realtimeSinceStartup };
            AddHold(hold);
            PressHolds.Clear();
            PressHolds.Add(hold);
        }
        catch (Exception e)
        {
            Fail("press: " + e.Message);
        }
    }

    // the game reports the key that was let go with its slot: the key held before it carries on
    static void BeforeRelease(int slotId)
    {
        if (_repeating) return;
        Held.RemoveAll(hold => hold.Slot == slotId);
    }

    static void Report(int skillId)
    {
        var now = Time.realtimeSinceStartup;
        if (Reported.TryGetValue(skillId, out var last) && now - last < 2f) return;
        Reported[skillId] = now;
        _link?.Send(new Envelope { T = MessageType.Cmd, Cmd = new CommandDto { Window = "skill.repeat", Action = CommandAction.SkillPressed, Key = skillId.ToString() } });
    }

    public static void Stop()
    {
        Held.Clear();
        _target = null;
    }

    /// <summary>Every frame: fires the key that decides again when its interval is over.</summary>
    public static void Tick()
    {
        if (Held.Count == 0) return;
        var now = Time.realtimeSinceStartup;
        if (!_active || !Application.isFocused)
        {
            Stop();
            return;
        }

        var top = Held[Held.Count - 1];
        if (!top.Repeat || now - top.Since > MaxHoldSeconds)
        {
            _target = null; // this key (or none) decides: nothing is repeated, the one before it waits
            return;
        }

        if (!ReferenceEquals(top, _target))
        {
            _target = top; // another key took over (or the first one starts): its first repeat comes one interval later, as a smooth carry on
            _next = now + _interval / 1000f;
            return;
        }

        if (now < _next) return;
        _next = now + _interval / 1000f;
        _repeating = true;
        try
        {
            _skipped = false;
            if (top.Controller != null && top.Args != null)
            {
                // a fast clicking: let go, then press again. A key that stays down never lets the game go on to the next step of a skill
                // (the chase 질풍 찌르기 → 공중 반격), a click does. The press finds the skill of the key as it is now (a derived skill that is gone is not fired).
                var processAttacks = _processAttacks ??= AccessTools.Method(typeof(PlayerInputController), "processAttacks");
                processAttacks.Invoke(top.Controller, new[] { top.Args, false });
                processAttacks.Invoke(top.Controller, new[] { top.Args, true });
            }
            else
                top.Mgr.FirePlaySkillEvent(top.Data, true, false, false);
            if (_skipped) top.Repeat = false;
            _failures = 0;
        }
        catch (Exception e)
        {
            Held.Remove(top);
            if (++_failures >= MaxFailures) Fail("repeat call failed: " + e.Message);
        }
        finally
        {
            _repeating = false;
        }
    }

    static void Fail(string reason)
    {
        _off = true;
        _active = false;
        Stop();
        Plugin.L.LogWarning("skill repeat is off: " + reason);
        _link?.Send(new Envelope { T = MessageType.Err, Error = "연속 입력을 껐습니다: " + reason });
    }
}
