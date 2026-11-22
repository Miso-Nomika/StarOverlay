using System;
using TMPro;
using UnityEngine;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// While typing in the game's chat input, the Tab key moves the sending channel one step on (world, local, guild, party ... and around). The step is made
/// with the game's own function for stepping through its channels (the chat view's moveChatChannelIndex, which also redraws the channel name), so the game's
/// channel list never opens. Then the cursor is put back into the input. Only works while our chat window is in use.
/// </summary>
static class ChannelCycle
{
    static TMP_InputField _input;
    static bool _keyBroken;

    public static void Poll(Transform chat)
    {
        try
        {
            RemoveTab(chat);
            if (_keyBroken || !TabPressed()) return;
            var input = InputField(chat);
            if (input == null || !input.isFocused) return; // only while typing
            GameLua.Execute(Script);
            input.ActivateInputField(); // the cursor goes back into the input
        }
        catch (Exception)
        {
            // a failed step must never reach the game
        }
    }

    static bool TabPressed()
    {
        try
        {
            return Input.GetKeyDown(KeyCode.Tab);
        }
        catch (Exception e)
        {
            _keyBroken = true;
            Plugin.L.LogWarning("channel cycle: the Tab key cannot be read here: " + e.Message);
            return false;
        }
    }

    static TMP_InputField InputField(Transform chat)
    {
        if (_input == null) _input = GameChatUi.Find(chat, "input_field")?.GetComponent<TMP_InputField>();
        return _input;
    }

    /// <summary>The Tab key also types a tab character into the input; it is taken out again at once, so Tab only switches the channel.</summary>
    static void RemoveTab(Transform chat)
    {
        var input = InputField(chat);
        if (input == null) return;
        var text = input.text;
        if (string.IsNullOrEmpty(text) || text.IndexOf('	') < 0) return;
        var caret = input.caretPosition;
        var cleaned = text.Replace("	", "");
        input.text = cleaned;
        input.caretPosition = Math.Clamp(caret - (text.Length - cleaned.Length), 0, cleaned.Length);
    }

    // The game steps through its channels in its own order (guild, party, local, world, ...). A full round (back to where it began) shows which channels
    // there are; they are put in the order world, local, guild, party (others after) and the game is stepped until the one after the current channel.
    const string Script = @"
local data = _G.Z.DataMgr.Get('chat_main_data')
local view = _G.Z.UIMgr:GetView('main_chat_pc')
if not view then error('the game chat is not open') end
local start = data:GetMainChatInputChannel()
local found = { start }
for i = 1, 12 do
  view:moveChatChannelIndex(1)
  local channel = data:GetMainChatInputChannel()
  if channel == start then break end
  found[#found + 1] = channel
end
local rank = { [1] = 0, [2] = 1, [4] = 2, [3] = 3 }
local order = {}
for index, channel in ipairs(found) do order[#order + 1] = { channel = channel, key = (rank[channel] or 10) * 100 + index } end
table.sort(order, function(a, b) return a.key < b.key end)
local position = 1
for index, item in ipairs(order) do if item.channel == start then position = index end end
local target = order[position % #order + 1].channel
if target ~= start then
  for i = 1, #found do
    view:moveChatChannelIndex(1)
    if data:GetMainChatInputChannel() == target then break end
  end
end
";
}
