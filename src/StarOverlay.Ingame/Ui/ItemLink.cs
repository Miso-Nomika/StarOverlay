using System;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Opens the game's own item card of a linked item in a chat message, with the item's full data (refinements, attributes). The game keeps every message in
/// its chat data per channel (chat_main_data:GetChannelQueueByChannelId); the message is found by its id there, the game builds the message's links as it
/// does for a chat bubble (chat_main_vm.GetShowMsg, whose link registration is held back for that moment) and the click is handed to the game's own
/// chat_main_vm.onClickItemShare. No chat bubble of the game is needed, so it works whichever channel the game's windows show.
/// </summary>
static class ItemLink
{
    /// <summary>Hands the click to the game. Returns false when the message is not in the game's data (any more) or the game refused.</summary>
    internal static bool Open(string channel, long msgId, int itemId)
    {
        var type = ChannelType(channel);
        if (type == 0 || msgId == 0 || itemId <= 0) return false;
        try
        {
            GameLua.Execute(Script.Replace("@TYPE@", type.ToString()).Replace("@MSG@", msgId.ToString()).Replace("@ITEM@", itemId.ToString()));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // the numbers of the game's E.ChatChannelType for the channel names of the service (LiveChat.ChannelName)
    static int ChannelType(string channel) => channel switch
    {
        "월드" => 1,
        "필드" => 2,
        "파티" => 3,
        "길드" => 4,
        "귓속말" => 5,
        "그룹" => 6,
        "초보자" => 9,
        _ => 0,
    };

    const string Script = @"
local data = _G.Z.DataMgr.Get('chat_main_data')
local queue = data:GetChannelQueueByChannelId(@TYPE@)
local found
if queue then
  for _, entry in pairs(queue) do
    if entry.ChitChatMsg and entry.ChitChatMsg.msgId == @MSG@ then found = entry end
  end
end
if not found then error('message not in the game chat data') end
local vm = package.loaded['ui.view_model.chat_main_vm']
local captured
local original = vm.addLinkClick
vm.addLinkClick = function(_, _, links, message) captured = { links = links, message = message } end
local ok, err = pcall(vm.GetShowMsg, found)
vm.addLinkClick = original
if not ok then error(tostring(err)) end
if not captured or not captured.links then error('the message has no links') end
local link
for _, candidate in pairs(captured.links) do
  local value = candidate.value
  if value and value.configId == @ITEM@ then link = candidate break end
end
if not link then error('the link is not in the message') end
local function find(parent, name)
  for i = 0, parent.childCount - 1 do
    local child = parent:GetChild(i)
    if child.name == name then return child end
    local deeper = find(child, name)
    if deeper then return deeper end
  end
end
local root = UnityEngine.GameObject.Find('zuiroot').transform
local list = find(root, 'main_chat_pc(Clone)')
local anchor = list and find(list, 'node_dialogue') or root
local rect = anchor:GetComponent('RectTransform')
vm.onClickItemShare(link, rect, captured.message)
";
}
