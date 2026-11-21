namespace StarOverlay.Framework;

/// <summary>The skill names of the game's tables (set by the service when it has read them), for plugins that list skills.</summary>
public static class SkillBook
{
    private static volatile IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>();
    private static volatile IReadOnlyDictionary<int, string> _icons = new Dictionary<int, string>();

    private static volatile IReadOnlyDictionary<int, int> _familyOf = new Dictionary<int, int>();
    private static volatile IReadOnlyDictionary<int, int[]> _members = new Dictionary<int, int[]>();

    /// <param name="icons">Skill id to the file name of its picture without folder and extension (like "weapon_cq-01_kx08").</param>
    /// <param name="families">Skill id to the lowest id of the skills that belong together (the steps of a combo, the enhanced version of a skill ...).</param>
    public static void Set(IReadOnlyDictionary<int, string> names, IReadOnlyDictionary<int, string>? icons = null, IReadOnlyDictionary<int, int>? families = null)
    {
        _names = names;
        if (icons != null) _icons = icons;
        if (families != null)
        {
            _familyOf = families;
            _members = families.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).OrderBy(id => id).ToArray());
        }
    }

    /// <summary>The skill that stands for all the skills belonging to this one (the lowest id of the family); the id itself when the tables name no family.</summary>
    public static int FamilyOf(int id) => _familyOf.TryGetValue(id, out var root) ? root : id;

    /// <summary>Every skill id of the family (the id itself when it has none), lowest first.</summary>
    public static IReadOnlyList<int> MembersOf(int id)
    {
        var root = FamilyOf(id);
        return _members.TryGetValue(root, out var members) ? members : [id];
    }

    /// <summary>The picture name of the skill (like "weapon_cq-01_kx08"), or empty when the tables have none.</summary>
    public static string IconOf(int id) => _icons.TryGetValue(id, out var icon) ? icon : "";

    /// <summary>The game's name of the skill, or "스킬 &lt;id&gt;" when the tables do not name it.</summary>
    public static string NameOf(int id) => _names.TryGetValue(id, out var name) && name.Length > 0 ? name : "스킬 " + id;
}
