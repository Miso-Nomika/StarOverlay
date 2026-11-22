using System;
using System.Text.RegularExpressions;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Sends a sticker the way the game's own emoji picker does: it calls the game's Lua function chat_main_vm.AsyncSendMessage with the channel the player
/// has chosen for the chat input (chat_main_data:GetMainChatInputChannel), the sticker's text, the picture-emoji message type and its config number.
/// The game does the sending (and its checks) itself; nothing is sent to the server by us.
/// </summary>
static class StickerSender
{
    const int PictureEmoji = 3; // E.ChitChatMsgType.EChatMsgPictureEmoji
    static readonly Regex Digits = new("^[0-9]+$", RegexOptions.Compiled);

    /// <summary>How the last send ended (1 = handed to the game, 2 = could not be sent) and when; the chat window shows it on its "+".</summary>
    internal static int Result;
    internal static float ResultAt;
    internal static int ResultVersion;

    /// <summary>Sends the sticker to the channel the chat input is on now. Returns false when it could not be handed to the game.</summary>
    internal static bool Send(string code)
    {
        var texture = StickerCache.TextureOfName(code) ?? StickerLoader.Resolve(code);
        // the sticker's name is the name of its picture followed by its config number: sticker_17_4 + 17004
        var config = texture != null && code.StartsWith(texture, StringComparison.Ordinal) ? code[texture.Length..] : null;
        if (config == null || !Digits.IsMatch(config))
        {
            StickerCache.Diag($"send: {code}: the config number is not known (picture {texture ?? "?"})\n");
            return Finish(2);
        }

        try
        {
            GameLua.Execute(Script.Replace("@TEXT@", "emojiPic=%s=%s" + code).Replace("@CONFIG@", config).Replace("@TYPE@", PictureEmoji.ToString()));
            return Finish(1);
        }
        catch (Exception e)
        {
            StickerCache.Diag($"send: {code} failed: {e.Message}\n");
            return Finish(2);
        }
    }

    static bool Finish(int result)
    {
        Result = result;
        ResultAt = UnityEngine.Time.realtimeSinceStartup;
        ResultVersion++;
        return result == 1;
    }

    const string Script = @"
local vm = package.loaded['ui.view_model.chat_main_vm']
local data = _G.Z.DataMgr.Get('chat_main_data')
local channel = data:GetMainChatInputChannel()
if channel == nil then error('no chat input channel') end
local source = _G.Z.CancelSource.Rent()
local token = source:CreateToken()
local co = coroutine.create(function()
  vm.AsyncSendMessage(channel, nil, '@TEXT@', @TYPE@, @CONFIG@, token)
  pcall(function() source:Recycle() end)
end)
local ok, err = coroutine.resume(co)
if not ok then error(tostring(err)) end
";
}
