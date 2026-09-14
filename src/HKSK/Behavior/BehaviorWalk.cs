using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// A completed walk of a graph, holding each node's path back to the root.
/// </summary>
/// <remarks>
/// <para>
/// The path is the reason to walk a graph rather than search it. A blend node
/// called <c>MT_DirectionalBlend</c> says nothing on its own; the chain above it
/// says the falmer reaches it through <c>MT_State</c>, then
/// <c>1HM_Locomotion_Behavior</c>, then the state
/// <c>1HM_DirectionalState_Walk</c>, tagged <c>iState = 2</c> -- which is the key
/// its speed-table entry is filed under, arrived at structurally.
/// </para>
/// <para>
/// It also makes siblings visible, and a gait change is a pair of siblings: the
/// giant's <c>CombatDirectionalState_WALK</c> and <c>CombatDirectionalState_RUN</c>
/// are states 0 and 1 of one <c>CombatLocomotionBehavior</c>, and the falmer's
/// walk and run blends are two states of one <c>1HM_Locomotion_Behavior</c>
/// carrying <c>iState</c> 2 and 3. Which is what a caller reaching for a name
/// token was trying to recover.
/// </para>
/// </remarks>
public sealed class BehaviorWalk
{
    private readonly Dictionary<IHavokObject, BehaviorStep> _steps;
    private Dictionary<(IHavokObject Machine, int State), int>? _managed;

    internal BehaviorWalk(IReadOnlyList<BehaviorStep> steps)
    {
        Steps = steps;

        _steps = new Dictionary<IHavokObject, BehaviorStep>(ReferenceEqualityComparer.Instance);
        foreach (BehaviorStep step in steps) _steps.TryAdd(step.Node, step);
    }

    /// <summary>Every node reached, depth first from the root.</summary>
    public IReadOnlyList<BehaviorStep> Steps { get; }

    /// <summary>Whether the walk reached a node at all.</summary>
    public bool Reached(IHavokObject node) => _steps.ContainsKey(node);

    /// <summary>The step a node was reached by, or null when it was not reached.</summary>
    public BehaviorStep? StepOf(IHavokObject node) =>
        _steps.TryGetValue(node, out BehaviorStep step) ? step : null;

    /// <summary>
    /// The nodes above a node, nearest first, ending at the root. The node itself
    /// is not included.
    /// </summary>
    public IEnumerable<BehaviorStep> Ancestors(IHavokObject node)
    {
        IHavokObject? at = _steps.TryGetValue(node, out BehaviorStep step) ? step.Parent : null;

        while (at is not null && _steps.TryGetValue(at, out BehaviorStep above))
        {
            yield return above;
            at = above.Parent;
        }
    }

    /// <summary>The path from the root down to a node, the node last.</summary>
    public IReadOnlyList<BehaviorStep> PathTo(IHavokObject node)
    {
        if (!_steps.TryGetValue(node, out BehaviorStep step)) return [];

        List<BehaviorStep> path = [.. Ancestors(node)];
        path.Reverse();
        path.Add(step);

        return path;
    }

    /// <summary>
    /// The <c>iState</c> a node is tagged with, from the nearest
    /// <c>BSiStateTaggingGenerator</c> above it, or null when none tags it.
    /// </summary>
    /// <remarks>
    /// The tag is how the graph tells the game which speed-table entry is in
    /// force: <c>iState</c> is a graph variable the game writes from the actor's
    /// movement type, and <c>BSiStateTaggingGenerator</c> sets it on entry to the
    /// subtree it guards. So a blend under a tag serves that key, and this is the
    /// one route to the key that reads no names at all.
    /// </remarks>
    public int? TagOf(IHavokObject node)
    {
        foreach (BehaviorStep above in Ancestors(node))
            if (above.Node is BSiStateTaggingGenerator tag) return tag.m_iStateToSetAs;

        return null;
    }

    /// <summary>
    /// The <c>iState</c> in force under a node, by either route the graph offers,
    /// or null when neither reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two node types set <c>iState</c> and they are used in different graphs.
    /// <c>BSiStateTaggingGenerator</c> sets it on entry to the subtree it guards,
    /// so it is read from the ancestors (<see cref="TagOf"/>).
    /// <c>BSIStateManagerModifier</c> sets it from a table of
    /// (state machine, state id) pairs, which only becomes resolvable once the
    /// walk can say which machine a node sits under.
    /// </para>
    /// <para>
    /// Over the corpus the two reach 61 and 10 of the 142 direction blends and
    /// <strong>never the same one</strong>, which is why both are needed to get to
    /// half of them.
    /// </para>
    /// </remarks>
    public int? KeyOf(IHavokObject node)
    {
        if (TagOf(node) is { } tagged) return tagged;

        _managed ??= ManagedStates();

        foreach (BehaviorStep above in Ancestors(node))
        {
            if (above.Node is not hkbStateMachineStateInfo info) continue;

            BehaviorStep? at = StepOf(info);
            if (at?.Parent is null) continue;

            if (_managed.TryGetValue((at.Value.Parent, info.m_stateId), out int key)) return key;
        }

        return null;
    }

    /// <summary>Every (machine, state) the graph's state managers assign an iState to.</summary>
    private Dictionary<(IHavokObject, int), int> ManagedStates()
    {
        var managed = new Dictionary<(IHavokObject, int), int>();

        foreach (BehaviorStep step in Steps)
        {
            if (step.Node is not BSIStateManagerModifier manager) continue;

            foreach (BSIStateManagerModifierBSiStateData data in manager.m_stateData)
                if (data.m_pStateMachine is not null)
                    managed[(data.m_pStateMachine, data.m_StateID)] = data.m_iStateToSetAs;
        }

        return managed;
    }
}
