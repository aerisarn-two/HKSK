using HKSK.Behavior;
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
    public static IReadOnlyList<ActiveNode> Under(hkbGenerator root, ProjectWalk walk)
    {
        Dictionary<string, Variables> tables = new(StringComparer.OrdinalIgnoreCase);

        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBehaviorGraph graph && !tables.ContainsKey(step.File))
                tables[step.File] = Variables.Of(graph);

        Variables fallback = root is hkbBehaviorGraph top ? Variables.Of(top) : Variables.Empty;
        return Walk(root, fallback, walk, tables);
    }

    /// <summary>Every generator active below one, against a single variable table.</summary>
    /// <remarks>For a graph held in one file, or for a test that supplies its own.</remarks>
    public static IReadOnlyList<ActiveNode> Under(
        hkbGenerator root, Variables variables, ProjectWalk? walk = null) =>
        Walk(root, variables, walk, null);

    private static IReadOnlyList<ActiveNode> Walk(
        hkbGenerator root, Variables fallback, ProjectWalk? walk,
        Dictionary<string, Variables>? tables)
    {
        List<ActiveNode> found = [];
        HashSet<hkbGenerator> seen = new(ReferenceEqualityComparer.Instance);

        Visit(root, 1f, 0, new Tables(fallback, walk, tables), found, seen);
        return found;
    }

    /// <summary>Which variable table a node's bindings are read against.</summary>
    private readonly record struct Tables(
        Variables Fallback, ProjectWalk? Walk, Dictionary<string, Variables>? ByFile)
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
        List<ActiveNode> found, HashSet<hkbGenerator> seen)
    {
        if (node is null || weight <= 0f || depth > 64 || !seen.Add(node)) return;

        found.Add(new ActiveNode(node, weight, depth));
        Variables variables = tables.For(node);
        ProjectWalk? walk = tables.Walk;

        switch (node)
        {
            case hkbBehaviorGraph graph:
                Visit(graph.m_rootGenerator, weight, depth + 1, tables, found, seen);
                break;

            case hkbStateMachine machine:
                Visit(StateOf(machine, variables), weight, depth + 1, tables, found, seen);
                break;

            case hkbBlenderGenerator blend:
                foreach (hkbBlenderGeneratorChild child in blend.m_children ?? [])
                {
                    float share = Bindings.RealOf(child, "weight", child.m_weight, variables);
                    Visit(child.m_generator, weight * share, depth + 1, tables, found, seen);
                }

                break;

            case hkbModifierGenerator wrapper:
                Visit(wrapper.m_generator, weight, depth + 1, tables, found, seen);
                break;

            case hkbManualSelectorGenerator selector:
            {
                IList<hkbGenerator> arms = selector.m_generators ?? [];
                int pick = Bindings.IntOf(
                    selector, "selectedGeneratorIndex", selector.m_selectedGeneratorIndex, variables);

                if (pick >= 0 && pick < arms.Count)
                    Visit(arms[pick], weight, depth + 1, tables, found, seen);

                break;
            }

            case BSiStateTaggingGenerator tagging:
                Visit(tagging.m_pDefaultGenerator, weight, depth + 1, tables, found, seen);
                break;

            case BSCyclicBlendTransitionGenerator cyclic:
                Visit(cyclic.m_pBlenderGenerator, weight, depth + 1, tables, found, seen);
                break;

            case BSBoneSwitchGenerator bones:
                Visit(bones.m_pDefaultGenerator, weight, depth + 1, tables, found, seen);
                foreach (BSBoneSwitchGeneratorBoneData data in bones.m_ChildrenA ?? [])
                    Visit(data.m_pGenerator, weight, depth + 1, tables, found, seen);

                break;

            case hkbBehaviorReferenceGenerator reference when walk is not null:
                Visit(Referenced(reference, walk), weight, depth + 1, tables, found, seen);
                break;

            // hkbClipGenerator, BSSynchronizedClipGenerator, hkbReferencePoseGenerator
            // and BSOffsetAnimationGenerator are leaves: they sample, they do not select.
        }
    }

    /// <summary>The generator of the state a machine rests in.</summary>
    /// <remarks>
    /// Three things can name the state, and they are checked in the order the
    /// runtime resolves them: a variable bound to <c>startStateId</c>, the
    /// <c>syncVariableIndex</c> field -- which is an index, not a binding -- and
    /// otherwise the stored <c>startStateId</c>.
    /// </remarks>
    public static hkbGenerator? StateOf(hkbStateMachine machine, Variables variables)
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

        return Find(machine, wanted);
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
