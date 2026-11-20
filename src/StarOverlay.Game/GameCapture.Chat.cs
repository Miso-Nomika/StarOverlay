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
using ChatCall = BPSR_ZDPSLib.ServiceMethods.ChitChatProxy;
using WorldCall = BPSR_ZDPSLib.ServiceMethods.WorldProxy;

namespace StarOverlay.Game;

public sealed partial class GameCapture
{
    void OnChat(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            NotifyNewestChitChatMsgsRequest? request = null;
            try
            {
                request = Zproto.ChitChatNtf.Types.NotifyNewestChitChatMsgs.Parser.ParseFrom(payload).VRequest;
            }
            catch (Exception)
            {
            }

            if (request?.ChatMsg == null)
                request = NotifyNewestChitChatMsgsRequest.Parser.ParseFrom(payload);
            var message = request?.ChatMsg;
            if (message?.MsgInfo == null) return;
            var (text, links) = ChatItems.Compose(message.MsgInfo);
            var name = message.SendCharInfo?.Name ?? "";
            LiveChat.Add(message.MsgId, LiveChat.ChannelName((int)request!.ChannelType), name, text, links: links);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>
    /// The game wraps every request and answer of the chat service in one outer field (0A <length> <message>); the message inside is what
    /// the protobuf types describe. Anything not shaped like that is taken as it is.
    /// </summary>
    internal static ReadOnlySpan<byte> Unwrap(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2 || payload[0] != 0x0A) return payload;
        var length = 0;
        var shift = 0;
        var index = 1;
        while (index < payload.Length && index < 6)
        {
            var b = payload[index++];
            length |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return index + length == payload.Length ? payload.Slice(index, length) : payload;
            shift += 7;
        }

        return payload;
    }

    // ---- what the game asks the server when it enters or changes the world chat channel: the history and the channel number

    readonly System.Collections.Concurrent.ConcurrentDictionary<uint, int> _historyCalls = new();

    /// <summary>The game asks for the chat history of a channel: remember which channel the answer will be about.</summary>
    internal void OnChatHistoryCall(ReadOnlySpan<byte> payload, uint callId, ExtraPacketData _)
    {
        try
        {
            var request = GetChipChatRecordsRequest.Parser.ParseFrom(Unwrap(payload));
            if (_historyCalls.Count > 64) _historyCalls.Clear();
            _historyCalls[callId] = (int)request.ChannelType;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>The game shows items as gained (chest, drop, reward): they become one line of the "시스템" chat channel.</summary>
    void OnShowItems(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var info = Zproto.WorldNtf.Types.NotifyShowItems.Parser.ParseFrom(payload).VInfo;
            if (info == null) return;
            var text = ChatItems.LootText(info.Items.Select(item => ((int)item.ConfigId, (long)item.Count)));
            if (text.Length > 0) LiveChat.Add(-DateTime.UtcNow.Ticks, "시스템", "", text); // each gain is its own line, never a repeat of the one before
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>The server answers with the older lines of that channel: they join the chat in time order.</summary>
    internal void OnChatHistoryReturn(ReadOnlySpan<byte> payload, uint callId, ExtraPacketData _)
    {
        try
        {
            var reply = GetChipChatRecordsReply.Parser.ParseFrom(Unwrap(payload));
            if (reply.ErrCode != 0)
            {
                return;
            }

            _historyCalls.TryRemove(callId, out var type);
            var channel = LiveChat.ChannelName(type);
            var added = 0;
            foreach (var message in reply.MultiMsgList)
            {
                if (message?.MsgInfo == null) continue;
                if (LiveChat.Add(message.MsgId, channel, message.SendCharInfo?.Name ?? "", ChatItems.Compose(message.MsgInfo).Text, SaidAt(message.Timestamp), links: ChatItems.Compose(message.MsgInfo).Links)) added++;
            }

        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>The answer to "which world chat channel am I in?".</summary>
    internal void OnWorldChannelGet(ReadOnlySpan<byte> payload, uint callId, ExtraPacketData _)
    {
        try
        {
            var reply = GetWorldChatChannelIdReply.Parser.ParseFrom(Unwrap(payload));
            if (reply.ErrCode == 0) LiveChat.SetWorldChannel((int)Math.Min(reply.ChannelId, (uint)int.MaxValue));
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<uint, uint> _worldCalls = new();

    /// <summary>The game asks to move to another world channel: remember which, because the answer may not repeat it.</summary>
    internal void OnWorldChannelSetCall(ReadOnlySpan<byte> payload, uint callId, ExtraPacketData _)
    {
        try
        {
            var request = SetWorldChatChannelIdRequest.Parser.ParseFrom(Unwrap(payload));
            if (_worldCalls.Count > 16) _worldCalls.Clear();
            _worldCalls[callId] = request.ChannelId;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>The answer to "change my world chat channel": the new channel (the history of it follows).</summary>
    internal void OnWorldChannelSet(ReadOnlySpan<byte> payload, uint callId, ExtraPacketData _)
    {
        try
        {
            var reply = SetWorldChatChannelIdReply.Parser.ParseFrom(Unwrap(payload));
            _worldCalls.TryRemove(callId, out var asked);
            var channel = reply.ChannelId > 0 ? reply.ChannelId : asked; // the answer often carries only the state
            if (reply.ErrCode == 0 && channel > 0) LiveChat.SetWorldChannel((int)Math.Min(channel, (uint)int.MaxValue));
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    /// <summary>The game writes the time as Unix seconds (or milliseconds); null when it is missing.</summary>
    static DateTime? SaidAt(long stamp)
    {
        if (stamp <= 0) return null;
        try
        {
            return (stamp > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(stamp) : DateTimeOffset.FromUnixTimeSeconds(stamp)).LocalDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

}
