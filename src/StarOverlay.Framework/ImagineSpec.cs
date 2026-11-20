namespace StarOverlay.Framework;

public static class ImagineSpec
{
    public static bool IsTank(int professionId)
    {
        var id = professionId >= 10000 ? professionId / 10000 : professionId;
        return id is 9 or 12;
    }

    public static int Profession(string spec) => spec switch
    {
        "발도" or "월광" => 1,
        "얼창" or "레이" => 2,
        "무상" or "적홍" => 3,
        "질풍" or "난무" => 4,
        "심판" or "치유" => 5,
        "늑대활" or "매활" => 11,
        "음파" or "협주" => 13,
        _ => 0,
    };

    public static string Choose(int professionId, IReadOnlyList<int> equipped, IEnumerable<(int Imagine, string Spec, int Count)> seen)
    {
        var profession = professionId >= 10000 ? professionId / 10000 : professionId;
        if (IsTank(profession) || equipped.Count == 0) return "";
        var scores = new Dictionary<string, int>();
        var mine = new HashSet<int>(equipped);
        foreach (var (imagine, spec, count) in seen)
        {
            if (count <= 0 || !mine.Contains(imagine) || Profession(spec) == 0) continue;
            if (profession != 0 && Profession(spec) != profession) continue;
            scores.TryGetValue(spec, out var total);
            scores[spec] = total + count;
        }

        string best = "";
        var bestScore = 0;
        var tie = false;
        foreach (var pair in scores)
        {
            if (pair.Value > bestScore)
            {
                best = pair.Key;
                bestScore = pair.Value;
                tie = false;
            }
            else if (pair.Value == bestScore)
            {
                tie = true;
            }
        }

        return tie || bestScore <= 0 ? "" : best;
    }
}
