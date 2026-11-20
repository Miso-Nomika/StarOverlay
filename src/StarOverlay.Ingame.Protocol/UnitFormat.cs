using System.Globalization;

namespace StarOverlay.Ingame.Protocol;

/// <summary>Damage numbers the way the meter shows them: K, M, B with two decimals.</summary>
public static class UnitFormat
{
    public static string Short(double value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);
        var culture = CultureInfo.InvariantCulture;
        if (abs >= 999_995_000d) return sign + (abs / 1_000_000_000d).ToString("0.00", culture) + "B";
        if (abs >= 999_995d) return sign + (abs / 1_000_000d).ToString("0.00", culture) + "M";
        if (abs >= 999.995d) return sign + (abs / 1_000d).ToString("0.00", culture) + "K";
        return sign + abs.ToString("0.##", culture);
    }

    public static string Percent(double fraction, int decimals = 1) =>
        (fraction * 100).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) + "%";
}
