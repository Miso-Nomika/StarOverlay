using System;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Changes the world chat channel the way the game's own number pad does: it calls the game's Lua function chat_main_vm.AsyncChannelGroupSwitch with the channel
/// number (found by watching what the pad calls when its OK is pressed). The game does the change (and tells the server) itself.
/// </summary>
static class WorldChannelSwitch
{
    /// <summary>Asks the game to move to the world channel. Returns false when it could not be handed to the game.</summary>
    internal static bool Switch(int number)
    {
        if (number <= 0 || number > 9999) return false;
        try
        {
            GameLua.Execute(Script.Replace("@N@", number.ToString()));
            return true;
        }
        catch (Exception e)
        {
            StickerCache.Diag($"world channel {number} failed: {e.Message}\n");
            return false;
        }
    }

    const string Script = @"
local vm = package.loaded['ui.view_model.chat_main_vm']
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  vm.AsyncChannelGroupSwitch(@N@, token)
  pcall(function() source:Recycle() end)
end)
local ok, err = coroutine.resume(co)
if not ok then error(tostring(err)) end
";
}
