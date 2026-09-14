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
