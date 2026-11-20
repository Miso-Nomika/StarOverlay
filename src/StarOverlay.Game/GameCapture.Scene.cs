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
    void OnEnter(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var info = EnterScene.Parser.ParseFrom(payload).EnterSceneInfo;
            if (info == null) return;
            var step = SceneStep.Same;
            var arrived = false;
            lock (_gate)
            {
                var guidBefore = _sceneGuid;
                if (info.SceneAttrs != null) step = ReadScene(info.SceneAttrs);
                if (!string.IsNullOrEmpty(info.SceneGuid)) _sceneGuid = info.SceneGuid;
                arrived = step == SceneStep.Reset;
                if (arrived && _entered)
                    BeginFreshRoster(awaitSelf: false);
                var player = info.PlayerEnt;
                if (player != null && player.Uuid != 0)
                {
                    _self = player.Uuid;
                    AdmitSelf(player.Uuid);
                    var fighter = Take(player.Uuid);
                    if (player.Attrs?.Attrs != null) ReadAttrs(fighter, player.Attrs.Attrs, player.Uuid);
                }

                _seen = true;
                _entered = true;
                RefreshImagineSpecs();
            }

            if (arrived)
            {
                Publish();
                PublishLocation();
                LiveCombat.NoteMap();
            }
            else if (step == SceneStep.Stay)
            {
                Publish();
                PublishLocation();
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnScene(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var attrs = SyncSceneAttrs.Parser.ParseFrom(payload).Attrs;
            if (attrs == null) return;
            SceneStep step;
            int sceneId;
            lock (_gate)
            {
                step = ReadScene(attrs);
                sceneId = _sceneId;
            }

            if (step == SceneStep.Same) return;
            Publish();
            PublishLocation();
            if (step == SceneStep.Reset)
            {
                LiveCombat.NoteScene(sceneId);
                LiveCombat.NoteMap(); // the map id may be the same (channel move): the meter must still start fresh
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnSwitchScene(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var transfer = NotifySwitchScene.Parser.ParseFrom(payload).Request?.TransferParam;
            if (transfer != null) NoteTransfer(transfer);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnTeleport(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var transfer = Teleport.Parser.ParseFrom(payload).TeleportParams?.TransferParams;
            if (transfer != null) NoteTransfer(transfer);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnLeaveTeam(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var request = GrpcTeamNtf.Types.NotifyLeaveTeam.Parser.ParseFrom(payload).VRequest;
            if (request == null || request.CharId == 0) return;
            var mine = false;
            var inside = false;
            lock (_gate)
            {
                var selfId = _self >> 16;
                mine = selfId != 0 && request.CharId == selfId;
                inside = _dungeonState != EDungeonState.DungeonStateNull || InInstance();
            }

            if (mine && inside) ForgetRoster();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void NoteTransfer(TransferParam transfer)
    {
        var changed = false;
        lock (_gate)
        {
            var sceneId = transfer.SceneId;
            var guid = transfer.SceneGuid ?? "";
            if (guid.Length > 0) _sceneGuid = guid;
            if (sceneId <= 0 || sceneId == _sceneId) return;
            var step = MoveScene(sceneId);
            changed = step == SceneStep.Reset;
        }

        Publish();
        PublishLocation();
        if (changed) LiveCombat.NoteMap();
    }

    void OnContainer(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var sceneId = (int)(SyncContainerData.Parser.ParseFrom(payload).VData?.SceneData?.LevelMapId ?? 0);
            var loaded = false;
            lock (_gate)
            {
                _seen = true;
                loaded = AcceptMapLoad(sceneId);
            }

            Publish();
            PublishLocation();
            if (loaded) LiveCombat.NoteMap();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    // SyncContainerData marks a real map load. The roster is cleared only when its scene id
    // changes, then later packets fill the new party. A repeat of the same scene must not
    // wipe people who have already appeared.
    bool _loadedOnce; // the first container data of a session is the login, not a move

    bool AcceptMapLoad(int sceneId)
    {
        var continued = _masterScenes.Contains(_sceneId) && (sceneId <= 0 || _masterScenes.Contains(sceneId));
        var changed = false;
        if (sceneId > 0 && sceneId != _sceneId)
        {
            changed = _sceneId > 0 && !continued;
            _sceneId = sceneId;
            _sceneName = _scenes.TryGetValue(_sceneId, out var name) ? name : "";
            if (!continued)
                _dungeonState = EDungeonState.DungeonStateNull;
        }

        if (!changed)
        {
            // The same map loads again: the player changed channel (the original ZDPS starts a new battle on every SyncContainerData
            // for this reason; it is sent when a load begins, never for a teleport). A master dungeon keeps its fight going.
            if (!_loadedOnce || _sceneId <= 0 || _masterScenes.Contains(_sceneId))
            {
                _loadedOnce = true;
                return false;
            }

            BeginFreshRoster(awaitSelf: false);
            _freshRoster = false;
            _dungeonState = EDungeonState.DungeonStateNull;
            return true;
        }

        _loadedOnce = true;
        BeginFreshRoster(awaitSelf: false);
        _freshRoster = false;
        _dungeonState = EDungeonState.DungeonStateNull;
        return true;
    }

    void OnContainerDirty(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var stream = SyncContainerDirtyData.Parser.ParseFrom(payload).VData;
            if (stream == null || stream.Buffer.IsEmpty) return;
            var parsed = new BPSR_ZDPSLib.Blobs.CharSerialize(new BlobReader(
                stream.Buffer.ToByteArray(),
                stream.StreamType == EStreamType.StreamTypeDeltaDirtySafe));
            var sceneId = (int)(parsed.SceneData?.LevelMapId ?? 0);
            var step = AdoptScene(sceneId);
            if (step == SceneStep.Same) return;
            Publish();
            PublishLocation();
            if (step == SceneStep.Reset)
                LiveCombat.NoteMap();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    SceneStep AdoptScene(int sceneId)
    {
        lock (_gate)
        {
            _seen = true;
            return MoveScene(sceneId);
        }
    }

    void ForgetRoster()
    {
        lock (_gate) ClearRoster();
        Publish();
        PublishLocation();
        LiveCombat.NoteMap();
    }

    void ClearRoster()
    {
        BeginFreshRoster(awaitSelf: true);
        _dungeonState = EDungeonState.DungeonStateNull;
    }

    void BeginFreshRoster(bool awaitSelf)
    {
        _fighters.Clear();
        _fighting.Clear();
        _monsters.Clear();
        LiveCombat.CancelScheduledEnd();
        _freshRoster = true;
        _awaitSelf = awaitSelf;
        _clearedTargets.Clear();
        _openTargets.Clear();
        _targetNums.Clear();
    }

    enum SceneStep { Same, Stay, Reset }

    SceneStep MoveScene(int sceneId)
    {
        if (sceneId <= 0 || sceneId == _sceneId) return SceneStep.Same;
        var previous = _sceneId;
        var continued = previous > 0 && _masterScenes.Contains(previous) && _masterScenes.Contains(sceneId);
        var leaving = !continued && (previous > 0 || _fighters.Count > 0);
        if (leaving)
            BeginFreshRoster(awaitSelf: true);
        _sceneId = sceneId;
        _sceneName = _scenes.TryGetValue(_sceneId, out var name) ? name : "";
        if (!continued)
            _dungeonState = EDungeonState.DungeonStateNull;
        return leaving ? SceneStep.Reset : SceneStep.Stay;
    }

    bool TrackPlayer(long uuid) => uuid != 0;

    void AdmitSelf(long uuid)
    {
        if (!_awaitSelf || uuid == 0 || _self == 0 || uuid != _self) return;
        _fighters.Clear();
        _fighting.Clear();
        _awaitSelf = false;
        _freshRoster = false;
    }

    bool InInstance()
    {
        var name = SceneName();
        return _masterScenes.Contains(_sceneId) || _raidScenes.Contains(_sceneId) || _vaultScenes.Contains(_sceneId) || IsVault(name);
    }

    void OnDungeon(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var data = SyncDungeonData.Parser.ParseFrom(payload).VData;
            if (data == null) return;
            if (data.FlowInfo != null) NoteDungeon(data.FlowInfo.State);
            if (data.Target?.TargetData == null) return;
            foreach (var target in data.Target.TargetData)
                NoteTrashClear(target.Key, target.Value.TargetId, target.Value.Nums, target.Value.Complete, live: false);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    void OnDungeonDirty(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var stream = SyncDungeonDirtyData.Parser.ParseFrom(payload).VData;
            if (stream == null || stream.Buffer.IsEmpty) return;
            var parsed = new BPSR_ZDPSLib.Blobs.DungeonDirtyData(new BlobReader(
                stream.Buffer.ToByteArray(),
                stream.StreamType == EStreamType.StreamTypeDeltaDirtySafe));
            if (parsed.FlowInfo?.State is { } state) NoteDungeon(state);
            if (parsed.Target?.TargetData == null) return;
            foreach (var target in parsed.Target.TargetData)
                NoteTrashClear(target.Key, target.Value.TargetId ?? 0, target.Value.Nums ?? 0, target.Value.Complete ?? 0, live: true);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Interlocked.Increment(ref _errors);
        }
    }

    DateTime _deadSince;
    bool _wipeClosed;

    /// <summary>
    /// A wipe: everyone who took part in the fight (the player and everyone who did damage) is dead, and stays dead for a moment.
    /// The game sends no dungeon-end signal for it, so the fight is closed here: the meter starts from zero when the party is back.
    /// (The original ZDPS finds a wipe from the player's state pattern dead - teleport and the bosses' health; this looks at the
    /// party directly, which also fits solo and raid fights.)
    /// </summary>
    void CheckWipe()
    {
        var close = false;
        lock (_gate)
        {
            if (!LiveCombat.Recordable)
            {
                _deadSince = default;
                _wipeClosed = false;
                return;
            }

            var taking = _fighters.Where(pair => pair.Key == _self || pair.Value.Damage > 0).Select(pair => pair.Value).ToList();
            var allDead = taking.Any(fighter => fighter.Damage > 0) && taking.All(fighter => fighter.Dead);
            if (!allDead)
            {
                _deadSince = default;
                _wipeClosed = false;
                return;
            }

            if (_deadSince == default)
            {
                _deadSince = DateTime.UtcNow;
                return;
            }

            if (!_wipeClosed && (DateTime.UtcNow - _deadSince).TotalSeconds >= WipeStayDead.TotalSeconds)
            {
                _wipeClosed = true;
                close = true;
            }
        }

        if (!close) return;
        lock (_gate) CloseFightByWipe("everyone who fought is dead", TimeSpan.Zero); // the three seconds were already spent staying dead
    }

    internal void NoteDungeon(EDungeonState state)
    {
        var close = false;
        lock (_gate)
        {
            close = state == EDungeonState.DungeonStateEnd && _dungeonState != state && LiveCombat.Recordable;
            _dungeonState = state;
        }

        if (close)
            EndEncounter(EncounterEndDelay); // the dungeon end signal clears the meter three seconds later, not at the same instant
    }

    void NoteTrashClear(int key, int targetId, int nums, int complete, bool live)
    {
        if (targetId <= 0) targetId = key;
        if (targetId <= 0 || targetId is 3010101 or 3010102) return;
        var schedule = false;
        lock (_gate)
        {
            if (!_masterScenes.Contains(_sceneId)) return;
            if (nums > 0)
                _targetNums[targetId] = nums;
            else if (complete == 1 && _targetNums.TryGetValue(targetId, out var seen))
                nums = seen;
            if (complete == 0 && nums > 0)
                _openTargets.Add(targetId);
            if (complete != 1 || nums <= 0) return;
            if (_clearedTargets.Contains(targetId)) return;
            var progressed = _openTargets.Contains(targetId);
            var fought = _fighters.Values.Any(fighter => fighter.Damage > 0);
            if (!fought || (!live && !progressed))
            {
                if (!live && !progressed) _clearedTargets.Add(targetId);
                return;
            }

            _clearedTargets.Add(targetId);
            schedule = true;
        }

        if (schedule)
        {
            LiveCombat.ScheduleEncounterEnd(TimeSpan.FromSeconds(3));
        }
    }

    SceneStep ReadScene(AttrCollection attrs)
    {
        var step = SceneStep.Same;
        if (attrs.Attrs == null) return step;
        foreach (var attr in attrs.Attrs)
        {
            if (attr.Id == 0 || attr.RawData == null || attr.RawData.Length == 0) continue;
            var reader = new CodedInputStream(attr.RawData.ToByteArray());
            switch ((EAttrType)attr.Id)
            {
                case EAttrType.AttrSceneBasicId:
                    var next = MoveScene((int)reader.ReadUInt32());
                    if (next != SceneStep.Same) step = next;
                    break;
                case EAttrType.AttrSceneChannel:
                    _channel = (int)reader.ReadUInt32();
                    break;
            }
        }

        return step;
    }

    string FormatPlace()
    {
        if (_sceneName.Length == 0 && _sceneId > 0 && _scenes.TryGetValue(_sceneId, out var loaded))
            _sceneName = loaded;
        var parts = new List<string>();
        if (_sceneName.Length > 0) parts.Add(LocalizeScene(_scenesKo, _sceneId, _sceneName));
        else if (_sceneId > 0) parts.Add($"지역 {_sceneId}");
        if (_self != 0 && _fighters.TryGetValue(_self, out var self) && self.HasPos)
            parts.Add($"X {self.X:0}  Z {self.Z:0}");
        return string.Join("  ·  ", parts);
    }

    void PublishLocation()
    {
        string title;
        bool recordable;
        bool master;
        lock (_gate)
        {
            var name = SceneName();
            master = _masterScenes.Contains(_sceneId);
            recordable = IsGuildScene(_sceneId, name) || IsVault(name) || master || _raidScenes.Contains(_sceneId) || _vaultScenes.Contains(_sceneId);
            title = LocationTitle(name);
        }

        LiveCombat.SetLocation(title, recordable);
        LiveCombat.SetPreserveClock(master);
    }

    string SceneName()
    {
        if (_sceneName.Length == 0 && _sceneId > 0 && _scenes.TryGetValue(_sceneId, out var loaded))
            _sceneName = loaded;
        return _sceneName;
    }

    string LocationTitle(string name) =>
        ComposeTitle(name, LocalizeScene(_scenesKo, _sceneId, name), _masterScenes.Contains(_sceneId), _raidScenes.Contains(_sceneId), IsVault(name) || _vaultScenes.Contains(_sceneId), IsGuildScene(_sceneId, name));

    /// <summary>The kind of place plus its name. <paramref name="english"/> decides (guild); <paramref name="shown"/> is what the player reads.</summary>
    internal static string ComposeTitle(string english, string shown, bool master, bool raid, bool vault, bool guild = false)
    {
        if (guild || IsGuild(english)) return "길드 센터";
        if (master) return shown.Length == 0 ? "마스터 극한공간" : "마스터 극한공간 · " + shown;
        if (raid) return shown.Length == 0 ? "레이드" : "레이드 · " + shown;
        if (vault)
        {
            if (shown.Length == 0) return "시디문 유적";
            return shown.Contains("시디문", StringComparison.Ordinal) ? shown : "시디문 유적 · " + shown; // "시디문 유적 5층" already says it
        }

        return shown;
    }

    /// <summary>The guild center ("Guild Center" in the old table, "길드 센터" before season 4, "길드 이벤트 센터" now) is scene 12000.</summary>
    const int GuildCenterSceneId = 12000;

    internal static bool IsGuildScene(int sceneId, string name) => sceneId == GuildCenterSceneId || IsGuild(name);

    static bool IsGuild(string name) =>
        name.Equals("Guild Center", StringComparison.OrdinalIgnoreCase)
        || name.Contains("길드 센터", StringComparison.Ordinal)
        || name.Contains("길드 이벤트 센터", StringComparison.Ordinal);

    static bool IsVault(string name) =>
        name.Contains("Stimen Vaults", StringComparison.OrdinalIgnoreCase)
        || name.Contains("시디문", StringComparison.Ordinal);

}
