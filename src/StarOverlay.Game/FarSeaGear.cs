namespace StarOverlay.Game;

/// <summary>
/// The far sea gear ("머나먼 바다"), known by equipment id. Names cannot tell them apart any more: the game's Korean names of the
/// season 4 pieces are "영원한 광휘의 …". The meter names the two tiers by the weapon text kept in <c>Fighter.Weapon</c>.
/// </summary>
internal static class FarSeaGear
{
    /// <summary>The real far sea: the season 4 pieces (gear score 320) and their "모조" copies (380). Shown light red.</summary>
    internal const string RealWeapon = "The Far Sea";

    /// <summary>The earlier far sea gear (the former 220 pieces and the lower ones). Shown light blue.</summary>
    internal const string OldWeapon = "Far Sea";

    static readonly HashSet<int> Real = [.. Enumerable.Range(2000635, 18)]; // 2000635..2000652

    static readonly HashSet<int> Old =
    [
        2000617, 2000618, 2000619, 2000620, 2000621, 2000622, 2000623, 2000624, 2000625, 2000626, 2000627, 2000628, 2000629, 2000630,
        2000631, 2000632, 2000633, 2000634,
        7310006, 7311061, 7311062, 7311063, 7320006, 7321061, 7321062, 7321063, 7330006, 7331061, 7331062, 7331063,
        7340006, 7341061, 7341062, 7341063, 7350006, 7351061, 7351062, 7351063, 7370006, 7371061, 7371062, 7371063,
        7390006, 7391061, 7391062, 7391063, 7400006, 7401061, 7401062, 7401063, 7410006, 7411061, 7411062, 7411063,
    ];

    /// <summary>The weapon text of a worn piece of gear, or null when it is no far sea gear.</summary>
    internal static string? WeaponOf(int equipId) => Real.Contains(equipId) ? RealWeapon : Old.Contains(equipId) ? OldWeapon : null;
}
