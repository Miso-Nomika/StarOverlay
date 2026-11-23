using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Panda.ZUi;
using StarOverlay.Ingame.Protocol;
using UnityEngine;
using ZResource.Loader;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// The sticker pictures are files of the game client (address "ui/textures/chat_emoji/sticker_3_11"). The names of all of them are read once from the
/// address list that comes with the agent and from the one the client ships (the files in StreamingAssets\container that begin with "AddressCount"); a sticker whose name fits exactly one of them is
/// loaded through the game's own loader (a ZRawImage that is asked for the address), so no chat bubble of the game is needed.
/// </summary>
static class StickerLoader
{
    const string AddressPrefix = "ui/textures/chat_emoji/";
    const float GiveUpSeconds = 8f;

    static readonly Regex Name = new(@"^[A-Za-z]+_[A-Za-z0-9]+_\d+$", RegexOptions.Compiled);
    static volatile HashSet<string> _names; // the texture names of the stickers and emojis the client has; null until read
    static bool _reading;

    sealed class Pending
    {
        public GameObject Host;
        public ZRawImage Image;
        public string Texture;
        public float Since;
    }

    static readonly Dictionary<string, Pending> Loading = new();
    static readonly HashSet<string> Failed = new();

    /// <summary>Starts reading the client's address list (once, on another thread).</summary>
    internal static void Prepare()
    {
        if (_reading) return;
        _reading = true;
        var root = Plugin.GameRoot;
        var shipped = Path.Combine(Plugin.PluginDirectory, "addresses.txt");
        Task.Run(() =>
        {
            try
            {
                _names = Read(root, shipped);
                StickerCache.Diag($"loader: {_names.Count} picture names read from the client\n");
            }
            catch (Exception e)
            {
                _names = new HashSet<string>();
                StickerCache.Diag("loader: reading the client's address list failed: " + e.Message + "\n");
            }
        });
    }

    static void Collect(string line, HashSet<string> names)
    {
        var at = line.IndexOf(AddressPrefix, StringComparison.Ordinal);
        if (at < 0) return;
        var end = line.IndexOf(' ', at);
        var name = end < 0 ? line[(at + AddressPrefix.Length)..] : line[(at + AddressPrefix.Length)..end];
        if (Name.IsMatch(name)) names.Add(name);
    }

    static HashSet<string> Read(string gameRoot, string shippedList)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        // the list that comes with the agent: the Steam client keeps no readable list of its own, so without it no sticker or emoji could be found there
        if (File.Exists(shippedList))
        {
            foreach (var line in File.ReadLines(shippedList)) Collect(line, names);
        }

        try
        {
            foreach (var data in Directory.GetDirectories(gameRoot, "*_Data"))
            {
                var container = Path.Combine(data, "StreamingAssets", "container");
                if (!Directory.Exists(container)) continue;
                foreach (var file in Directory.GetFiles(container, "*.bytes", SearchOption.AllDirectories))
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var first = reader.ReadLine();
                    if (first == null || !first.TrimStart('﻿').StartsWith("AddressCount", StringComparison.Ordinal)) continue;
                    string line;
                    while ((line = reader.ReadLine()) != null) Collect(line, names);
                }
            }
        }
        catch (Exception e)
        {
            StickerCache.Diag("loader: reading the client's lists failed: " + e.Message + "\n"); // development build only
        }

        return names;
    }

    /// <summary>The name of the client's picture that belongs to a sticker name, when it is certain (null: not known yet, or not certain).</summary>
    internal static string Resolve(string code)
    {
        var names = _names;
        if (names == null) return null;
        var fits = StickerPairing.Candidates(code, names);
        if (fits.Count > 1)
        {
            // several pictures fit the name: the one whose series part is known for the pack is taken, when it is only one
            var known = fits.FindAll(texture => StickerCache.SeriesKnown(code, StickerPairing.SeriesOf(code, texture)));
            if (known.Count == 1) fits = known;
        }

        return fits.Count == 1 ? fits[0] : null;
    }

    /// <summary>Asks for the picture of a sticker when its name fits exactly one picture of the client. Returns whether a load was started or is running.</summary>
    internal static bool Request(string code)
    {
        if (Loading.ContainsKey(code)) return true;
        if (Failed.Contains(code)) return false;
        var texture = Resolve(code);
        if (texture == null) return false;
        try
        {
            var host = new GameObject("StarOverlaySticker");
            host.hideFlags = HideFlags.HideAndDontSave;
            var image = host.AddComponent<ZRawImage>();
            var pending = new Pending { Host = host, Image = image, Texture = texture, Since = Time.realtimeSinceStartup };
            Loading[code] = pending;
            Action done = () => Loaded(code);
            ZResAddress address = AddressPrefix + texture;
            image.SetImageWithCallback(address, done);
            StickerCache.Diag($"loader: asked for {code} -> {texture}\n");
            return true;
        }
        catch (Exception e)
        {
            Loading.Remove(code);
            Failed.Add(code);
            StickerCache.Diag($"loader: asking for {code} failed: {e.Message}\n");
            return false;
        }
    }

    static void Loaded(string code)
    {
        try
        {
            if (!Loading.TryGetValue(code, out var pending)) return;
            var texture = pending.Image == null ? null : pending.Image.texture;
            if (texture != null && StickerCache.CopyFrom(code, pending.Image))
            {
                StickerCache.LearnSeries(code, pending.Texture);
                StickerCache.RememberTexture(code, pending.Texture);
                StickerCache.Diag($"loader: {code} loaded from {pending.Texture} ({texture.name}) in {Time.realtimeSinceStartup - pending.Since:0.00}s\n");
                Finish(code);
                StickerCache.Version++;
                return;
            }

            StickerCache.Diag($"loader: {code} came back without a picture\n");
            Failed.Add(code);
            Finish(code);
        }
        catch (Exception e)
        {
            StickerCache.Diag($"loader: {code} failed: {e.Message}\n");
            Failed.Add(code);
            Finish(code);
        }
    }

    static void Finish(string code)
    {
        if (!Loading.TryGetValue(code, out var pending)) return;
        Loading.Remove(code);
        if (pending.Host != null) UnityEngine.Object.Destroy(pending.Host);
    }

    /// <summary>Every poll: a load that never answered is given up (the chat bubbles take over for it).</summary>
    internal static void Watch()
    {
        if (Loading.Count == 0) return;
        var now = Time.realtimeSinceStartup;
        foreach (var code in Loading.Where(pair => now - pair.Value.Since > GiveUpSeconds).Select(pair => pair.Key).ToList())
        {
            StickerCache.Diag($"loader: {code} gave no answer\n");
            Failed.Add(code);
            Finish(code);
        }
    }
}
