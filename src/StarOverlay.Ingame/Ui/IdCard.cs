using System;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Opens the game's own character card (the window a click on a name in the game's chat opens) for a character id, by calling the game's Lua function
/// idcard_vm.AsyncGetCardData(charId, cancelToken) the way its chat bubble does. No chat bubble of the game is needed, so it works for every channel.
/// </summary>
static class IdCard
{
    /// <summary>Hands the request to the game. Returns false when it could not be handed over.</summary>
    internal static bool Open(long charId)
    {
        if (charId <= 0) return false;
        try
        {
            GameLua.Execute(Script.Replace("@ID@", charId.ToString()));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    const string Script = @"
local vm = package.loaded['ui.view_model.idcard_vm']
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  vm.AsyncGetCardData(@ID@, token)
  pcall(function() source:Recycle() end)
end)
local ok, err = coroutine.resume(co)
if not ok then error(tostring(err)) end
";
}
