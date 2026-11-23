using System;
using System.Collections.Generic;
using StarOverlay.Ingame.Protocol;
using StarOverlay.Ingame.Ui;
using UnityEngine;

namespace StarOverlay.Ingame.Party;

/// <summary>
/// The agent's part of the plugin "파티 가입". While the plugin is on it reads the master score of every applicant once (the game's own request for a
/// character's data, the one its character card uses, without opening the card) and sends it to the service, and it hands the answer the player
/// pressed (accept or reject) to the game by calling the game's own function for it, the way the game's application screen does. While the plugin is off
/// it does nothing at all.
/// </summary>
static class PartyBridge
{
    const string Window = "party.join";
    const float CheckEvery = 0.2f; // seconds between two looks at a score that is on its way
    const float GiveUpAfter = 10f; // seconds to wait for the game's answer

    static bool _active;
    static int _serial = -1; // the first message after the agent started only tells where the counter is: an old accept or reject is never done again
    static PipeLink _link;
    static readonly HashSet<long> Asked = new();
    static readonly Queue<long> Waiting = new();
    static long _current;
    static float _since;
    static float _nextCheck;
    static float _retryAt; // the game's Lua was not up when a request was to start: the next one waits a moment
    const int Unknown = -2; // sent when the game gave no score (shown as "-" instead of waiting for ever)

    /// <summary>The service says whether the plugin is on, whose master score is wanted, and what the player pressed (<c>Serial</c> counts the presses).</summary>
    public static void Apply(PartyDto dto, PipeLink link)
    {
        _link = link;
        if (dto.Active != _active)
        {
            _active = dto.Active;
            Asked.Clear();
            Waiting.Clear();
            _current = 0;
        }

        if (!_active)
        {
            _serial = dto.Serial;
            return;
        }

        foreach (var id in dto.Need)
            if (Asked.Add(id)) Waiting.Enqueue(id);

        if (_serial < 0)
        {
            _serial = dto.Serial;
            return;
        }

        if (dto.Serial == _serial) return;
        _serial = dto.Serial;
        Deal(dto.Request);
    }

    /// <summary>Every frame: starts the next score request and looks at the one on its way (only while there is one).</summary>
    public static void Tick()
    {
        if (!_active || (_current == 0 && Waiting.Count == 0)) return;
        var now = Time.realtimeSinceStartup;
        if (_current == 0 && now < _retryAt) return;
        if (_current == 0)
        {
            _current = Waiting.Dequeue();
            _since = now;
            _nextCheck = now + CheckEvery;
            try
            {
                GameLua.Execute(StartScript.Replace("@ID@", _current.ToString()));
            }
            catch (Exception)
            {
                Waiting.Enqueue(_current); // the game's Lua is not up yet: asked again in a moment
                _current = 0;
                _retryAt = now + 2f;
            }

            return;
        }

        if (now < _nextCheck) return;
        _nextCheck = now + CheckEvery;
        string answer;
        try
        {
            answer = GameLua.Query(LookScript.Replace("@ID@", _current.ToString()));
        }
        catch (Exception)
        {
            answer = "x";
        }

        if (answer.Length == 0)
        {
            if (now - _since <= GiveUpAfter) return;
            answer = "x"; // no answer in time: given up
        }

        Send(CommandAction.PartyScores, "", _current + "|" + (answer == "x" ? Unknown + "|1" : answer));
        _current = 0;
    }

    static void Deal(string request)
    {
        var colon = request.IndexOf(':');
        if (colon <= 0) return;
        var kind = request[..colon];
        var applyId = request[(colon + 1)..];
        if (kind is not ("accept" or "reject") || applyId.Length == 0 || !IsPlain(applyId)) return;
        string answer;
        try
        {
            answer = GameLua.Query(DealScript.Replace("@ID@", applyId).Replace("@AGREE@", kind == "accept" ? "true" : "false"));
        }
        catch (Exception)
        {
            answer = "failed";
        }

        Send(CommandAction.PartyDealt, applyId, answer == "ok" ? "ok" : "failed");
    }

    /// <summary>An application id is letters, digits and the colon and hyphen the game uses ("p:564505997:43216926"): it is written into a Lua string, so nothing else gets in.</summary>
    static bool IsPlain(string text)
    {
        foreach (var c in text)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is ':' or '-' or '_')) return false;
        return true;
    }

    static void Send(string action, string key, string value) =>
        _link?.Send(new Envelope { T = MessageType.Cmd, Cmd = new CommandDto { Window = Window, Action = action, Key = key, Value = value } });

    // The game's request for a character's data (social_vm.AsyncGetSocialData(0, charId, cancelToken), the one the character card uses): the answer lands in a
    // table on the module, which LookScript reads: score|shown (shown 0 = the player keeps the score private), x = no data, nothing = still on its way.
    // The game's flag is called isShow but it is the other way round: with it true the game's character card writes "마스터 점수: 숨기기" instead of the number
    // (measured by switching the flag on a card), so a private score is isShow true.
    const string StartScript = @"
local sv = package.loaded['ui.view_model.social_vm']
if not sv then error('the social view model is not loaded') end
local box = sv.__starOverlayMaster
if not box then box = {} sv.__starOverlayMaster = box end
box[@ID@] = nil
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  local ok, data = pcall(sv.AsyncGetSocialData, 0, @ID@, token)
  local m = ok and data and data.masterModeDungeonData
  box[@ID@] = m and { score = m.seasonScore or 0, shown = m.isShow and 0 or 1 } or false
  pcall(function() source:Recycle() end)
end)
local ok, err = coroutine.resume(co)
if not ok then error(tostring(err)) end
";

    const string LookScript = @"
local sv = package.loaded['ui.view_model.social_vm']
local box = sv and sv.__starOverlayMaster
local r = box and box[@ID@]
if r == nil then return '' end
if r == false then return 'x' end
return r.score .. '|' .. r.shown
";

    // The game's answer to an application: team_vm.AsyncDealApplyJoin(applyId, agree, cancelToken) (seen in the game when an application was accepted).
    const string DealScript = @"
local vm = package.loaded['ui.view_model.team_vm']
if not vm then return 'failed' end
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  vm.AsyncDealApplyJoin('@ID@', @AGREE@, token)
  pcall(function() source:Recycle() end)
end)
local ok, err = coroutine.resume(co)
if not ok then return 'failed' end
return 'ok'
";
}
