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
/// A project's side of <c>speeddatasinglefile.txt</c>: the node that reads the
/// table, the states it can be read for, and the blends its answer drives.
/// </summary>
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
    public required IReadOnlyList<(string Name, IReadOnlyList<(float Direction, SpeedLadder Ladder)> Arms)> Compasses { get; init; }

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

        var compasses = new List<(string, IReadOnlyList<(float, SpeedLadder)>)>();
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

                if (arms.Count >= 2) compasses.Add((blender.m_name ?? "", arms));
            }

        return new SpeedSampler
        {
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
        foreach ((string name, var arms) in Compasses)
        {
            if (compass is not null && name != compass) continue;
            foreach ((float d, SpeedLadder ladder) in arms)
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

    /// <summary>
    /// The compass serving a locomotion state, or null when the project's
    /// compasses do not separate on it.
    /// </summary>
    /// <remarks>
    /// A key is an <c>iState</c> value and names a movement type (§3.1), and the
    /// movement type is what says which family a record was sampled for: a
    /// <c>GiantProject</c> key of 2 is <c>GiantCombatRun</c> and the compass is
    /// <c>CombatDirectionalBlend_RUN</c>; a <c>DraugrProject</c> key of 5 is
    /// <c>DraugrGreatSword</c> and the compass is <c>2GS_Direction_Blend</c>.
    /// Matching is on the tokens the two names share once the creature's own name
    /// -- carried by every one of its movement types, so it distinguishes nothing
    /// -- is removed.
    ///
    /// This is a name match and it is the one place here that is. The tree says
    /// which blends the sampler drives and which arm answers a heading; it does
    /// not say which family a state belongs to, because nothing in the graph
    /// writes <c>iState</c> for most creatures -- the engine sets it from the
    /// actor's movement type. So the movement type's name is the join, and where
    /// two compasses tie on it this returns null rather than guessing.
    /// </remarks>
    public IReadOnlyList<(float Direction, SpeedLadder Ladder)>? CompassFor(int key)
    {
        if (Compasses.Count == 0) return null;

        var movements = States.FirstOrDefault(s => s.Key == key).MovementTypes ?? [];
        if (movements.Count == 0) return null;

        // Every movement type of a creature carries its name, so the tokens they
        // all share are noise here.
        HashSet<string>? shared = null;
        foreach (SpeedState state in States)
            foreach (string movement in state.MovementTypes)
            {
                HashSet<string> tokens = Tokenise(movement);
                if (shared is null) shared = tokens;
                else shared.IntersectWith(tokens);
            }

        var wanted = new HashSet<string>();
        foreach (string movement in movements) wanted.UnionWith(Tokenise(movement));
        if (shared is not null) wanted.ExceptWith(shared);

        var ranked = Compasses
            .Select(c =>
            {
                HashSet<string> tokens = Tokenise(c.Name);
                return (c.Arms, Score: tokens.Count(t => wanted.Contains(t)) * 10 - tokens.Count(t => !wanted.Contains(t)));
            })
            .OrderByDescending(c => c.Score)
            .ToList();

        return ranked.Count == 1 || ranked[0].Score > ranked[1].Score ? ranked[0].Arms : null;
    }

    /// <summary>Splits a node or movement-type name into comparable tokens.</summary>
    private static HashSet<string> Tokenise(string name)
    {
        string[] parts = System.Text.RegularExpressions.Regex.Split(
            name, @"(?<!^)(?=[A-Z][a-z])|[^A-Za-z0-9]+|(?<=[a-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])");

        // words every locomotion blender carries, which separate nothing
        HashSet<string> noise = ["blend", "direction", "directional", "locomotion", "mt", ""];
        return [.. parts.Select(p => p.ToLowerInvariant()).Where(p => p.Length > 0 && !noise.Contains(p))];
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

    private static IReadOnlyList<SpeedState> ReadStates(ActorProject project)
    {
        var states = new SortedDictionary<int, List<string>>();
        foreach (BehaviorFile behavior in project.Behaviors)
        {
            IList<string> names = VariableNames(behavior);
            var initial = behavior.File.All<hkbBehaviorGraphData>()
                .FirstOrDefault()?.m_variableInitialValues?.m_wordVariableValues;
            if (initial is null) continue;

            for (int i = 0; i < names.Count && i < initial.Count; i++)
            {
                if (!names[i].StartsWith("iState_", StringComparison.OrdinalIgnoreCase)) continue;
                string movt = names[i]["iState_".Length..];
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
