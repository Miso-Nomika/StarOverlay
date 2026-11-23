using System;
using System.Collections.Generic;

namespace StarOverlay.Ingame.Protocol;

/// <summary>
/// The names of the game's picture files and where each one is. The client ships lists of its files (the "AddressCount" files in StreamingAssets\container), one
/// line per file; a line "address:ui/textures/skill_aoyi/skill_aoyi_skill_icon_001 -&gt;&gt;&gt;&gt; hash:..." gives a picture file: its name is the last part of the
/// address, and the address is what the game's loader asks for. A name that two different addresses have is not certain and is left out.
/// </summary>
public sealed class AddressList
{
    const string Marker = "address:";
    const string End = " ->>>>";

    readonly Dictionary<string, string> _unique = new(StringComparer.Ordinal);
    readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);

    public int Count => _unique.Count;

    /// <summary>Takes one line of a list; lines that are not a picture file ("address:" lines under ui/textures or without a folder) are ignored.</summary>
    public void Add(string line)
    {
        if (!line.StartsWith(Marker, StringComparison.Ordinal)) return;
        var end = line.IndexOf(End, StringComparison.Ordinal);
        if (end < 0) return;
        var address = line[Marker.Length..end].Trim();
        var slash = address.LastIndexOf('/');
        if (slash >= 0 && !address.StartsWith("ui/textures/", StringComparison.Ordinal)) return; // atlas pieces and other kinds are not asked for by name
        var name = slash >= 0 ? address[(slash + 1)..] : address;
        if (name.Length == 0 || _ambiguous.Contains(name)) return;
        if (_unique.TryGetValue(name, out var known))
        {
            if (known == address) return;
            _unique.Remove(name);
            _ambiguous.Add(name);
            return;
        }

        _unique[name] = address;
    }

    /// <summary>The address of the picture with this name, or null when the game has none (or two).</summary>
    public string? AddressOf(string name) => _unique.TryGetValue(name, out var address) ? address : null;
}
