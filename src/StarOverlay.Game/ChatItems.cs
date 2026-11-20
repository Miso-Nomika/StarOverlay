using StarOverlay.Framework;
using Google.Protobuf;
using Zproto;

namespace StarOverlay.Game;

/// <summary>Chat lines made from items: the system line for loot and the item links inside a player's message.</summary>
internal static class ChatItems
{
    /// <summary>"획득: 회복 물약 x3, 심판의 검" for the items the game shows as gained; empty when nothing was gained.</summary>
    public static string LootText(IEnumerable<(int Id, long Count)> items)
    {
        var parts = items.Where(i => i.Count > 0).Select(i => i.Count > 1 ? $"{ItemBook.NameOf(i.Id)} x{i.Count}" : ItemBook.NameOf(i.Id)).ToArray();
        return parts.Length == 0 ? "" : "획득: " + string.Join(", ", parts);
    }

    /// <summary>Where the k-th item link sits inside the text of a line (the window shows the item name there, as a link).</summary>
    public static string Marker(int index) => "\uE000" + index + "\uE001";

    /// <summary>The text of a line with the item names in place of its markers ("[이름]").</summary>
    public static string Expand(ChatLineView line)
    {
        var text = line.Text;
        var links = line.Links;
        if (links == null || links.Count == 0) return text;
        for (var i = links.Count - 1; i >= 0; i--) text = text.Replace(Marker(i), "[" + ItemBook.NameOf(links[i].ItemId) + "]");
        return text;
    }

    /// <summary>
    /// The text and the item links of a message. A hypertext message has no text of its own: the game sends placeholders (strings and items)
    /// that are shown one after another, so the text is those strings in order with a marker where each item goes. Other placeholders
    /// and broken ones are skipped.
    /// </summary>
    public static (string Text, IReadOnlyList<ChatLink> Links) Compose(ChatMsgInfo info)
    {
        var links = new List<ChatLink>();
        var contents = info.ChatHypertext?.HypertextContents;
        if (contents == null || contents.Count == 0) return (info.MsgText ?? "", links);
        var text = new System.Text.StringBuilder(info.MsgText ?? "");
        foreach (var holder in contents)
        {
            if (holder.Type == PlaceHolderType.String)
            {
                text.Append(StringOf(holder.BytesContent));
            }
            else if (holder.Type == PlaceHolderType.Item)
            {
                try
                {
                    var item = PlaceHolderItem.Parser.ParseFrom(holder.BytesContent);
                    var id = item.ConfigId != 0 ? item.ConfigId : (uint)(item.ItemDetail?.ConfigId ?? 0);
                    if (id == 0) continue;
                    text.Append(Marker(links.Count));
                    links.Add(new ChatLink((int)id, Math.Max(1L, (long)(item.ItemDetail?.Count ?? 1))));
                }
                catch (InvalidProtocolBufferException)
                {
                }
            }
        }

        return (text.ToString(), links);
    }

    /// <summary>The text of a string placeholder: plain UTF-8, or UTF-8 behind a length byte or a protobuf field header (the exact shape is not known).</summary>
    static string StringOf(ByteString bytes)
    {
        var raw = bytes.ToByteArray();
        if (raw.Length == 0) return "";
        foreach (var skip in new[] { raw[0] == raw.Length - 1 ? 1 : 0, raw.Length > 2 && raw[0] == 0x0A && raw[1] == raw.Length - 2 ? 2 : 0 })
        {
            if (skip == 0) continue;
            var text = Decode(raw.AsSpan(skip));
            if (text != null) return text;
        }

        return Decode(raw) ?? "";
    }

    static string? Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            return text.Any(c => c < 0x20 && c != '\n') ? null : text;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return null;
        }
    }
}
