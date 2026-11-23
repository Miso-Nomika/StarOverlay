using System.Globalization;
using StarOverlay.Framework;

namespace StarOverlay.PartyJoin;

public sealed class PartyJoinPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("party.join", "파티 가입", "0.4.10");

    public void Load(IPluginContext context)
    {
        context.Controls.UseFrameButton(0, "\U0001F5D1\uFE0F", press: LiveParty.Clear);
        context.Controls.UseFrameButton(1, "\u2699\uFE0F", opensSettings: true);
        context.Controls.AddText("row-height", "줄 높이", DefaultRowHeight.ToString(CultureInfo.InvariantCulture), MinRowHeight, MaxRowHeight);
        context.Ui.AddList("party.join.rows", Rows, "가입 신청 없음", () => RowHeight(context));
        LiveParty.SetActive(true); // the agent reads the master scores and hands accept and reject to the game
    }

    public void Unload() => LiveParty.SetActive(false); // the agent lets go of the game

    public IReadOnlyList<MeterRowView> Rows()
    {
        var failed = LiveParty.RecentFailure(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Entries().Select(entry => entry.Header ? HeaderRow(entry, failed) : PersonRow(entry, failed)).ToArray();
    }

    /// <summary>One line of the window: an applicant, or the header of an application of several people (the answer is for the whole application).</summary>
    internal sealed record Entry(PartyApplicant Person, bool Header, int GroupSize, bool Member);

    /// <summary>
    /// The lines of the window. A single applicant is one line with the accept and reject buttons. An application of several people (a merge) is a group:
    /// a header line "합병 신청 · N명" with the buttons, then its members under it without buttons. Singles come first (tanks, healers, damage), then the
    /// groups in the order they came, each with its members sorted the same way.
    /// </summary>
    internal static IReadOnlyList<Entry> Entries()
    {
        var applications = LiveParty.Rows.GroupBy(person => person.ApplyId).ToList();
        var entries = new List<Entry>();
        foreach (var person in applications.Where(group => group.Count() == 1).SelectMany(group => group).OrderBy(person => Role(person)))
            entries.Add(new Entry(person, false, 1, false));
        foreach (var group in applications.Where(group => group.Count() > 1))
        {
            var members = group.OrderBy(person => Role(person)).ToList();
            entries.Add(new Entry(members[0], true, members.Count, false));
            entries.AddRange(members.Select(member => new Entry(member, false, members.Count, true)));
        }

        return entries;
    }

    private static MeterRowView PersonRow(Entry entry, string failed)
    {
        var person = entry.Person;
        var name = person.Profession.Length > 0 ? $"{person.Name}-{person.Profession}" : person.Name;
        return new MeterRowView(
            "", // no UID in front of the name
            entry.Member ? "└ " + name : name,
            person.ProfessionId >= 10000 ? person.ProfessionId / 10000 : person.ProfessionId,
            ValueText(person, entry.Member ? "" : failed),
            0, // no damage ratio in this list: no bar, the rows are plain
            false,
            0,
            [],
            IconColor: RoleColor(Role(person)), // only the picture of the class is tinted
            Actions: entry.Member ? null : Buttons(person.ApplyId));
    }

    private static MeterRowView HeaderRow(Entry entry, string failed) =>
        new("", $"합병 신청 · {entry.GroupSize}명", -1, entry.Person.ApplyId == failed && failed.Length > 0 ? "<color=#ff8a8a>실패</color>" : "", 0, false, 0, [], Actions: Buttons(entry.Person.ApplyId));

    private static RowAction[] Buttons(string applyId) =>
    [
        new RowAction("수락", "accept:" + applyId, AcceptColor, "check"),
        new RowAction("거절", "reject:" + applyId, RejectColor, "cross"),
    ];

    /// <summary>0 tank (shield knight and the like), 1 healer (a healing spec, or a healer class whose spec is not known), 2 in between (심판 and 음파: damage that also supports), 3 damage dealer: what the class and the spec of the applicant are known to be.</summary>
    private static int Role(PartyApplicant person)
    {
        if (PartyJobs.Normalize(person.ProfessionId) is 9 or 12) return 0;
        if (PartyJobs.IsHealer(person.ProfessionId, person.Profession)) return 1;
        return person.Profession is "심판" or "음파" ? 2 : 3;
    }

    private const int TankColor = 0x3B82F6;
    private const int HealerColor = 0x3DDC78;
    private const int DamageColor = 0xE23B3B;

    /// <summary>The tint of the class picture: tank blue, healer green, damage dealer red (the in-between specs are damage dealers here).</summary>
    private static int RoleColor(int role) => role switch { 0 => TankColor, 1 => HealerColor, _ => DamageColor };

    private const int DefaultRowHeight = 30;
    private const int MinRowHeight = 18;
    private const int MaxRowHeight = 72;

    /// <summary>The height of a row in the window (the setting "줄 높이"); the text and the buttons grow with it.</summary>
    private static int RowHeight(IPluginContext context) =>
        int.TryParse(context.Settings.Get("row-height"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, MinRowHeight, MaxRowHeight) : DefaultRowHeight;

    private const int AcceptColor = 0x2E9B5A;
    private const int RejectColor = 0xC0392B;

    /// <summary>"128,400+42 · 1,234점": fight power and season strength joined by a plus without spaces, then the master score as points ("비공개" when the player keeps it private, "…" until it is read, "-" when the game gave none).</summary>
    internal static string ValueText(PartyApplicant person, string failedApplyId)
    {
        // -1: not read yet ("…"), below that: the game gave no score ("-"), then private or the number
        var master = person.MasterScore == -1 ? "<color=#9ea9bb>…</color>"
            : person.MasterScore < -1 ? "<color=#9ea9bb>-</color>"
            : person.MasterShown ? person.MasterScore.ToString("N0", CultureInfo.InvariantCulture) + "점"
            : "<color=#9ea9bb>비공개</color>";
        var text = $"{person.FightPoint.ToString("N0", CultureInfo.InvariantCulture)}+{person.SeasonStrength} · {master}";
        return person.ApplyId == failedApplyId && failedApplyId.Length > 0 ? text + " <color=#ff8a8a>실패</color>" : text;
    }
}
