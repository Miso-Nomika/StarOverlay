namespace StarOverlay.Framework;

/// <summary>The bar colour of a job (0xRRGGBB), shared by the meter rows and the record window so they look the same.</summary>
public static class ProfessionColors
{
    public static int BaseProfession(int professionId) =>
        professionId >= 10000 ? professionId / 10000 : professionId;

    public static int Bar(int professionId) => BaseProfession(professionId) switch
    {
        1 => 0x805AA3,
        2 => 0x7788D4,
        3 => 0xBA7F12,
        4 => 0x799A9C,
        5 => 0x639C70,
        9 => 0x7D6033,
        11 => 0x8E8B47,
        12 => 0x9C9B75,
        13 => 0x9C5353,
        14 or 15 => 0xDB8787,
        _ => 0x67AEF6,
    };
}

/// <summary>How the meter tints a person's name by weapon (self green, Far Sea light blue, the Far Sea light red); shared with the record window.</summary>
public static class MeterColors
{
    public const int SelfText = 0x3DDC78;
    public const int FuryText = 0xFF9C9C;
    public const int FarSeaText = 0xC6F0FF;

    public static bool IsFury(string weapon) =>
        weapon.Equals("The Far Sea", StringComparison.OrdinalIgnoreCase);

    public static bool IsFarSea(string weapon)
    {
        if (string.IsNullOrWhiteSpace(weapon) || IsFury(weapon)) return false;
        return weapon.Equals("Far Sea", StringComparison.OrdinalIgnoreCase)
            || weapon.Contains("먼바다", StringComparison.Ordinal)
            || weapon.Contains("far sea", StringComparison.OrdinalIgnoreCase)
            || weapon.Contains("fury of the sea", StringComparison.OrdinalIgnoreCase)
            || weapon.Contains("fury ot the sea", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>0 = the default text colour.</summary>
    public static int Text(bool self, string weapon) =>
        self ? SelfText : IsFury(weapon ?? "") ? FuryText : IsFarSea(weapon ?? "") ? FarSeaText : 0;
}
