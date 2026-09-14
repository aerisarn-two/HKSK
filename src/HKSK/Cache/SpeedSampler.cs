using System.Numerics;
using HKSK.Havok;
using HKSK.Model;
using HKX2;

namespace HKSK.Cache;

/// <summary>
/// One locomotion state a speed table can be sampled for: its key, and the
/// movement types that name it.
/// </summary>
/// <param name="Key">The <c>iState</c> value, which is a record's key.</param>
/// <param name="MovementTypes">
/// The movement types declaring this key, from the <c>iState_&lt;MOVT&gt;</c>
/// variables. Usually one; a project that reuses a key across families has more.
/// </param>
public readonly record struct SpeedState(int Key, IReadOnlyList<string> MovementTypes)
{
    public override string ToString() => $"{Key} = {string.Join(", ", MovementTypes)}";
}

/// <summary>
/// One compass: a blend whose children are speed ladders, so its child weights
/// are headings rather than speeds.
/// </summary>
/// <param name="Name">The blender's node name.</param>
/// <param name="File">
/// The behaviour file it was defined in, without directory or extension.
/// Bethesda split the player's graph along the lines the game switches on --
/// <c>mt_behavior</c>, <c>1hm_locomotion</c>, <c>bow_direction_behavior</c>,
/// <c>crossbow_direction_behavior</c> -- so where a name occurs in several files
/// with different ladders under it, the file says which copy is the locomotion
/// one and which is a variant.
/// </param>
/// <param name="Arms">The ladders, by the heading each answers.</param>
public readonly record struct SpeedCompass(
    string Name, string? File, IReadOnlyList<(float Direction, SpeedLadder Ladder)> Arms, hkbBlenderGenerator Node)
{
    public override string ToString() => $"{Name} ({File}) x{Arms.Count}";
}

/// <summary>
/// A project's side of <c>speeddatasinglefile.txt</c>: the node that reads the
/// table, the states it can be read for, and the blends its answer drives.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This reports what the graph says and infers nothing.</strong>
/// <see cref="SpeedDataFile"/> is the table itself, read and written as
/// <see cref="AnimationDataFile"/> and <see cref="AnimationSetDataFile"/> are;
/// this is the other side of the same join, read the same way. Which compass a
/// given state's records were sampled from is not something the graph settles for
/// most creatures, and guessing it is a caller's business -- one that knows, or
/// is told, should not have to route around a guess made here.
/// </para>
/// <remarks>
/// <para>
/// <c>BSSpeedSamplerModifier</c> is the only thing in the game that reads a speed
/// table. It sits in the root modifier list and is bound to four variables --
/// state, direction and goalSpeed in, the sampled speed out -- and that output
/// variable is what drives the locomotion blenders' <c>m_blendParameter</c>. So
/// the node is also the join: it names the variable, and the variable identifies
/// the ladders, with no guessing from node names.
/// </para>
/// <para>
/// <strong>Eight of the 49 projects have no such node anywhere in the game.</strong>
/// <c>AtronachStormProject</c>, <c>ChaurusFlyer</c>, <c>WispProject</c> and
/// <c>WitchlightProject</c> carry a table that is flat zero; <c>AtronachFlame</c>,
/// <c>DragonProject</c>, <c>Dragon_Priest</c> and <c>IceWraithProject</c> carry a
/// real curve that nothing reads, their blenders being driven straight from
/// <c>Speed</c> instead. <see cref="FromProject"/> returns null for all eight,
/// which is the honest answer: the table is not part of how they move.
/// </para>
/// </remarks>
public sealed class SpeedSampler
{
    /// <summary>The variable the sampler writes.</summary>
    public required string SamplerOutput { get; init; }

    /// <summary>The variable the locomotion blends actually read.</summary>
    /// <remarks>
    /// Usually <see cref="SamplerOutput"/>. Two projects wire it differently and
    /// the difference matters, because it says whether the table's answer reaches
    /// the blend at all: <c>SlaughterfishProject</c>'s ladders read the sampler's
    /// *input* (<c>Speed</c>), and <c>NetchProject</c>'s read <c>SpeedDamped</c>.
    /// <see cref="ReadsSampledSpeed"/> reports which.
    /// </remarks>
    public required string SpeedVariable { get; init; }

    /// <summary>Whether the blends read the sampler's own output.</summary>
    public bool ReadsSampledSpeed => SpeedVariable == SamplerOutput;

    /// <summary>The variable carrying the speed asked for, before sampling.</summary>
    public string? GoalSpeedVariable { get; init; }

    /// <summary>The variable carrying the heading the sampler is asked about.</summary>
    public string? DirectionVariable { get; init; }

    /// <summary>The variable carrying the locomotion state.</summary>
    public string? StateVariable { get; init; }

    /// <summary>The states this project declares, by key.</summary>
    public required IReadOnlyList<SpeedState> States { get; init; }

    /// <summary>Every blend the sampler's answer drives, by node name.</summary>
    public required IReadOnlyList<(string Name, SpeedLadder Ladder)> Ladders { get; init; }

    /// <summary>
    /// The compasses: blenders whose children are themselves sampler-driven, so
    /// their child weights are headings rather than speeds.
    /// </summary>
    /// <remarks>
    /// This is what makes a heading resolvable without reading node names. A
    /// cardinal record picks the child sitting at its direction; §5.2's convention
    /// is 0.00 forward, 0.25 right, 0.50 back, 0.75 left.
    /// </remarks>
    public required IReadOnlyList<SpeedCompass> Compasses { get; init; }

    /// <summary>
    /// The compasses a <c>BSiStateTaggingGenerator</c> puts under each iState
    /// value, which is the graph saying so itself.
    /// </summary>
    /// <remarks>
    /// A tagging generator sets <c>iState</c> to <see cref="int"/> while its
    /// subtree is active, so the compasses under it are the ones that state's
    /// records were sampled from. Eight projects carry them.
    /// <c>BSIStateManagerModifier</c> says the same thing a different way, binding
    /// each entry's <c>iStateToSetAs</c> to an <c>iState_&lt;MOVT&gt;</c> variable
    /// rather than storing the number.
    /// </remarks>
    public required IReadOnlyDictionary<int, IReadOnlyList<SpeedCompass>> TaggedCompasses { get; init; }

    /// <summary>Reads a project's sampler, or null when it has none.</summary>
    public static SpeedSampler? FromProject(ActorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        string? output = null, goal = null, direction = null, state = null;
        foreach (BehaviorFile behavior in project.Behaviors)
        {
            IList<string> names = VariableNames(behavior);
            foreach (BSSpeedSamplerModifier sampler in behavior.File.All<BSSpeedSamplerModifier>())
            {
                output ??= BoundVariable(sampler, "speedOut", names);
                goal ??= BoundVariable(sampler, "goalSpeed", names);
                direction ??= BoundVariable(sampler, "direction", names);
                state ??= BoundVariable(sampler, "state", names);
            }
        }

        if (output is null) return null;

        // A blend is a speed ladder if it reads the speed axis and its children sit
        // at speeds. Prefer the sampler's own output; where nothing reads it, the
        // ladders are driven from the goal speed or a damped form of it, and that
        // is worth resolving rather than reporting no ladders at all.
        var ladders = new List<(string, SpeedLadder)>();
        var driven = new Dictionary<hkbBlenderGenerator, SpeedLadder>();
        string speed = output;
        foreach (string candidate in Candidates(output, goal))
        {
            foreach (BehaviorFile behavior in project.Behaviors)
            {
                IList<string> names = VariableNames(behavior);
                foreach (hkbBlenderGenerator blender in behavior.File.All<hkbBlenderGenerator>())
                {
                    if (BoundVariable(blender, "blendParameter", names) != candidate) continue;
                    if (!LooksLikeSpeeds(blender)) continue;

                    SpeedLadder ladder = SpeedLadder.FromBlender(blender, project);
                    driven[blender] = ladder;
                    ladders.Add((blender.m_name ?? "", ladder));
                }
            }

            if (ladders.Count > 0) { speed = candidate; break; }
        }

        var compasses = new List<SpeedCompass>();
        foreach (BehaviorFile behavior in project.Behaviors)
            foreach (hkbBlenderGenerator blender in behavior.File.All<hkbBlenderGenerator>())
            {
                if (driven.ContainsKey(blender)) continue;

                var arms = new List<(float, SpeedLadder)>();
                foreach (hkbBlenderGeneratorChild? child in blender.m_children ?? [])
                {
                    hkbBlenderGenerator? under = LadderUnder(child?.m_generator, driven);
                    if (under is not null) arms.Add((child!.m_weight, driven[under]));
                }

                if (arms.Count >= 2) compasses.Add(new SpeedCompass(blender.m_name ?? "", behavior.Name, arms, blender));
            }

        // Which compasses a tagging generator puts under each iState value. The
        // node matters and not just its name: a project can define one name in
        // several graphs with different ladders under it, and the tag reaches one
        // of them in particular.
        var tagged = new SortedDictionary<int, List<SpeedCompass>>();
        var byNode = new Dictionary<hkbBlenderGenerator, SpeedCompass>();
        foreach (SpeedCompass c in compasses) byNode[c.Node] = c;

        foreach (BehaviorFile behavior in project.Behaviors)
            foreach (BSiStateTaggingGenerator tagging in behavior.File.All<BSiStateTaggingGenerator>())
                foreach (hkbBlenderGenerator found in CompassesUnder(tagging.m_pDefaultGenerator, byNode, []))
                {
                    if (!tagged.TryGetValue(tagging.m_iStateToSetAs, out List<SpeedCompass>? list))
                        tagged[tagging.m_iStateToSetAs] = list = [];
                    if (!list.Any(c => ReferenceEquals(c.Node, found))) list.Add(byNode[found]);
                }

        return new SpeedSampler
        {
            TaggedCompasses = tagged.ToDictionary(t => t.Key, t => (IReadOnlyList<SpeedCompass>)[.. t.Value]),
            SamplerOutput = output,
            SpeedVariable = speed,
            GoalSpeedVariable = goal,
            DirectionVariable = direction,
            StateVariable = state,
            States = ReadStates(project),
            Ladders = ladders,
            Compasses = compasses,
        };
    }

    /// <summary>The ladder serving a heading, by the compass child sitting on it.</summary>
    /// <remarks>
    /// Returns null for a heading that falls between two compass children, which
    /// is a blend of both and not a ladder (§5.2), and for a project whose
    /// locomotion has no compass at all -- a quadruped turns rather than strafes,
    /// so it has one gait ladder and a turn axis this does not model.
    /// </remarks>
    public SpeedLadder? Cardinal(float direction, string? compass = null)
    {
        foreach (SpeedCompass c in Compasses)
        {
            if (compass is not null && c.Name != compass) continue;
            foreach ((float d, SpeedLadder ladder) in c.Arms)
                if (MathF.Abs(d - direction) <= 1e-4f) return ladder;
        }

        return null;
    }

    /// <summary>The speed axis, in the order worth trying.</summary>
    private static IEnumerable<string> Candidates(string output, string? goal)
    {
        yield return output;
        if (goal is null) yield break;
        yield return goal;
        yield return goal + "Damped";
    }

    /// <summary>
    /// Whether a blend's children sit at speeds rather than at headings.
    /// </summary>
    /// <remarks>
    /// A compass puts its children on [0,1) and a turn axis puts them in degrees,
    /// so neither is distinguishable by the variable alone. A rung above 1 unit
    /// per second separates a speed ladder from a compass; the turn axis is
    /// excluded already by reading a different variable.
    /// </remarks>
    private static bool LooksLikeSpeeds(hkbBlenderGenerator blender)
    {
        float top = 0f;
        foreach (hkbBlenderGeneratorChild? child in blender.m_children ?? [])
            if (child is not null) top = MathF.Max(top, child.m_weight);

        return top > 1.001f;
    }

    /// <summary>Every compass reachable below a node.</summary>
    private static IEnumerable<hkbBlenderGenerator> CompassesUnder(
        object? node, Dictionary<hkbBlenderGenerator, SpeedCompass> wanted, HashSet<object> seen, int depth = 0)
    {
        if (node is null || depth > 14 || !seen.Add(node)) yield break;

        switch (node)
        {
            case hkbBlenderGenerator blender:
                if (wanted.ContainsKey(blender)) { yield return blender; yield break; }
                foreach (hkbBlenderGeneratorChild? child in blender.m_children ?? [])
                    foreach (var found in CompassesUnder(child?.m_generator, wanted, seen, depth + 1)) yield return found;
                break;
            case hkbStateMachine machine:
                foreach (hkbStateMachineStateInfo? state in machine.m_states ?? [])
                    foreach (var found in CompassesUnder(state?.m_generator, wanted, seen, depth + 1)) yield return found;
                break;
            case BSiStateTaggingGenerator tagging:
                foreach (var found in CompassesUnder(tagging.m_pDefaultGenerator, wanted, seen, depth + 1)) yield return found;
                break;
            case hkbModifierGenerator modifier:
                foreach (var found in CompassesUnder(modifier.m_generator, wanted, seen, depth + 1)) yield return found;
                break;
            case BSCyclicBlendTransitionGenerator cyclic:
                foreach (var found in CompassesUnder(cyclic.m_pBlenderGenerator, wanted, seen, depth + 1)) yield return found;
                break;
        }
    }

    /// <summary>
    /// What a compass delivers at a heading, for a table sampled between its arms.
    /// </summary>
    /// <remarks>
    /// The file samples 19 headings at steps of 0.05 and a compass has its arms at
    /// steps of 0.125, so only 0, 0.25, 0.5 and 0.75 ever land on an arm. Every
    /// other heading is a blend of the two arms bracketing it, and it is a
    /// synchronised blend like any other: lerp the travel as a vector, lerp the
    /// duration, divide once at the end. Collapsing each arm to a speed first
    /// would overstate the mix, because the arms point in different directions and
    /// |lerp(a,b)| &lt; lerp(|a|,|b|) unless they are parallel.
    ///
    /// The compass wraps, so the arm at 0.875 brackets with the one at 0.
    /// </remarks>
    public static float Sample(IReadOnlyList<(float Direction, SpeedLadder Ladder)> arms, float direction, float x)
    {
        ArgumentNullException.ThrowIfNull(arms);
        if (arms.Count == 0) return 0f;

        var ordered = arms.OrderBy(a => a.Direction).ToList();
        if (ordered.Count == 1) return ordered[0].Ladder.Evaluate(x);

        int upper = ordered.FindIndex(a => a.Direction >= direction - 1e-4f);
        if (upper < 0) upper = 0;                       // past the last arm: wrap to the first

        if (MathF.Abs(ordered[upper].Direction - direction) <= 1e-4f)
            return ordered[upper].Ladder.Evaluate(x);

        int lower = upper == 0 ? ordered.Count - 1 : upper - 1;

        float from = ordered[lower].Direction;
        float to = ordered[upper].Direction;
        if (to <= from) to += 1f;                       // the wrapping segment
        float here = direction < from ? direction + 1f : direction;

        float span = to - from;
        float u = span > 0f ? (here - from) / span : 0f;

        (Vector3 travelA, float durationA) = ordered[lower].Ladder.Resolve(x);
        (Vector3 travelB, float durationB) = ordered[upper].Ladder.Resolve(x);

        Vector3 travel = Vector3.Lerp(travelA, travelB, u);
        float duration = durationA + u * (durationB - durationA);

        return duration > 0f ? travel.Length() / duration : 0f;
    }

    private static hkbBlenderGenerator? LadderUnder(object? node, Dictionary<hkbBlenderGenerator, SpeedLadder> driven)
    {
        switch (node)
        {
            case null: return null;
            case hkbBlenderGenerator blender when driven.ContainsKey(blender): return blender;
            case hkbModifierGenerator modifier: return LadderUnder(modifier.m_generator, driven);
            case BSiStateTaggingGenerator tagging: return LadderUnder(tagging.m_pDefaultGenerator, driven);
            case BSCyclicBlendTransitionGenerator cyclic: return LadderUnder(cyclic.m_pBlenderGenerator, driven);
            default: return null;
        }
    }

    /// <summary>
    /// The states a project declares, read from the root behaviour graph.
    /// </summary>
    /// <remarks>
    /// <strong>The root graph, not every graph.</strong> A sub-graph carries its
    /// own copy of the <c>iState_&lt;MOVT&gt;</c> variables and the copies do not
    /// always agree: the player's <c>iState_NPCSneaking</c> is 2 in
    /// <c>0_master</c> and 0 in <c>1hm_locomotion</c>,
    /// <c>bow_direction_behavior</c> and two others, and its
    /// <c>iState_NPCSprinting</c> is 1 in <c>0_master</c> and 2 in
    /// <c>1hm_behavior</c>. Merging them puts one movement type under two keys and
    /// makes the state look ambiguous when it is not. The character file names the
    /// graph that counts.
    /// </remarks>
    private static IReadOnlyList<SpeedState> ReadStates(ActorProject project)
    {
        string? rootName = project.Character?.BehaviorFilename is { } filename
            ? Path.GetFileNameWithoutExtension(filename.Replace('\\', '/'))
            : null;

        var states = new SortedDictionary<int, List<string>>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // the root first, so its answer stands where a sub-graph disagrees
        foreach (BehaviorFile behavior in project.Behaviors
                     .OrderByDescending(b => rootName is not null
                         && string.Equals(b.Name, rootName, StringComparison.OrdinalIgnoreCase)))
        {
            IList<string> names = VariableNames(behavior);
            var initial = behavior.File.All<hkbBehaviorGraphData>()
                .FirstOrDefault()?.m_variableInitialValues?.m_wordVariableValues;
            if (initial is null) continue;

            for (int i = 0; i < names.Count && i < initial.Count; i++)
            {
                if (!names[i].StartsWith("iState_", StringComparison.OrdinalIgnoreCase)) continue;

                string movt = names[i]["iState_".Length..];
                if (!claimed.Add(movt)) continue;

                if (!states.TryGetValue(initial[i].m_value, out List<string>? list))
                    states[initial[i].m_value] = list = [];
                if (!list.Contains(movt)) list.Add(movt);
            }
        }

        return [.. states.Select(s => new SpeedState(s.Key, s.Value))];
    }

    private static IList<string> VariableNames(BehaviorFile behavior) =>
        behavior.File.All<hkbBehaviorGraphStringData>().FirstOrDefault()?.m_variableNames ?? [];

    private static string? BoundVariable(hkbBindable? node, string member, IList<string> names)
    {
        foreach (hkbVariableBindingSetBinding binding in node?.m_variableBindingSet?.m_bindings ?? [])
            if (binding.m_memberPath == member)
                return binding.m_variableIndex >= 0 && binding.m_variableIndex < names.Count
                    ? names[binding.m_variableIndex] : null;

        return null;
    }
}
