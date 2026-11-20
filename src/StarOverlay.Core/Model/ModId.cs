namespace StarOverlay.Core.Model;

/// <summary>
/// Canonical mod identifier. Stored lowercase; comparison is case-insensitive.
/// </summary>
public readonly record struct ModId
{
    public string Value { get; }

    private ModId(string value) => Value = value;

    public static bool TryParse(string? text, out ModId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (trimmed.Length is < 2 or > 64) return false;
        if (!char.IsAsciiLetter(trimmed[0])) return false;
        if (trimmed[^1] is '.' or '-') return false;
        if (trimmed.Contains("..", StringComparison.Ordinal)) return false;

        foreach (var c in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')
                continue;
            return false;
        }

        id = new ModId(trimmed.ToLowerInvariant());
        return true;
    }

    public override string ToString() => Value ?? "";
}
