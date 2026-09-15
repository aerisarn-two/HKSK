using HKSK.Behavior;
using HKSK.Cache;
using HKX2;

namespace HKSK.Engine;

/// <summary>A generator the graph is evaluating, and how it was reached.</summary>
/// <param name="Generator">The generator itself.</param>
/// <param name="Weight">Its weight within its parent blend, or 1 when nothing blends it.</param>
/// <param name="Depth">How far below the root it sits.</param>
public readonly record struct ActiveNode(hkbGenerator Generator, float Weight, int Depth)
{
    /// <summary>The generator's own name.</summary>
    public string? Name => Generator.m_name;

    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Generator.GetType().Name} '{Name}' x{Weight:0.###}";
}

/// <summary>What a graph selected, and the variables it read to get there.</summary>
/// <param name="Active">The generators being evaluated, the root first.</param>
/// <param name="Variables">Each behaviour file's own table, keyed by file.</param>
/// <param name="Walk">
/// The visit the nodes came from. It is carried because a project read twice gives
/// two sets of objects: asking a <em>different</em> walk where one of these nodes
/// lives returns nothing, silently, and anything filtering on that finds none.
/// </param>
public readonly record struct Evaluation(
    IReadOnlyList<ActiveNode> Active,
    IReadOnlyDictionary<string, Variables> Variables,
    ProjectWalk? Walk = null);

/// <summary>
/// Which generators a graph is evaluating, given its variables.
/// </summary>
/// <remarks>
/// <para>
/// This is the selection half of the engine and deliberately not the pose half:
/// which state a machine sits in and which arm of a blend carries weight decide
/// which animation the game samples, and nothing downstream of that -- the pose,
/// the ragdoll, the IK -- feeds back into the choice. See
/// <c>docs/behavior-engine.md</c> §1.
/// </para>
/// <para>
/// The graph is read at rest: a state machine sits in the state its
/// <c>startStateId</c> names, since no event has been sent. That is the steady
/// state the speed tables describe, not a transition.
/// </para>
/// </remarks>
public static class ActiveGenerators
{
    /// <summary>Every generator active below one, the root itself first.</summary>
    /// <remarks>
    /// <strong>Variable indices are per file.</strong> Each behaviour packfile
    /// carries its own <c>hkbBehaviorGraphData</c>, so a binding's
    /// <c>variableIndex</c> only means anything against the table of the file the
    /// bound node lives in. Resolving a referenced file's bindings against the
    /// root's table reads a different variable entirely -- on the humanoids it
    /// turned <c>startStateId</c> into <c>fSpeedMin</c>.
    /// </remarks>
    public static IReadOnlyList<ActiveNode> Under(hkbGenerator root, ProjectWalk walk) =>
        Evaluate(root, walk).Active;

    /// <summary>Evaluates a project from its packfile, character properties and all.</summary>
    public static Evaluation Of(
        string projectHkx, Action<IReadOnlyDictionary<string, Variables>>? drive = null,
        Events? events = null, SpeedProjectBlock? speeds = null)
    {
        ProjectWalk walk = ProjectWalk.Of(projectHkx);
        hkbBehaviorGraph graph = walk.Steps
            .Select(step => step.Node).OfType<hkbBehaviorGraph>().First();

        return Evaluate(graph, walk, drive, Properties.OfProject(projectHkx), events, speeds);
    }

    /// <summary>
    /// Evaluates a project's graph, optionally setting variables first, and returns
    /// both what it selected and the tables it read.
    /// </summary>
    /// <param name="drive">
    /// Called once with every file's variable table before evaluation, so a caller
    /// can put the character in a situation -- moving, blocking, swimming -- rather
    /// than only see it at rest.
    /// </param>
    public static Evaluation Evaluate(
        hkbGenerator root, ProjectWalk walk,
        Action<IReadOnlyDictionary<string, Variables>>? drive = null,
        Properties? characterProperties = null,
        Events? events = null, SpeedProjectBlock? speeds = null)
    {
        Dictionary<string, Variables> tables = new(StringComparer.OrdinalIgnoreCase);
        VariableSpace space = new();

        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBehaviorGraph graph && !tables.ContainsKey(step.File))
                tables[step.File] = Variables.Of(graph, space);

        // A shared behaviour file is specialised by the character file, not by the
        // graph: the per-creature modifier lists are picked by character properties.
        Properties properties = characterProperties ?? Properties.Empty;

        Variables fallback = root is hkbBehaviorGraph top ? Variables.Of(top) : Variables.Empty;
        drive?.Invoke(tables);
        Tables reading = new(fallback, walk, tables, properties, events ?? Events.None, speeds);

        // Modifiers write variables and variables decide what the machines below
        // select, so one pass is not enough: run until the selection stops moving.
        // Four is well past what any shipped graph needs; the runtime reaches the
        // same place by running the modifier pass every frame.
        IReadOnlyList<ActiveNode> active = [];
        for (int pass = 0; pass < 4; pass++)
        {
            Trace trace = Run(root, reading);
            if (Same(active, trace.Active)) return new Evaluation(trace.Active, tables, walk);

            active = trace.Active;
            Settle(trace, reading);
        }

        return new Evaluation(active, tables, walk);
    }

    /// <summary>What one pass saw.</summary>
    private sealed record Trace(
        List<ActiveNode> Active,
        Dictionary<IHavokObject, int> States,
        List<(hkbModifier Modifier, Variables Variables)> Managers);

    private static Trace Run(hkbGenerator root, Tables reading)
    {
        Trace trace = new([], new Dictionary<IHavokObject, int>(ReferenceEqualityComparer.Instance), []);
        Visit(root, 1f, 0, reading, trace, new HashSet<hkbGenerator>(ReferenceEqualityComparer.Instance));

        return trace;
    }

    /// <summary>Applies the state managers, now that the pass knows where each machine is.</summary>
    private static void Settle(Trace trace, Tables reading)
    {
        foreach ((hkbModifier modifier, Variables variables) in trace.Managers)
            foreach ((int variable, int value) in Modifiers.StateWrites(
                         modifier, node => trace.States.TryGetValue(node, out int at) ? at : null))
                if (variable >= 0 && variable < variables.Count)
                    variables.Set(variable, value);
    }

    private static bool Same(IReadOnlyList<ActiveNode> a, IReadOnlyList<ActiveNode> b)
    {
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i].Generator, b[i].Generator)) return false;

        return true;
    }

    /// <summary>Every generator active below one, against a single variable table.</summary>
    /// <remarks>For a graph held in one file, or for a test that supplies its own.</remarks>
    public static IReadOnlyList<ActiveNode> Under(
        hkbGenerator root, Variables variables, ProjectWalk? walk = null) =>
        Walk(root, variables, walk, null);

    private static IReadOnlyList<ActiveNode> Walk(
        hkbGenerator root, Variables fallback, ProjectWalk? walk,
        Dictionary<string, Variables>? tables) =>
        Run(root, new Tables(fallback, walk, tables)).Active;

    /// <summary>Which variable table a node's bindings are read against.</summary>
    private readonly record struct Tables(
        Variables Fallback, ProjectWalk? Walk, Dictionary<string, Variables>? ByFile,
        Properties? Properties = null, Events? Events = null, SpeedProjectBlock? Speeds = null)
    {
        public Variables For(IHavokObject node)
        {
            if (ByFile is null || Walk is null) return Fallback;

            ProjectStep? step = Walk.StepOf(node);
            return step is not null && ByFile.TryGetValue(step.Value.File, out Variables? table)
                ? table
                : Fallback;
        }
    }

    private static void Visit(
        hkbGenerator? node, float weight, int depth, Tables tables,
        Trace trace, HashSet<hkbGenerator> seen)
    {
        if (node is null || weight <= 0f || depth > 64 || !seen.Add(node)) return;

        List<ActiveNode> found = trace.Active;
        found.Add(new ActiveNode(node, weight, depth));
        Variables variables = tables.For(node);
        Properties? properties = tables.Properties;
        ProjectWalk? walk = tables.Walk;

        switch (node)
        {
            case hkbBehaviorGraph graph:
                Visit(graph.m_rootGenerator, weight, depth + 1, tables, trace, seen);
                break;

            case hkbStateMachine machine:
            {
                int? at = StateIdOf(machine, variables, tables.Events, properties);
                if (at is not null) trace.States[machine] = at.Value;

                Visit(Find(machine, at ?? int.MinValue), weight, depth + 1, tables, trace, seen);
                break;
            }

            case hkbBlenderGenerator blend:
            {
                IList<hkbBlenderGeneratorChild> children = blend.m_children ?? [];
                float[] shares = Shares(blend, children, variables, properties);

                for (int i = 0; i < children.Count; i++)
                    Visit(children[i].m_generator, weight * shares[i], depth + 1, tables, trace, seen);

                break;
            }

            case hkbModifierGenerator wrapper:
                // The modifier runs before the generator below it, because what it
                // writes is what that generator's machines and blends then read.
                Modifiers.Apply(
                    wrapper.m_modifier, variables, properties, tables.Events, tables.Speeds);
                if (wrapper.m_modifier is { } modifier) trace.Managers.Add((modifier, variables));

                Visit(wrapper.m_generator, weight, depth + 1, tables, trace, seen);
                break;

            case hkbManualSelectorGenerator selector:
            {
                IList<hkbGenerator> arms = selector.m_generators ?? [];
                int pick = Bindings.IntOf(
                    selector, "selectedGeneratorIndex", selector.m_selectedGeneratorIndex,
                    variables, properties);

                if (pick >= 0 && pick < arms.Count)
                    Visit(arms[pick], weight, depth + 1, tables, trace, seen);

                break;
            }

            case BSiStateTaggingGenerator tagging:
                // It tags the subtree it guards: iState holds its value from here down.
                // Which variable is not stored anywhere, so it is the one named iState.
                variables.Set("iState", tagging.m_iStateToSetAs);
                Visit(tagging.m_pDefaultGenerator, weight, depth + 1, tables, trace, seen);
                break;

            case BSCyclicBlendTransitionGenerator cyclic:
                Visit(cyclic.m_pBlenderGenerator, weight, depth + 1, tables, trace, seen);
                break;

            case BSBoneSwitchGenerator bones:
                Visit(bones.m_pDefaultGenerator, weight, depth + 1, tables, trace, seen);
                foreach (BSBoneSwitchGeneratorBoneData data in bones.m_ChildrenA ?? [])
                    Visit(data.m_pGenerator, weight, depth + 1, tables, trace, seen);

                break;

            case hkbBehaviorReferenceGenerator reference when walk is not null:
                Visit(Referenced(reference, walk), weight, depth + 1, tables, trace, seen);
                break;

            // hkbClipGenerator, BSSynchronizedClipGenerator, hkbReferencePoseGenerator
            // and BSOffsetAnimationGenerator are leaves: they sample, they do not select.
        }
    }

    /// <summary>From the runtime's own <c>BlenderFlags</c>.</summary>
    private const int FlagParametric = 16, FlagParametricCyclic = 32;

    /// <summary>
    /// How much of each child a blend takes, which is what decides which animation
    /// is sampled and therefore how fast the creature travels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <strong>parametric</strong> blend -- <c>FLAG_PARAMETRIC_BLEND</c> -- does
    /// not mix all its children. Each child's weight is a position on an axis, and
    /// <c>blendParameter</c> picks the two it falls between, so only those two are
    /// live. That is the shape the speed tables record: a ladder of clips indexed
    /// by speed.
    /// </para>
    /// <para>
    /// An ordinary blend mixes every child, normalised so the weights sum to one.
    /// </para>
    /// </remarks>
    private static float[] Shares(
        hkbBlenderGenerator blend, IList<hkbBlenderGeneratorChild> children,
        Variables variables, Properties? properties)
    {
        float[] shares = new float[children.Count];
        if (children.Count == 0) return shares;

        float[] weights = new float[children.Count];
        for (int i = 0; i < children.Count; i++)
            weights[i] = Bindings.RealOf(
                children[i], "weight", children[i].m_weight, variables, properties);

        if ((blend.m_flags & FlagParametric) == 0)
        {
            float total = weights.Sum();
            for (int i = 0; i < shares.Length; i++)
                shares[i] = total > 0f ? weights[i] / total : 0f;

            return shares;
        }

        float at = Bindings.RealOf(blend, "blendParameter", blend.m_blendParameter, variables, properties);
        if ((blend.m_flags & FlagParametricCyclic) != 0)
        {
            float low = blend.m_minCyclicBlendParameter, high = blend.m_maxCyclicBlendParameter;
            if (high > low) at = low + Wrap(at - low, high - low);
        }

        // Children are authored in order along the axis, so the pair that brackets
        // the parameter is the pair to blend; outside the ends, one child carries it.
        if (at <= weights[0]) { shares[0] = 1f; return shares; }
        if (at >= weights[^1]) { shares[^1] = 1f; return shares; }

        for (int i = 0; i + 1 < weights.Length; i++)
        {
            if (at < weights[i] || at > weights[i + 1]) continue;

            float span = weights[i + 1] - weights[i];
            float t = span > 0f ? (at - weights[i]) / span : 0f;
            shares[i] = 1f - t;
            shares[i + 1] = t;

            return shares;
        }

        return shares;
    }

    private static float Wrap(float value, float period)
    {
        float wrapped = value % period;
        return wrapped < 0f ? wrapped + period : wrapped;
    }

    /// <summary>The generator of the state a machine rests in.</summary>
    /// <remarks>
    /// Three things can name the state, and they are checked in the order the
    /// runtime resolves them: a variable bound to <c>startStateId</c>, the
    /// <c>syncVariableIndex</c> field -- which is an index, not a binding -- and
    /// otherwise the stored <c>startStateId</c>.
    /// </remarks>
    public static hkbGenerator? StateOf(hkbStateMachine machine, Variables variables) =>
        Find(machine, StateIdOf(machine, variables) ?? int.MinValue);

    /// <summary>The id of the state a machine rests in, or null when none matches.</summary>
    public static int? StateIdOf(
        hkbStateMachine machine, Variables variables, Events? events = null,
        Properties? properties = null)
    {
        int wanted = Bindings.IntOf(machine, "startStateId", machine.m_startStateId, variables);

        // The sync variable is consulted only in that mode; otherwise the field is
        // set but unused, and reading it picks an arbitrary state. When it names no
        // state -- which is what the initial values give on the vampire brute -- the
        // machine falls back to its own startStateId rather than to nothing.
        int sync = machine.m_syncVariableIndex;
        if ((StartStateMode)machine.m_startStateMode == StartStateMode.START_STATE_MODE_SYNC &&
            sync >= 0 && sync < variables.Count)
        {
            int synced = variables.AsInt(sync);
            if (Find(machine, synced) is not null) wanted = synced;
        }

        if (Find(machine, wanted) is null) return null;

        // Whatever the machine starts in, a raised event may carry it elsewhere.
        return events is null
            ? wanted
            : Transitions.Settle(machine, wanted, events, variables, properties);
    }

    private static hkbGenerator? Find(hkbStateMachine machine, int stateId)
    {
        foreach (hkbStateMachineStateInfo state in machine.m_states ?? [])
            if (state.m_stateId == stateId) return state.m_generator;

        return null;
    }

    /// <summary>The graph a reference generator names, resolved through the visit.</summary>
    /// <remarks>
    /// A reference stores a path and leaves the pointer ignored, so the target is
    /// whatever <see cref="ProjectVisitor"/> opened for it: the file's root
    /// container, whose behaviour graph is the entry point.
    /// </remarks>
    private static hkbGenerator? Referenced(hkbBehaviorReferenceGenerator reference, ProjectWalk walk)
    {
        foreach (ProjectStep step in walk.Steps)
            if (ReferenceEquals(step.Parent, reference) && step.Node is hkRootLevelContainer container)
                foreach (hkRootLevelContainerNamedVariant variant in container.m_namedVariants ?? [])
                    if (variant.m_variant is hkbBehaviorGraph graph)
                        return graph;

        return null;
    }
}
