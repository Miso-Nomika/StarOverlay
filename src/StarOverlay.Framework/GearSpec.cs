namespace StarOverlay.Framework;

public static class GearSpec
{
    public static string Choose(int professionId, IReadOnlyList<string> stats) =>
        Choose(professionId, stats, out _);

    public static string Choose(int professionId, IReadOnlyList<string> stats, out bool tied)
    {
        tied = false;
        var profession = professionId >= 10000 ? professionId / 10000 : professionId;
        if (stats.Count == 0 || !Known.TryGetValue(profession, out var specs)) return "";
        var counts = new Dictionary<string, int>(4);
        for (var i = 0; i < stats.Count; i++)
        {
            var stat = stats[i];
            if (stat.Length == 0) continue;
            counts.TryGetValue(stat, out var count);
            counts[stat] = count + ((i & 1) == 0 ? 2 : 1);
        }

        string best = "";
        var bestScore = 0;
        foreach (var spec in specs)
        {
            counts.TryGetValue(spec.First, out var first);
            counts.TryGetValue(spec.Second, out var second);
            var score = first + second;
            if (spec.Third.Length > 0 && counts.TryGetValue(spec.Third, out var third))
                score += third;
            if (score > bestScore)
            {
                best = spec.Name;
                bestScore = score;
                tied = false;
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }

        return tied ? "" : best;
    }

    readonly record struct Choice(string Name, string First, string Second, string Third = "");

    static readonly Dictionary<int, Choice[]> Known = new()
    {
        [1] = [new("발도", "치명", "숙련"), new("월광", "속공", "행운")],
        [2] = [new("얼창", "치명", "행운"), new("레이", "속공", "숙련")],
        [3] = [new("적홍", "치명", "속공"), new("무상", "행운", "숙련")],
        [4] = [new("질풍", "속공", "숙련"), new("난무", "치명", "행운", "속공")],
        [5] = [new("심판", "숙련", "행운"), new("치유", "속공", "숙련")],
        [9] = [new("암방", "만능", "숙련"), new("가드", "숙련", "행운")],
        [11] = [new("늑대활", "속공", "숙련"), new("매활", "속공", "치명")],
        [12] = [new("신방", "치명", "숙련"), new("광휘", "속공", "숙련")],
        [13] = [new("음파", "속공", "행운"), new("협주", "속공", "치명")],
    };
}
