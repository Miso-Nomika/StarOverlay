using System;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The game's full chat window (J) opens on the channel its chat tab shows, which may differ from the channel the player types into. While our chat window
/// is shown, this wraps the game's chat_main_vm.OpenChatMainPCView: just before the window opens, the channel it shows is set to the current input channel
/// (world, local, party, guild). The wrapper is put in once per game start and does nothing while our window is off.
/// </summary>
static class FullChatChannel
{
    static bool _installed;
    static bool _active;

    /// <summary>Called every poll with whether our chat window is shown. Hands the game a change only when the state changes.</summary>
    internal static void Update(bool ours)
    {
        if (ours == _active && (_installed || !ours)) return;
        try
        {
            GameLua.Execute(Install + "\nvm.__starOverlayOn = " + (ours ? "true" : "false"));
            _installed |= ours;
            _active = ours;
        }
        catch (Exception)
        {
            // the game's Lua or its chat is not loaded yet: tried again at the next poll
        }
    }

    const string Install = @"
local vm = package.loaded['ui.view_model.chat_main_vm']
if not vm then error('the chat is not loaded yet') end
if not vm.__starOverlayJ then
  vm.__starOverlayJ = true
  local orig = vm.OpenChatMainPCView
  vm.OpenChatMainPCView = function(...)
    if vm.__starOverlayOn then
      pcall(function()
        local data = _G.Z.DataMgr.Get('chat_main_data')
        local channel = data:GetMainChatInputChannel()
        if channel == 1 or channel == 2 or channel == 3 or channel == 4 then data:SetChannelId(channel) end
      end)
    end
    return orig(...)
  end
end
";
}
