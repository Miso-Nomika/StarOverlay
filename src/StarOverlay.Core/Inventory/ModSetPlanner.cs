using StarOverlay.Core.Model;

namespace StarOverlay.Core.Inventory;

public enum ModChangeKind
{
    Enable,
    Disable,
}

public sealed record ModChange(ModId Id, ModChangeKind Kind);

public sealed record ModPlan(IReadOnlyList<ModChange> Changes, IReadOnlyList<ModId> Missing);

public static class ModSetPlanner
{
    public static ModPlan Plan(IReadOnlyList<ManagedMod> installed, IReadOnlyCollection<ModId> desiredEnabled)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(desiredEnabled);

        var installedIds = new HashSet<ModId>();
        foreach (var mod in installed)
        {
            if (!installedIds.Add(mod.Id))
                throw new ArgumentException($"Duplicate mod id '{mod.Id}'.", nameof(installed));
        }

        var wanted = new HashSet<ModId>();
        var missing = new List<ModId>();
        foreach (var id in desiredEnabled)
        {
            if (!wanted.Add(id)) continue;
            if (!installedIds.Contains(id)) missing.Add(id);
        }

        var changes = new List<ModChange>();
        foreach (var mod in installed)
        {
            var shouldEnable = wanted.Contains(mod.Id);
            if (shouldEnable && !mod.Enabled)
                changes.Add(new ModChange(mod.Id, ModChangeKind.Enable));
            else if (!shouldEnable && mod.Enabled)
                changes.Add(new ModChange(mod.Id, ModChangeKind.Disable));
        }

        return new ModPlan(changes, missing);
    }
}
