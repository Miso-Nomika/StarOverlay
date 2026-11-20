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
    void WriteLiveStatus()
    {
        int players;
        long damage;
        string place;
        lock (_gate)
        {
            players = _fighters.Count;
            damage = _fighters.Values.Sum(fighter => fighter.Damage);
            place = FormatPlace();
        }

        LiveCombat.SetPlace(place);
        PublishLocation();
        if ((DateTime.UtcNow - _statusAt).TotalSeconds < 1) return;
        _statusAt = DateTime.UtcNow;

        var packets = _cap?.NumSeenPackets ?? 0;
        var messages = _cap?.NumGameMessagesSeen ?? 0;
        var reassembler = _cap?.TcpReassempler;
        var text = $"수신 {packets}패킷 {messages}메시지 / 플레이어 {players} / 피해 {damage}";
        if (reassembler != null) text += $" / 연결 시작부터 {reassembler.NumSyncedFromStart} 중간부터 {reassembler.NumSyncedByShape} 동기화 전 버림 {reassembler.NumUnsyncedDrops}";
        if (_errors > 0 && _lastError.Length > 0) text += " / " + _lastError;
        LiveCombat.SetNote(text);
        WriteStatus($"{_device} / {text}");
    }

    void WriteStatus(string text)
    {
        try
        {
            var folder = Path.Combine(_game, "bpsr", "state");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "capture.txt"), text, new UTF8Encoding(false));
        }
        catch (Exception)
        {
        }
    }

}
