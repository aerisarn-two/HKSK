using HKSK.SetData;
using HKSK.Speed;

namespace HKSK.Records;

/// <summary>
/// What the game's records mean to the caches: the engine's rules over them, which the
/// plugin format does not state.
/// </summary>
public static class GameRecordRules
{
    /// <summary>Every movement type, by the name the graph's <c>iState_&lt;MOVT&gt;</c> constants carry.</summary>
    /// <remarks>
    /// That name is the record's <c>MNAM</c>, falling back to the editor id where the
    /// record has none: the falmer's <c>Falmer_1HM_Walk</c> is named <c>Falmer1HMWalk</c>.
    /// </remarks>
    public static IReadOnlyDictionary<string, MovementType> MovementTypes(IGameRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var types = new Dictionary<string, MovementType>(StringComparer.OrdinalIgnoreCase);

        foreach (MovementTypeRecord movement in records.MovementTypes)
        {
            if (GraphName(movement) is not { } name) continue;

            types[name] = new MovementType(
                name,
                movement.ForwardWalk, movement.ForwardRun,
                movement.BackWalk, movement.BackRun,
                movement.LeftWalk, movement.LeftRun,
                movement.RightWalk, movement.RightRun);
        }

        return types;
    }

    /// <summary>
    /// The roles the races give each movement type -- walk, run, swim, fly, sneak,
    /// sprint -- by the type's graph name.
    /// </summary>
    /// <remarks>
    /// A race's base movement defaults are the only place the masters point at a
    /// movement type, apart from the default object manager naming the player's.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlySet<MovementRole>> MovementRoles(IGameRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var names = new Dictionary<string, string>();
        foreach (MovementTypeRecord movement in records.MovementTypes)
            if (GraphName(movement) is { } name) names[movement.Id] = name;

        var roles = new Dictionary<string, SortedSet<MovementRole>>(StringComparer.OrdinalIgnoreCase);
        foreach (RaceRecord race in records.Races)
            foreach ((MovementRole role, string id) in race.DefaultMovements)
            {
                if (!names.TryGetValue(id, out string? name)) continue;
                if (!roles.TryGetValue(name, out SortedSet<MovementRole>? set)) roles[name] = set = [];
                set.Add(role);
            }

        return roles.ToDictionary(r => r.Key, r => (IReadOnlySet<MovementRole>)r.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The projects a race wears -- the actors -- by the stem of the graph it names.</summary>
    /// <remarks>
    /// This is what separates an actor from a prop, and the Havok files do not say it: 247 of the
    /// game's props have animations as well. A race naming <c>Actors\Canine\WolfProject.hkx</c>
    /// makes <c>WolfProject</c> an actor. The masters' races wear 48 of the 49 actors; the
    /// 49th, <c>FirstPerson</c>, is the player's view, which the engine loads by itself.
    /// </remarks>
    public static IReadOnlySet<string> ActorProjects(IGameRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RaceRecord race in records.Races)
            foreach (string? graph in new[] { race.MaleBehavior, race.FemaleBehavior })
                if (!string.IsNullOrEmpty(graph))
                    projects.Add(Path.GetFileNameWithoutExtension(graph.Replace('\\', '/')));

        return projects;
    }

    /// <summary>What the set data needs to know the game can send (<see cref="GameEvents"/>).</summary>
    public static GameEvents Events(IGameRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var idles = new Dictionary<string, IdleRecord>();
        foreach (IdleRecord record in records.Idles) idles[record.Id] = record;

        var idle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var equip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (IdleRecord record in idles.Values)
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
        // a project's folder, from its races' graph paths: Actors\Character\DefaultMale.hkx
        var stemsIn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (RaceRecord race in records.Races)
            foreach (string? graph in new[] { race.MaleBehavior, race.FemaleBehavior })
            {
                if (string.IsNullOrEmpty(graph)) continue;

                string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));
                if (!attacks.TryGetValue(stem, out var events)) attacks[stem] = events = new(StringComparer.OrdinalIgnoreCase);
                foreach (string attack in race.AttackEvents)
                    if (!string.IsNullOrEmpty(attack)) events.Add(attack);

                if (FolderOf(graph) is not { } folder) continue;
                if (!stemsIn.TryGetValue(folder, out var stems)) stemsIn[folder] = stems = new(StringComparer.OrdinalIgnoreCase);
                stems.Add(stem);
            }

        var moving = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (IdleRecord record in idles.Values)
        {
            if (string.IsNullOrEmpty(record.AnimationEvent) || !ChosenOnTheMove(record, idles)) continue;
            if (BehaviorFolderOf(record, idles) is not { } folder || !stemsIn.TryGetValue(folder, out var stems)) continue;

            foreach (string stem in stems)
            {
                if (!moving.TryGetValue(stem, out var events)) moving[stem] = events = new(StringComparer.OrdinalIgnoreCase);
                events.Add(record.AnimationEvent);
            }
        }

        return new GameEvents(
            idle, equip,
            attacks.ToDictionary(a => a.Key, a => (IReadOnlySet<string>)a.Value, StringComparer.OrdinalIgnoreCase),
            moving.ToDictionary(a => a.Key, a => (IReadOnlySet<string>)a.Value, StringComparer.OrdinalIgnoreCase));
    }

    private static string? GraphName(MovementTypeRecord movement) =>
        (movement.Name ?? movement.EditorID) is { Length: > 0 } name ? name : null;

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
    private static bool ChosenOnTheMove(IdleRecord idle, Dictionary<string, IdleRecord> idles)
    {
        var chain = Chain(idle, idles, parent: true).ToList();

        foreach (IdleCondition c in chain.SelectMany(i => i.Conditions))
        {
            if (Is(c, "IsSprinting") && c.Operator == ConditionOperator.EqualTo && c.Value == 1) return true;
            if (Is(c, "GetMovementSpeed")
                && c.Operator is ConditionOperator.GreaterThan or ConditionOperator.GreaterThanOrEqualTo && c.Value >= 1) return true;
        }

        // beaten siblings: tried first, for standing still
        foreach (IdleRecord level in chain)
            foreach (IdleRecord before in Chain(level, idles, parent: false).Skip(1))
                if (before.Conditions.Any(c => Is(c, "GetMovementSpeed")
                                               && c.Operator is ConditionOperator.LessThan or ConditionOperator.LessThanOrEqualTo))
                    return true;

        return false;

        static bool Is(IdleCondition c, string function) =>
            string.Equals(c.Function, function, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Follows one of an idle's two links: its parents, or the siblings before it.</summary>
    private static IEnumerable<IdleRecord> Chain(IdleRecord idle, Dictionary<string, IdleRecord> idles, bool parent)
    {
        var seen = new HashSet<string>();
        for (IdleRecord? at = idle; at is not null && seen.Add(at.Id);)
        {
            yield return at;
            string? link = parent ? at.Parent : at.PreviousSibling;
            at = link is not null && idles.TryGetValue(link, out IdleRecord? next) ? next : null;
        }
    }

    /// <summary>The top of an idle's tree: the last idle before an action or nothing.</summary>
    private static IdleRecord RootOf(IdleRecord idle, Dictionary<string, IdleRecord> idles) =>
        Chain(idle, idles, parent: true).Last();

    /// <summary>The meshes-relative folder of the behaviour an idle belongs to, inherited from its parents.</summary>
    private static string? BehaviorFolderOf(IdleRecord idle, Dictionary<string, IdleRecord> idles)
    {
        foreach (IdleRecord at in Chain(idle, idles, parent: true))
            if (at.BehaviorFile is { Length: > 0 } file)
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
}
