using StarOverlay.Framework;

namespace StarOverlay.PartyJoin;

public sealed class PartyJoinPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("party.join", "파티 가입", "0.4.2");

    public void Load(IPluginContext context)
    {
        context.Controls.UseFrameButton(0, "\U0001F5D1\uFE0F", press: LiveParty.Clear);
        context.Ui.AddList("party.join.rows", Rows, "가입 신청 없음");
    }

    public void Unload()
    {
    }

    public IReadOnlyList<MeterRowView> Rows()
    {
        return LiveParty.Rows
            .Select(person => new MeterRowView(
                person.CharId == 0 ? "" : person.CharId.ToString(),
                person.Profession.Length > 0 ? $"{person.Name}-{person.Profession}" : person.Name,
                person.ProfessionId >= 10000 ? person.ProfessionId / 10000 : person.ProfessionId,
                $"{person.FightPoint} / {person.SeasonStrength}",
                100, // no damage ratio in this list: a full bar
                false,
                0,
                [],
                IconColor: IconColor(person)))
            .ToArray();
    }

    private const int FuryIcon = 0xE23B3B;

    private static int IconColor(PartyApplicant person)
    {
        var id = PartyJobs.Normalize(person.ProfessionId);
        if (id != 0
            && person.CharId != 0
            && SpecBook.TryGet(person.CharId, out var saved, out _, out _, out var farSea)
            && farSea
            && PartyJobs.ProfessionOf(saved) == id)
            return FuryIcon;

        if (id is 9 or 12) return 0x3B82F6;
        if (person.Profession is "협주" or "치유") return 0x3DDC78;
        return FuryIcon;
    }
}
