using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BPSR_ZDPSLib;
using StarOverlay.Core;
using StarOverlay.Framework;
using StarOverlay.Game.Packets;
using Google.Protobuf;
using Zproto;
using static Zproto.WorldNtfCsharp.Types;
using WorldNtf = BPSR_ZDPSLib.ServiceMethods.WorldNtf;
using TeamNtf = BPSR_ZDPSLib.ServiceMethods.GrpcTeamNtf;
using ChatNtf = BPSR_ZDPSLib.ServiceMethods.ChitChatNtf;
using WorldCall = BPSR_ZDPSLib.ServiceMethods.WorldProxy;

namespace StarOverlay.Game;

public sealed partial class GameCapture
{
    void OnApplyJoin(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var item = GrpcTeamNtf.Types.NotifyApplyJoin.Parser.ParseFrom(payload).VRequest?.Apply;
            if (item != null) RememberApply(item);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnApplyChanged(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var item = GrpcTeamNtf.Types.NotifyTeamJoinApplyChanged.Parser.ParseFrom(payload).VRequest?.Item;
            if (item != null) RememberApply(item);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnDealApply(ReadOnlySpan<byte> payload, uint _, ExtraPacketData __)
    {
        try
        {
            var applyId = ReadDealApplyId(payload);
            if (applyId.Length > 0) LiveParty.Remove(applyId);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnDenyAllApply(ReadOnlySpan<byte> payload, uint _, ExtraPacketData __)
    {
        try
        {
            if (TryReadDenyType(payload, out var applyType)) LiveParty.RemoveType(applyType);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    static string ReadDealApplyId(ReadOnlySpan<byte> payload)
    {
        var bytes = payload.ToArray();
        try
        {
            var applyId = World.Types.DealApplyJoin.Parser.ParseFrom(bytes).VRequest?.ApplyId;
            if (!string.IsNullOrEmpty(applyId)) return applyId;
        }
        catch (Exception)
        {
        }

        try
        {
            return DealApplyJoinRequest.Parser.ParseFrom(bytes).ApplyId ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    static bool TryReadDenyType(ReadOnlySpan<byte> payload, out int applyType)
    {
        applyType = 0;
        var bytes = payload.ToArray();
        try
        {
            var request = World.Types.DenyAllApplyJoin.Parser.ParseFrom(bytes).VRequest;
            if (request != null)
            {
                applyType = (int)request.ApplyType;
                return true;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            applyType = (int)DenyAllApplyJoinRequest.Parser.ParseFrom(bytes).ApplyType;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    void RememberApply(TeamJoinApplyListItem item)
    {
        var people = new List<PartyApplicant>();
        var seen = DateTime.UtcNow;
        if (item.ApplicantList.Count > 0)
        {
            foreach (var display in item.ApplicantList)
                AddApplicant(people, item, display, seen);
        }
        else if (item.ApplicantDisplay != null)
        {
            AddApplicant(people, item, item.ApplicantDisplay, seen);
        }

        var key = item.ApplyId.Length > 0 ? item.ApplyId : people.FirstOrDefault()?.CharId.ToString() ?? "";
        if (key.Length == 0) return;
        LiveParty.Replace(key, people.ToArray());
        AttachSeen(people);
    }

    void AttachSeen(List<PartyApplicant> people)
    {
        lock (_gate)
        {
            foreach (var person in people)
            {
                if (person.CharId == 0) continue;
                foreach (var pair in _fighters)
                {
                    if ((pair.Key >> 16) != person.CharId || pair.Value.Spec.Length == 0) continue;
                    LiveParty.NoteSeen(person.CharId, pair.Value.Spec);
                    break;
                }
            }
        }
    }

    static void AddApplicant(List<PartyApplicant> people, TeamJoinApplyListItem item, TeamApplyPlayerDisplay display, DateTime seen)
    {
        var name = display.Name?.Trim() ?? "";
        var charId = display.CharId != 0 ? display.CharId : item.ApplicantId;
        if (name.Length == 0 && charId == 0) return;
        if (people.Any(person => person.CharId == charId && charId != 0)) return;
        if (name.Length == 0) name = charId.ToString();
        people.Add(new PartyApplicant(
            item.ApplyId,
            charId,
            name,
            display.ProfessionId,
            PartyJobs.Base(display.ProfessionId),
            display.FightPoint,
            display.SeasonStrength,
            seen,
            (int)item.ApplyType,
            display.TalentId));
    }

}
