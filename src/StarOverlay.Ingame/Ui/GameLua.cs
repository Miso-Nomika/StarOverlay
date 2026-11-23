using System;
using LuaInterface;

namespace StarOverlay.Ingame.Ui;

/// <summary>The game's screens are written in Lua. This runs a short piece of Lua in the game's own Lua state (to call one of the game's functions).</summary>
static class GameLua
{
    /// <summary>Runs the script in the game's main Lua state and gives back what it returns (a string; empty when it returns nothing). Throws when the game's Lua is not up or the script fails.</summary>
    internal static string Query(string script)
    {
        var state = LuaClient.GetMainState() ?? throw new InvalidOperationException("the game's Lua is not up");
        return state.DoString<string>(script, "StarOverlay") ?? "";
    }

    /// <summary>Runs the script in the game's main Lua state. Throws when the game's Lua is not up or the script fails.</summary>
    internal static void Execute(string script)
    {
        var state = LuaClient.GetMainState() ?? throw new InvalidOperationException("the game's Lua is not up");
        state.DoString(script, "StarOverlay");
    }
}
