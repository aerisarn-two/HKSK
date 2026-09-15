using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// A completed multi-file visit, holding each object's path back to the project's
/// first container.
/// </summary>
/// <remarks>
/// The same service <see cref="BehaviorWalk"/> gives over a single graph, but over
/// a <see cref="ProjectVisitor"/> visit, so ancestry crosses file boundaries: the
/// dog's forward blends are in <c>forwardlocomotion.hkx</c> and their ancestors run
/// back through <c>quadrupedbehavior.hkx</c> to <c>dogbehavior.hkx</c>.
/// </remarks>
public sealed class ProjectWalk
{
    private readonly Dictionary<IHavokObject, ProjectStep> _steps;
    private Dictionary<(IHavokObject, int), int>? _managed;

    /// <summary>Indexes a visit. The visit is enumerated once.</summary>
    public ProjectWalk(IEnumerable<ProjectStep> steps)
    {
        Steps = [.. steps];

        _steps = new Dictionary<IHavokObject, ProjectStep>(ReferenceEqualityComparer.Instance);
        foreach (ProjectStep step in Steps) _steps.TryAdd(step.Node, step);
    }

    /// <summary>Visits a project and indexes the result.</summary>
    public static ProjectWalk Of(string projectHkx) => new(ProjectVisitor.Visit(projectHkx));

    /// <summary>Every object reached, depth first.</summary>
    public IReadOnlyList<ProjectStep> Steps { get; }

    /// <summary>The step an object was reached by, or null when it was not reached.</summary>
    public ProjectStep? StepOf(IHavokObject node) =>
        _steps.TryGetValue(node, out ProjectStep step) ? step : null;

    /// <summary>The objects above one, nearest first. The object itself is not included.</summary>
    public IEnumerable<ProjectStep> Ancestors(IHavokObject node)
    {
        IHavokObject? at = _steps.TryGetValue(node, out ProjectStep step) ? step.Parent : null;

        while (at is not null && _steps.TryGetValue(at, out ProjectStep above))
        {
            yield return above;
            at = above.Parent;
        }
    }

    /// <summary>The nearest ancestor of a type, or null.</summary>
    public T? Nearest<T>(IHavokObject node) where T : class
    {
        foreach (ProjectStep above in Ancestors(node))
            if (above.Node is T found) return found;

        return null;
    }

    /// <summary>
    /// The <c>iState</c> a node is tagged with, from the nearest
    /// <c>BSiStateTaggingGenerator</c> above it, or null when none tags it.
    /// </summary>
    public int? TagOf(IHavokObject node) =>
        Nearest<BSiStateTaggingGenerator>(node)?.m_iStateToSetAs;

    /// <summary>
    /// The <c>iState</c> in force under a node, by either route the graph offers,
    /// or null when neither reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing selects a generator on <c>iState</c>.</strong> Across the 49
    /// projects only two members bind to it: <c>BSSpeedSamplerModifier.state</c>,
    /// which reads it to index the speed table, and
    /// <c>BSIStateManagerModifier.iStateVar</c>, which writes it. It is a label the
    /// graph announces for the table, not a switch -- so the way to find a key's
    /// generators is to read where the graph declares it, not to set it and see.
    /// </para>
    /// <para>
    /// Two node types declare it and they are used in different graphs.
    /// <c>BSiStateTaggingGenerator</c> tags the subtree it guards, so it is read
    /// from the ancestors. <c>BSIStateManagerModifier</c> declares it for a table of
    /// (state machine, state id) pairs, which needs the ancestry to say which
    /// machine a node sits under. Over the corpus the two reach different states and
    /// <strong>never the same one</strong>, which is why both are needed.
    /// </para>
    /// </remarks>
    public int? KeyOf(IHavokObject node)
    {
        if (TagOf(node) is { } tagged) return tagged;

        _managed ??= ManagedStates();

        foreach (ProjectStep above in Ancestors(node))
        {
            if (above.Node is not hkbStateMachineStateInfo info) continue;

            ProjectStep? at = StepOf(info);
            if (at?.Parent is null) continue;

            if (_managed.TryGetValue((at.Value.Parent, info.m_stateId), out int key)) return key;
        }

        return null;
    }

    /// <summary>Every (machine, state) the project's state managers declare an iState for.</summary>
    private Dictionary<(IHavokObject, int), int> ManagedStates()
    {
        Dictionary<(IHavokObject, int), int> managed = new();

        foreach (ProjectStep step in Steps)
        {
            if (step.Node is not BSIStateManagerModifier manager) continue;

            foreach (BSIStateManagerModifierBSiStateData data in manager.m_stateData ?? [])
                if (data.m_pStateMachine is not null)
                    managed[(data.m_pStateMachine, data.m_StateID)] = data.m_iStateToSetAs;
        }

        return managed;
    }
}
