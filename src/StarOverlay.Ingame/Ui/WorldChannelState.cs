using System;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// How crowded the world chat channel the player is in is (the game's E.EWorldChannelState: 1 low, 2 hot, 3 high), read from the game's chat data
/// (chat_main_data:GetWorldChannelState). It is read when our chat window comes up, when the world channel changes, and every few seconds while the badge is
/// shown; one small call each time.
/// </summary>
static class WorldChannelState
{
    const float Every = 5f; // seconds between two reads
    const string Script = "return tostring(_G.Z.DataMgr.Get('chat_main_data'):GetWorldChannelState())";

    /// <summary>0 = not known, 1 = low (green), 2 = hot (orange), 3 = high (red).</summary>
    internal static int Level { get; private set; }

    static float _next;
    static int _numberSeen = -1;

    /// <summary>The colour of the bullet for the level (white-grey when not known).</summary>
    internal static Color Colour => Level switch
    {
        1 => new Color(0.36f, 0.86f, 0.5f, 1f),
        2 => new Color(1f, 0.66f, 0.3f, 1f),
        3 => new Color(1f, 0.4f, 0.4f, 1f),
        _ => new Color(0.5f, 0.52f, 0.58f, 1f),
    };

    /// <summary>Called while our chat window is shown; <paramref name="worldNumber"/> is the world channel now (a change of it is read again at once).</summary>
    internal static void Poll(int worldNumber)
    {
        if (worldNumber != _numberSeen)
        {
            _numberSeen = worldNumber;
            _next = 0f;
        }

        if (Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + Every;
        try
        {
            Level = int.TryParse(GameLua.Query(Script), out var level) && level is >= 1 and <= 3 ? level : 0;
        }
        catch (Exception)
        {
            Level = 0; // the game's chat data is not up yet: tried again in a few seconds
        }
    }
}
