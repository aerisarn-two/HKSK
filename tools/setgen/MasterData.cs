using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace HKSK.SetGen;

/// <summary>
/// What the set data needs from the game's masters: which events can be sent, which of
/// them equip a weapon, and which attack events each project's races use.
/// </summary>
/// <remarks>
/// HKSK reads Havok files and the animation cache and does not open plugins, so this is
/// the caller's half. The masters are opened directly and in order, later records
/// overriding earlier, as <c>speedgen</c> does.
/// </remarks>
public static class MasterData
{
    public static readonly string[] Order =
        ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    public static HKSK.SetData.GameEvents Events(string dataFolder)
    {
        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var races = new Dictionary<FormKey, IRaceGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();

        try
        {
            foreach (string master in Order)
            {
                string path = Path.Combine(dataFolder, master);
                if (!File.Exists(path)) continue;

                var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
                mods.Add(mod);

                foreach (IIdleAnimationGetter record in mod.IdleAnimations) idles[record.FormKey] = record;
                foreach (IRaceGetter race in mod.Races) races[race.FormKey] = race;
            }

            var idle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var equip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IIdleAnimationGetter record in idles.Values)
            {
                if (string.IsNullOrEmpty(record.AnimationEvent)) continue;
                idle.Add(record.AnimationEvent);

                // The equip path resolves Action Draw and Action Force Equip through the
                // idle tree; the idles it can land on sit under the roots named for
                // drawing and equipping (DrawSheathRoot, ForceEquipRoot, WerewolfDrawRoot).
                if (RootOf(record, idles).EditorID is { } root &&
                    (root.Contains("Draw", StringComparison.OrdinalIgnoreCase) || root.Contains("Equip", StringComparison.OrdinalIgnoreCase)))
                    equip.Add(record.AnimationEvent);
            }

            var attacks = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (IRaceGetter race in races.Values)
                foreach (string? graph in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path })
                {
                    if (string.IsNullOrEmpty(graph)) continue;

                    string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));
                    if (!attacks.TryGetValue(stem, out var events)) attacks[stem] = events = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var attack in race.Attacks)
                        if (!string.IsNullOrEmpty(attack.AttackEvent)) events.Add(attack.AttackEvent);
                }

            // a project's folder, from its races' graph paths: Actors\Character\DefaultMale.hkx
            var stemsIn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (IRaceGetter race in races.Values)
                foreach (string? graph in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path })
                {
                    if (string.IsNullOrEmpty(graph) || FolderOf(graph) is not { } folder) continue;
                    if (!stemsIn.TryGetValue(folder, out var stems)) stemsIn[folder] = stems = new(StringComparer.OrdinalIgnoreCase);
                    stems.Add(Path.GetFileNameWithoutExtension(graph.Replace('\\', '/')));
                }

            var moving = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (IIdleAnimationGetter record in idles.Values)
            {
                if (string.IsNullOrEmpty(record.AnimationEvent) || !ChosenOnTheMove(record, idles)) continue;
                if (BehaviorFolderOf(record, idles) is not { } folder || !stemsIn.TryGetValue(folder, out var stems)) continue;

                foreach (string stem in stems)
                {
                    if (!moving.TryGetValue(stem, out var events)) moving[stem] = events = new(StringComparer.OrdinalIgnoreCase);
                    events.Add(record.AnimationEvent);
                }
            }

            return new HKSK.SetData.GameEvents(
                idle, equip,
                attacks.ToDictionary(a => a.Key, a => (IReadOnlySet<string>)a.Value, StringComparer.OrdinalIgnoreCase),
                moving.ToDictionary(a => a.Key, a => (IReadOnlySet<string>)a.Value, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var mod in mods) mod.Dispose();
        }
    }

    /// <summary>
    /// Whether the idle tree only reaches this idle while the character is moving: it, or an
    /// ancestor, requires <c>IsSprinting == 1</c> or a movement speed above standing, or it is
    /// tried only after a sibling for standing still (<c>GetMovementSpeed &lt;= x</c>) has
    /// failed.
    /// </summary>
    /// <remarks>
    /// An idle tree takes the first child whose conditions pass, so a child's real condition is
    /// its own and its ancestors', and the failure of every sibling before it. The werewolf's
    /// <c>WerewolfLeftRunningPowerAttack</c> has no condition of its own and is tried only after
    /// <c>WerewolfLeftPowerAttack[GetMovementSpeed &lt;= 1]</c>: it is the running power attack by
    /// elimination. Directions are deliberately not counted -- <c>GetMovementDirection == 1</c>
    /// picks the player's forward lunge, whose travel is its own.
    /// </remarks>
    private static bool ChosenOnTheMove(IIdleAnimationGetter idle, Dictionary<FormKey, IIdleAnimationGetter> idles)
    {
        var chain = Chain(idle, idles, 0).ToList();

        foreach (IConditionGetter c in chain.SelectMany(i => i.Conditions))
        {
            if (c.Data is IIsSprintingConditionDataGetter && c.CompareOperator == CompareOperator.EqualTo && Value(c) == 1) return true;
            if (c.Data is IGetMovementSpeedConditionDataGetter
                && c.CompareOperator is CompareOperator.GreaterThan or CompareOperator.GreaterThanOrEqualTo && Value(c) >= 1) return true;
        }

        // beaten siblings: tried first, for standing still
        foreach (IIdleAnimationGetter level in chain)
            foreach (IIdleAnimationGetter before in Chain(level, idles, 1).Skip(1))
                if (before.Conditions.Any(c => c.Data is IGetMovementSpeedConditionDataGetter
                                               && c.CompareOperator is CompareOperator.LessThan or CompareOperator.LessThanOrEqualTo))
                    return true;

        return false;

        static float Value(IConditionGetter c) => c is IConditionFloatGetter f ? f.ComparisonValue : float.NaN;
    }

    /// <summary>Follows one of an idle's two links: 0, the parent; 1, the sibling before it.</summary>
    private static IEnumerable<IIdleAnimationGetter> Chain(
        IIdleAnimationGetter idle, Dictionary<FormKey, IIdleAnimationGetter> idles, int link)
    {
        var seen = new HashSet<FormKey>();
        for (IIdleAnimationGetter? at = idle; at is not null && seen.Add(at.FormKey);)
        {
            yield return at;
            at = at.RelatedIdles.Count > link && !at.RelatedIdles[link].IsNull
                 && idles.TryGetValue(at.RelatedIdles[link].FormKey, out var next) ? next : null;
        }
    }

    /// <summary>The meshes-relative folder of the behaviour an idle belongs to, inherited from its parents.</summary>
    private static string? BehaviorFolderOf(IIdleAnimationGetter idle, Dictionary<FormKey, IIdleAnimationGetter> idles)
    {
        foreach (IIdleAnimationGetter at in Chain(idle, idles, 0))
            if (at.Filename?.DataRelativePath.Path is { Length: > 0 } file)
                return FolderOf(file);

        return null;
    }

    /// <summary>
    /// <c>Meshes/Actors/WerewolfBeast/Behaviors/WerewolfBehavior.hkx</c> and
    /// <c>Actors\WerewolfBeast\WerewolfBeastProject.hkx</c> are both <c>actors/werewolfbeast</c>.
    /// </summary>
    private static string? FolderOf(string path)
    {
        string norm = path.Replace('\\', '/').ToLowerInvariant();
        if (norm.StartsWith("meshes/", StringComparison.Ordinal)) norm = norm[7..];
        int behaviors = norm.IndexOf("/behaviors/", StringComparison.Ordinal);
        return behaviors >= 0 ? norm[..behaviors] : Path.GetDirectoryName(norm)?.Replace('\\', '/');
    }

    private static IIdleAnimationGetter RootOf(IIdleAnimationGetter idle, Dictionary<FormKey, IIdleAnimationGetter> idles)
    {
        var seen = new HashSet<FormKey>();
        IIdleAnimationGetter at = idle;

        while (seen.Add(at.FormKey) && at.RelatedIdles.FirstOrDefault() is { IsNull: false } parent
               && idles.TryGetValue(parent.FormKey, out var next))
            at = next;

        return at;
    }
}
