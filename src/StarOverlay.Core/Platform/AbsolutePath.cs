namespace StarOverlay.Core.Platform;

public enum PathNormalizeError
{
    Empty,
    NotAbsolute,
    Invalid,
}

public static class AbsolutePath
{
    public static bool TryNormalize(string? path, out string normalized, out PathNormalizeError error)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = PathNormalizeError.Empty;
            return false;
        }

        var trimmed = path.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(trimmed))
        {
            error = PathNormalizeError.NotAbsolute;
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
            error = default;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = PathNormalizeError.Invalid;
            return false;
        }
    }
}
