using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using StarOverlay.Ingame.Protocol;

namespace StarOverlay.Ingame.Ui;

/// <summary>
/// Where the game's picture files are, read once from the lists the client ships (StreamingAssets\container, the files that begin with "AddressCount") on another
/// thread: a picture of the game is asked for by its name and <see cref="AddressOf"/> gives the address the game's loader needs (see <see cref="AddressList"/>).
/// </summary>
static class AddressBook
{
    static volatile AddressList _list;
    static bool _reading;

    /// <summary>True once the lists are read (before that a missing name only means "not known yet").</summary>
    internal static bool Ready => _list != null;

    /// <summary>The address of the picture with this name; null while the lists are being read and when the game has no such picture. The first call starts reading.</summary>
    internal static string AddressOf(string name)
    {
        if (_list == null) Start();
        return _list?.AddressOf(name);
    }

    static void Start()
    {
        if (_reading) return;
        _reading = true;
        var root = Plugin.GameRoot;
        var shipped = Path.Combine(Plugin.PluginDirectory, "addresses.txt");
        Task.Run(() =>
        {
            // the list that comes with the agent (the picture files of the game by name): the Steam client keeps no readable list of its own, and with it the
            // pictures load at once on every client. It is ready in a moment; the game's own lists, read after it, add what a newer patch brought.
            var shippedList = new AddressList();
            ReadLines(shipped, shippedList);
            _list = shippedList;

            var list = new AddressList();
            ReadLines(shipped, list);
            try
            {
                foreach (var data in Directory.GetDirectories(root, "*_Data"))
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
                        while ((line = reader.ReadLine()) != null) list.Add(line);
                    }
                }
            }
            catch (Exception e)
            {
                StickerCache.Diag("address book: reading failed: " + e.Message + "\n"); // development build only
            }

            if (list.Count > shippedList.Count) _list = list; // the game's lists knew more than ours
        });
    }

    static void ReadLines(string path, AddressList into)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path)) into.Add(line);
        }
        catch (Exception e)
        {
            StickerCache.Diag("address book: " + path + ": " + e.Message + "\n"); // development build only
        }
    }
}
