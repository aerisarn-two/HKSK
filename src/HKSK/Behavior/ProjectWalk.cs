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
}
