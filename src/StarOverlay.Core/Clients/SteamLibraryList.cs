namespace StarOverlay.Core.Clients;

public static class SteamLibraryList
{
    public static IReadOnlyList<string> ReadLibraryPaths(string libraryFoldersVdf)
    {
        var paths = new List<string>();
        foreach (var rawLine in libraryFoldersVdf.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var quotes = QuotedValues(rawLine);
            if (quotes.Count < 2) continue;
            if (!quotes[0].Equals("path", StringComparison.OrdinalIgnoreCase)) continue;

            var path = quotes[1]
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Replace("\\/", "/", StringComparison.Ordinal);
            if (path.Length > 0)
                paths.Add(path);
        }

        return paths;
    }

    private static List<string> QuotedValues(string line)
    {
        var values = new List<string>();
        var index = 0;
        while (index < line.Length)
        {
            var start = line.IndexOf('"', index);
            if (start < 0 || start + 1 >= line.Length) break;
            var end = line.IndexOf('"', start + 1);
            if (end < 0) break;
            values.Add(line[(start + 1)..end]);
            index = end + 1;
        }

        return values;
    }
}
