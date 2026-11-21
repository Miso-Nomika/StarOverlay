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

public sealed partial class GameCapture : IDisposable
{
    readonly string _game;
    readonly object _gate = new();
    readonly Dictionary<long, Fighter> _fighters = [];
    readonly Dictionary<long, Monster> _monsters = [];
    readonly ImagineCatalog _imagineCatalog = new();
    readonly HashSet<long> _fighting = [];
    readonly HashSet<int> _clearedTargets = [];
    readonly HashSet<int> _openTargets = [];
    bool _sectionCleared;
    bool _vaultCleared;
    readonly Dictionary<int, int> _targetNums = [];
    int _combatArm;
    long _self;
    bool _seen;
    bool _entered;
    bool _awaitSelf;
    bool _freshRoster;
    int _appear;
    int _delta;
    int _hits;
    int _errors;
    int _sceneId;
    string _sceneGuid = "";
    EDungeonState _dungeonState;
    int _channel;
    string _sceneName = "";
    string _device = "";
    string _lastError = "";
    DateTime _statusAt = DateTime.MinValue;
    NetCap? _cap;
    SpecStore? _specStore;
    System.Threading.Timer? _timer;

    readonly IReadOnlyList<IPacketSource> _sources;
    IPacketSource? _source;

    public GameCapture(string game, IReadOnlyList<IPacketSource> sources)
    {
        _game = game;
        _dumpRoot = Path.Combine(game, "tables");
        _sources = sources;
    }

    /// <summary>Name of the source in use (pipe, replay); empty before Start succeeds.</summary>
    public string SourceName => _source?.Name ?? "";

    /// <summary>The engine behind the source; tests read its counters.</summary>
    internal NetCap? Engine => _cap;

    /// <summary>Packets and game messages the engine has seen so far (status line, diagnostics).</summary>
    public (ulong Packets, ulong Messages, ulong Parsed) Counters =>
        (_cap?.NumSeenPackets ?? 0, _cap?.NumGameMessagesSeen ?? 0, _cap?.NumGameMessagesDequeued ?? 0);

    public void Start()
    {
        try
        {
            var cap = new NetCap();
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncNearEntities, OnAppear);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncNearDeltaInfo, OnDelta);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncToMeDeltaInfo, OnMe);
            cap.RegisterWorldNotifyHandler(WorldNtf.EnterScene, OnEnter);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncSceneAttrs, OnScene);
            cap.RegisterWorldNotifyHandler(WorldNtf.NotifySwitchScene, OnSwitchScene);
            cap.RegisterWorldNotifyHandler(WorldNtf.Teleport, OnTeleport);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncContainerData, OnContainer);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncContainerDirtyData, OnContainerDirty);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncDungeonData, OnDungeon);
            cap.RegisterWorldNotifyHandler(WorldNtf.SyncDungeonDirtyData, OnDungeonDirty);
            cap.RegisterWorldNotifyHandler(WorldNtf.NotifyShowItems, OnShowItems);
            cap.RegisterNotifyHandler((ulong)EServiceId.ChitChatNtf, (uint)ChatNtf.NotifyNewestChitChatMsgs, OnChat);
            cap.RegisterNotifyHandler((ulong)EServiceId.GrpcTeamNtf, (uint)TeamNtf.NotifyApplyJoin, OnApplyJoin);
            cap.RegisterNotifyHandler((ulong)EServiceId.GrpcTeamNtf, (uint)TeamNtf.NotifyTeamJoinApplyChanged, OnApplyChanged);
            cap.RegisterProxyHandler((uint)EProxyServiceId.World, (uint)WorldCall.DealApplyJoin, OnDealApply);
            cap.RegisterProxyHandler((uint)EProxyServiceId.World, (uint)WorldCall.DenyAllApplyJoin, OnDenyAllApply);
            cap.RegisterProxyHandler((uint)EProxyServiceId.ChitChat, (uint)ChatCall.GetChipChatRecords, OnChatHistoryCall);
            cap.RegisterProxyHandler((uint)EProxyServiceId.ChitChat, (uint)ChatCall.SetWorldChatChannelId, OnWorldChannelSetCall);
            cap.RegisterProxyReturnHandler((uint)EProxyServiceId.ChitChat, (uint)ChatCall.GetChipChatRecords, OnChatHistoryReturn);
            cap.RegisterProxyReturnHandler((uint)EProxyServiceId.ChitChat, (uint)ChatCall.GetWorldChatChannelId, OnWorldChannelGet);
            cap.RegisterProxyReturnHandler((uint)EProxyServiceId.ChitChat, (uint)ChatCall.SetWorldChatChannelId, OnWorldChannelSet);
            LoadImagineCatalog();
            OpenSpecStore();
            StartSource(cap);
            _cap = cap;
            _timer = new System.Threading.Timer(_ =>
            {
                Publish();
                WriteLiveStatus();
            }, null, 100, 100);
            Task.Run(() =>
            {
                LoadNames();
                lock (_gate)
                {
                    foreach (var monster in _monsters.Values)
                        if (monster.Dead) FinishBoss(monster);
                }
            });
        }
        catch (Exception ex)
        {
        }
    }

    void StartSource(NetCap cap)
    {
        var failures = new List<string>();
        foreach (var source in _sources)
        {
            try
            {
                source.Start(cap);
                _source = source;
                _device = $"[{source.Name}] {source.Label}";
                return;
            }
            catch (Exception ex)
            {
                failures.Add($"{source.Name}: {ex.Message}");
                try
                {
                    source.Stop();
                }
                catch (Exception)
                {
                }
            }
        }

        throw new InvalidOperationException(string.Join(" / ", failures));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        try
        {
            _cap?.Stop();
        }
        catch (Exception)
        {
        }

        try
        {
            _source?.Stop();
        }
        catch (Exception)
        {
        }

        LiveCombat.Clear();
        LiveParty.Clear();
        LiveChat.Clear();
        _specStore?.Dispose();
    }

    Fighter Take(long uuid)
    {
        if (!_fighters.TryGetValue(uuid, out var fighter))
        {
            fighter = new Fighter();
            _fighters[uuid] = fighter;
        }

        return fighter;
    }

    void Publish()
    {
        LiveFighter[] rows;
        var changed = new List<(long Id, string Spec, bool Check, long FightPoint, bool FarSea, int Season)>();
        var removed = new List<long>();
        lock (_gate)
        {
            if (!_seen) return;
            RefreshImagineSpecs();
            foreach (var pair in _fighters)
            {
                var fighter = pair.Value;
                if (fighter.SpecFromSkill) continue;
                var charId = pair.Key >> 16;
                if (!SpecBook.TryGet(charId, out var saved, out var savedCheck, out var savedFight) || !savedCheck) continue;
                var currentProfession = PartyJobs.ProfessionOf(fighter.Spec);
                var savedProfession = PartyJobs.ProfessionOf(saved);
                if (currentProfession != 0 && currentProfession != savedProfession && fighter.FightPoint > savedFight) continue;
                fighter.Spec = saved;
                fighter.SpecFromGear = false;
                fighter.SpecFromImagine = false;
                fighter.DropSpec = false;
            }

            rows = _fighters
                .Where(pair => pair.Value.Name.Length > 0 || pair.Value.Damage > 0)
                .Select(pair => pair.Value.ToLive(pair.Key, pair.Key == _self, _skillNames, _buffMeta, _cooldowns, _imagines))
                .ToArray();
            foreach (var pair in _fighters)
            {
                var fighter = pair.Value;
                var charId = pair.Key >> 16;
                if (fighter.DropSpec)
                {
                    fighter.DropSpec = false;
                    if (charId == 0) continue;
                    SpecBook.Forget(charId);
                    LiveParty.Forget(charId);
                    removed.Add(charId);
                    continue;
                }

                if (charId == 0) continue;
                bool? farSea = fighter.EquipKnown ? IsFarSea(fighter.Weapon) : null;
                if (fighter.Spec.Length > 0)
                {
                    LiveParty.NoteSeen(charId, fighter.Spec);
                    var check = fighter.SpecFromSkill;
                    var classified = check || fighter.SpecFromGear || fighter.SpecFromImagine;
                    if ((classified || fighter.FightPoint > 0)
                        && SpecBook.Remember(charId, fighter.Spec, check, fighter.FightPoint, farSea)
                        && SpecBook.TryGet(charId, out var stored, out var storedCheck, out var storedFight, out var storedSea))
                        changed.Add((charId, stored, storedCheck, storedFight, storedSea, SpecBook.SeasonOf(charId)));
                }
                else if (farSea is not null
                    && SpecBook.TryGet(charId, out var known, out var knownCheck, out var knownFight, out _)
                    && SpecBook.Remember(charId, known, knownCheck, knownFight, farSea)
                    && SpecBook.TryGet(charId, out var stored, out var storedCheck, out var storedFight, out var storedSea))
                {
                    changed.Add((charId, stored, storedCheck, storedFight, storedSea, SpecBook.SeasonOf(charId)));
                }
            }
        }

        if (changed.Count > 0) _specStore?.Save(changed);
        if (removed.Count > 0) _specStore?.Delete(removed);
        LiveCombat.Publish(rows);
    }

}
