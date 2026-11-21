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
                _masterDifficulty = 0;
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
            _masterDifficulty = 0;
            return true;
        }

        _loadedOnce = true;
        BeginFreshRoster(awaitSelf: false);
        _freshRoster = false;
        _dungeonState = EDungeonState.DungeonStateNull;
        _masterDifficulty = 0;
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
        _sectionCleared = false;
        _vaultCleared = false;
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

    void OnDungeon(ReadOnlySpan<byte> payload, ExtraPacketData _)
    {
        try
        {
            var data = SyncDungeonData.Parser.ParseFrom(payload).VData;
            if (data == null) return;
            if (data.FlowInfo != null) NoteDungeon(data.FlowInfo.State);
            if (data.DungeonSceneInfo is { Difficulty: > 0 } sceneInfo) NoteDifficulty(sceneInfo.Difficulty);
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

    /// <summary>The server told the difficulty of the dungeon we are in (master 1-6): the place may now count as recorded or not.</summary>
    void NoteDifficulty(int difficulty)
    {
        lock (_gate)
        {
            if (_masterDifficulty == difficulty) return;
            _masterDifficulty = difficulty;
        }

        PublishLocation();
    }

    internal void NoteDungeon(EDungeonState state)
    {
        var close = false;
        var kind = EncounterEndKind.DungeonEnd;
        lock (_gate)
        {
            close = state == EDungeonState.DungeonStateEnd && _dungeonState != state && LiveCombat.Encounters;
            _dungeonState = state;
            // a Stimen Vault ends both when every enemy is down and when its time runs out: the record tells which
            if (_vaultScenes.Contains(_sceneId)) kind = _vaultCleared ? EncounterEndKind.VaultClear : EncounterEndKind.VaultFail;
        }

        if (close)
            EndEncounter(EncounterEndDelay, kind); // the dungeon end signal clears the meter three seconds later, not at the same instant
    }

    void NoteTrashClear(int key, int targetId, int nums, int complete, bool live)
    {
        if (targetId <= 0) targetId = key;
        if (targetId > 0 && complete == 1 && _killAllTargets.Contains(targetId))
        {
            var cleared = false;
            lock (_gate)
            {
                if (_vaultScenes.Contains(_sceneId)) cleared = _vaultCleared = true; // the floor is cleared: the end of the vault is a success
            }

            // the server sends the vault's end first and this goal a moment later: an end already waiting is told as a success
            if (cleared) LiveCombat.ReplacePendingEnd(EncounterEndKind.VaultFail, EncounterEndKind.VaultClear);
        }

        if (targetId <= 0 || targetId is 3010101 or 3010102) return;
        var schedule = false;
        lock (_gate)
        {
            if (!_masterScenes.Contains(_sceneId) && !_extremeHardScenes.Contains(_sceneId) && !_guildHuntScenes.Contains(_sceneId)) return;
            // only the "조사를 완료" goal ends the trash section, and only once per visit (some places hold a second goal of the same kind)
            if (_sectionCleared || (_surveyTargets.Count > 0 && !_surveyTargets.Contains(targetId))) return;
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
            _sectionCleared = true;
            schedule = true;
        }

        if (schedule)
        {
            LiveCombat.ScheduleEncounterEnd(TimeSpan.FromSeconds(3), EncounterEndKind.SectionClear);
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
        if (_sceneName.Length > 0) parts.Add(LocationTitle(_sceneName)); // the same title as the records: "극한공간 하드 · 거울 속의 심판", "레이드 · ..."
        else if (_sceneId > 0) parts.Add($"지역 {_sceneId}");
        if (_self != 0 && _fighters.TryGetValue(_self, out var self) && self.HasPos)
            parts.Add($"X {self.X:0}  Z {self.Z:0}");
        return string.Join("  ·  ", parts);
    }

    void PublishLocation()
    {
        string title;
        bool recordable;
        bool encounters;
        lock (_gate)
        {
            var name = SceneName();
            var guildCenter = IsGuildScene(_sceneId, name);
            var vault = IsVault(name) || _vaultScenes.Contains(_sceneId);
            var master = _masterScenes.Contains(_sceneId);
            var raid = _raidScenes.Contains(_sceneId);
            var extremeHard = _extremeHardScenes.Contains(_sceneId);
            var guildHunt = _guildHuntScenes.Contains(_sceneId);
            recordable = IsRecordable(guildCenter, vault, master, _masterDifficulty, raid, extremeHard, guildHunt, LiveCombat.RecordExtra, VaultFloor(name), LiveCombat.VaultFifthOnly);
            encounters = IsRecordable(guildCenter, vault, master, _masterDifficulty, raid, extremeHard, guildHunt, recordMore: true); // the places that can be recorded all have encounters
            title = LocationTitle(name);
        }

        LiveCombat.SetLocation(title, recordable, encounters);
    }

    int _masterDifficulty; // the difficulty (1-6) of the master dungeon we are in, 0 while it is not known

    const int MasterEasiest = 1;
    const int MasterHardest = 6;

    /// <summary>
    /// Whether the fights of a place are recorded. By default: the guild center, the vault, the raids and the master dungeons except master 1 and 6.
    /// With the "record more" choice also master 1 and 6, the hard extreme space and the guild hunts. A master dungeon whose difficulty is
    /// not known yet counts as recorded (nothing is lost if the packet that says it is missed).
    /// </summary>
    internal static bool IsRecordable(bool guildCenter, bool vault, bool master, int masterDifficulty, bool raid, bool extremeHard, bool guildHunt, bool recordMore, int vaultFloor = 0, bool vaultFifthOnly = false)
    {
        // the vault is recorded on every floor, or (by choice) only on 5, 10, 15...; a floor that is not known counts as recorded
        if (vault) return !vaultFifthOnly || vaultFloor <= 0 || vaultFloor % 5 == 0;
        if (guildCenter || raid) return true;
        if (master) return recordMore || masterDifficulty is not (MasterEasiest or MasterHardest);
        return recordMore && (extremeHard || guildHunt);
    }

    /// <summary>The floor number in a vault place name ("시디문 유적 5층" → 5); 0 when the name has none.</summary>
    internal static int VaultFloor(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name ?? "", @"(\d+)\s*층");
        return match.Success && int.TryParse(match.Groups[1].Value, out var floor) ? floor : 0;
    }

    string SceneName()
    {
        if (_sceneName.Length == 0 && _sceneId > 0 && _scenes.TryGetValue(_sceneId, out var loaded))
            _sceneName = loaded;
        return _sceneName;
    }

    string LocationTitle(string name) =>
        ComposeTitle(name, LocalizeScene(_scenesKo, _sceneId, name), _masterScenes.Contains(_sceneId), _raidScenes.Contains(_sceneId), IsVault(name) || _vaultScenes.Contains(_sceneId), IsGuildScene(_sceneId, name), _masterDifficulty, _extremeKinds.GetValueOrDefault(_sceneId, ""));

    /// <summary>The kind of place plus its name. <paramref name="english"/> decides (guild); <paramref name="shown"/> is what the player reads.</summary>
    internal static string ComposeTitle(string english, string shown, bool master, bool raid, bool vault, bool guild = false, int masterDifficulty = 0, string extremeLevel = "")
    {
        if (guild || IsGuild(english)) return "길드 센터";
        if (master)
        {
            var kind = masterDifficulty > 0 ? "마스터 극한공간 · 난이도 " + masterDifficulty : "마스터 극한공간";
            return shown.Length == 0 ? kind : kind + " · " + shown;
        }

        if (raid) return shown.Length == 0 ? "레이드" : "레이드 · " + shown;
        if (vault)
        {
            if (shown.Length == 0) return "시디문 유적";
            return shown.Contains("시디문", StringComparison.Ordinal) ? shown : "시디문 유적 · " + shown; // "시디문 유적 5층" already says it
        }

        // the extreme space: the normal and the hard level have the same scene name, so the level is put in front of it
        // (the place names start with "극한" themselves, which the prefix already says)
        if (extremeLevel.Length > 0)
        {
            var place = shown.StartsWith("극한", StringComparison.Ordinal) ? shown[2..].TrimStart(' ', '・', '·') : shown;
            return place.Length == 0 ? "극한공간 " + extremeLevel : "극한공간 " + extremeLevel + " · " + place;
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
