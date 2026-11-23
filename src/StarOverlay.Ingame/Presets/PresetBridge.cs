using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using StarOverlay.Ingame.Ui;
using UnityEngine;

namespace StarOverlay.Ingame.Presets;

/// <summary>
/// The agent's part of the plugin "빠른 프리셋". While the plugin is on it reads the game's presets with the game's own Lua (the "심상" = the season ability
/// sets of season_talent_scheme_vm, the "캐릭터" = the character plans of weapon_vm) only when it has to (the plugin was switched on, the refresh button was
/// pressed, a switch was made), sends the list to the service when it changed, and
/// switches to the preset the player pressed by calling the game's own switch function, the way the game's preset screens do (refused while the game has
/// changes that are not saved, like those screens). While the plugin is off it does nothing at all.
/// </summary>
static class PresetBridge
{
    const float RetryEvery = 2f;      // seconds between two tries while the game cannot be read yet (just after the plugin was switched on)
    const float EnterDelay = 1f;      // seconds after entering the game before the first read
    const int MaxTries = 15;          // reads after one trigger while the game gives only part of the list
    const float VerifyAfter = 1.5f;   // seconds after a switch: the preset must be the current one by then
    const string Window = "quick.preset";

    static bool _active;
    static int _serial;
    static PipeLink _link;
    static float _nextRead;
    static bool _needRead;
    static bool _wasShown;
    static int _tries;
    static string _lastSent = "";
    static string _verifyKey = "";
    static float _verifyAt;
    static Dictionary<string, string> _states = new();

    /// <summary>The service says whether the plugin is on and what the player asked for ("kind:id" or "refresh"; <c>Serial</c> counts the requests).</summary>
    public static void Apply(PresetDto dto, PipeLink link)
    {
        _link = link;
        if (dto.Active != _active)
        {
            _active = dto.Active;
            _lastSent = "";
            _verifyKey = "";
            _needRead = _active; // read at once when switched on
            _nextRead = 0f;
            _tries = 0;
        }

        if (_active && !dto.HaveItems)
        {
            // the service has nothing to show (it was restarted, or the plugin was switched off and on): read and send the presets again, even when they did not change
            _lastSent = "";
            _needRead = true;
            _nextRead = 0f;
        }

        if (dto.Serial == _serial) return;
        _serial = dto.Serial;
        if (!_active) return;
        if (dto.Request == "refresh")
        {
            _tries = 0;
            _needRead = true;
            _nextRead = 0f;
        }
        else if (dto.Request.Length > 0) Switch(dto.Request);
    }

    /// <summary>
    /// The main screen came up (the player entered the game, or a loading screen ended): read the presets once, a moment later so the game has filled its data in.
    /// </summary>
    public static void Shown(bool shown)
    {
        var edge = shown && !_wasShown;
        _wasShown = shown;
        if (!edge || !_active) return;
        _tries = 0;
        _needRead = true;
        _nextRead = Time.realtimeSinceStartup + EnterDelay;
    }

    /// <summary>Every frame: reads the presets when one is wanted (and retries until the game could be read), and checks that a switch came through.</summary>
    public static void Tick()
    {
        if (!_active) return;
        var now = Time.realtimeSinceStartup;
        if (_verifyKey.Length > 0 && now >= _verifyAt)
        {
            Read(); // the state of the preset the player pressed is current now, or the game did not do it
            _needRead = false;
            if (_states.TryGetValue(_verifyKey, out var state) && state != "current") Fail(_verifyKey, "failed");
            _verifyKey = "";
            return;
        }

        if (!_needRead || now < _nextRead) return;
        if (Read() || ++_tries >= MaxTries) _needRead = false;
        else _nextRead = now + RetryEvery;
    }

    /// <summary>Reads the presets from the game. False when the game could not be read (not up yet, nothing there).</summary>
    static bool Read()
    {
        string text;
        try
        {
            text = GameLua.Query(ReadScript);
        }
        catch (Exception e)
        {
            StickerCache.Diag("presets: reading failed: " + e.Message + "\n"); // development build only
            return false; // the game's Lua is not up yet (or the screens are not loaded): the list stays as it is
        }

        if (string.IsNullOrEmpty(text)) return false;
        var states = new Dictionary<string, string>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split('|');
            if (parts.Length >= 4) states[parts[0] + ":" + parts[1]] = parts[3];
        }

        _states = states;
        var whole = text.Contains("mind|") && text.Contains("char|"); // both lists are there (the game fills its data in step by step after entering)
        StickerCache.Diag($"presets: read {states.Count} presets, {(text == _lastSent ? "unchanged" : "sent")}, {(whole ? "whole" : "partial")}\n"); // development build only
        if (text != _lastSent)
        {
            _lastSent = text;
            _link?.Send(new Envelope { T = MessageType.Cmd, Cmd = new CommandDto { Window = Window, Action = CommandAction.Presets, Value = text } });
        }

        return whole;
    }

    static void Switch(string key)
    {
        var colon = key.IndexOf(':');
        if (colon <= 0 || !int.TryParse(key[(colon + 1)..], out var id)) return;
        var kind = key[..colon];
        if (kind is not ("mind" or "char")) return;
        string answer;
        try
        {
            answer = GameLua.Query((kind == "mind" ? SwitchMind : SwitchChar).Replace("@ID@", id.ToString()));
        }
        catch (Exception)
        {
            answer = "failed";
        }

        if (answer == "ok")
        {
            _verifyKey = key;
            _verifyAt = Time.realtimeSinceStartup + VerifyAfter;
            return;
        }

        Fail(key, answer == "changed" ? "changed" : "failed");
    }

    static void Fail(string key, string why) =>
        _link?.Send(new Envelope { T = MessageType.Cmd, Cmd = new CommandDto { Window = Window, Action = CommandAction.PresetFailed, Key = key, Value = why } });

    // One line per preset: kind|number|name|state|icon (state: current, ready, empty or locked; a locked preset is one that is not unlocked yet). The icon of a season
    // ability set is the sprite of its main node (the first active area of its cultivate line, SeasonTalentTemplateTableMgr.BigIconNormal); the icon of a character
    // plan is its class: profession:<id>.
    const string ReadScript = @"
local out = {}
local nl = string.char(10)
local function clean(name) return (tostring(name or ''):gsub('|', ' '):gsub(nl, ' ')) end
local function add(kind, id, name, state, icon) out[#out + 1] = kind .. '|' .. id .. '|' .. clean(name) .. '|' .. state .. '|' .. clean(icon) end
local function mainNode(sv, id)
  local ok, icon = pcall(function()
    local data = sv.GetSchemeData(id)
    local rows = _G.Z.TableMgr.GetTable('SeasonTalentTemplateTableMgr')
    for _, line in pairs(data.cultivateLineMap) do
      local area = line.cultivateLineAreaList[1]
      if not area then
        for areaId, info in pairs(line.cultivateLineDataMap) do if info.isActive then area = areaId end end
      end
      local row = area and rows.GetRow(area, true)
      if row and row.BigIconNormal and row.BigIconNormal ~= '' then return row.BigIconNormal end
    end
  end)
  return ok and icon or ''
end
local sv = package.loaded['ui.view_model.season_talent_scheme_vm']
if sv then
  pcall(function()
    local current = sv.GetCurrentSchemeId()
    for id = 1, sv.GetSchemeCount() do
      local state = 'ready'
      if not sv.IsSchemeActive(id) then state = 'locked'
      elseif id == current then state = 'current'
      elseif sv.IsSchemeEmpty(id) then state = 'empty' end
      add('mind', id, sv.GetSchemeName(id), state, state == 'locked' and '' or mainNode(sv, id))
    end
  end)
end
local wv = package.loaded['ui.view_model.weapon_vm']
local wd = _G.Z.DataMgr.Get('weapon_data')
if wv and wd then
  pcall(function()
    local data = wd:GetRolePlanServerData()
    local unlocked = {}
    for id in pairs(data.PlanDataDict) do unlocked[id] = true end
    local total = _G.Z.Global.ModPlanNum[2]
    for id = 1, total do
      local state = 'ready'
      if not unlocked[id] then state = 'locked'
      elseif id == data.CurPlanId then state = 'current'
      elseif wv.IsRolePlanEmpty(id) then state = 'empty' end
      local plan = data.PlanDataDict[id]
      local class = plan and plan.professionId and plan.professionId > 0 and ('profession:' .. plan.professionId) or ''
      add('char', id, wd:GetPlanNameByPlanId(id), state, class)
    end
  end)
end
return table.concat(out, nl)
";

    // The answer is ok (the switch was handed to the game), changed (the game has changes that are not saved) or failed.
    const string SwitchMind = @"
local sv = package.loaded['ui.view_model.season_talent_scheme_vm']
local id = @ID@
if not sv or not sv.IsSchemeActive(id) or sv.IsSchemeEmpty(id) or sv.GetCurrentSchemeId() == id then return 'failed' end
if sv.IsCurrentSchemeChanged() then return 'changed' end
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  sv.AsyncSwitchScheme(id, token)
  pcall(function() source:Recycle() end)
end)
local ok = coroutine.resume(co)
if not ok then return 'failed' end
return 'ok'
";

    const string SwitchChar = @"
local wv = package.loaded['ui.view_model.weapon_vm']
local wd = _G.Z.DataMgr.Get('weapon_data')
local id = @ID@
if not wv or not wd then return 'failed' end
local data = wd:GetRolePlanServerData()
if data.PlanDataDict[id] == nil or wv.IsRolePlanEmpty(id) or data.CurPlanId == id then return 'failed' end
if wv.CheckRolePlanIsChange() then return 'changed' end
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  wv.AsyncSwitchRolePlan(id, token)
  pcall(function() source:Recycle() end)
end)
local ok = coroutine.resume(co)
if not ok then return 'failed' end
return 'ok'
";
}
