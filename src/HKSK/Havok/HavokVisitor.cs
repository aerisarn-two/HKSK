using HKX2;

namespace HKSK.Havok;

/// <summary>
/// One object reached while visiting a packfile, with how it was reached.
/// </summary>
/// <param name="Node">The object itself.</param>
/// <param name="Parent">The object holding it, or null at the root.</param>
/// <param name="Member">The property of <paramref name="Parent"/> it came from.</param>
/// <param name="Index">Its position when that property is a list, or -1 when it is not.</param>
/// <param name="Depth">How far below the root it sits.</param>
public readonly record struct HavokStep(
    IHavokObject Node,
    IHavokObject? Parent,
    string Member,
    int Index,
    int Depth)
{
    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Node.GetType().Name}" +
        (Member.Length == 0 ? "" : $"  <- .{Member}{(Index < 0 ? "" : $"[{Index}]")}");
}

/// <summary>
/// Visits every object in a packfile from its <c>hkRootLevelContainer</c>.
/// </summary>
/// <remarks>
/// <para>
/// A packfile is not a list, it is a graph with one entry point. HKX2 hands back
/// the root container and leaves the rest to the caller, and the container holds
/// only <em>named variants</em> -- a name, a class name, and a pointer -- so
/// everything a file actually contains hangs below those pointers.
/// </para>
/// <para>
/// This is the type-agnostic form of the walk: it knows nothing about behaviours
/// or characters or skeletons, only that a Havok object's children are the
/// properties of it that hold Havok objects. <see cref="HKSK.Behavior.BehaviorGraph"/>
/// is the same traversal with two things added -- it starts at the behaviour
/// graph's root generator rather than the container, and it follows
/// <c>hkbBehaviorReferenceGenerator</c> across file boundaries, which this cannot
/// because a single file does not know what it is part of.
/// </para>
/// <para>
/// Cycles are real, so an object is visited once, and identity is by reference:
/// the generated classes compare structurally, and two distinct objects with
/// equal contents are two objects.
/// </para>
/// </remarks>
public static class HavokVisitor
{
    /// <summary>Every object reachable from an object, depth first, itself included.</summary>
    public static IEnumerable<HavokStep> Visit(IHavokObject from)
    {
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<HavokStep>();
        stack.Push(new HavokStep(from, null, "", -1, 0));

        while (stack.Count > 0)
        {
            HavokStep step = stack.Pop();
            if (!seen.Add(step.Node)) continue;

            yield return step;

            // reversed so the first child is popped first
            foreach ((string member, int index, IHavokObject child) in HavokEdges.Of(step.Node).Reverse())
                stack.Push(new HavokStep(child, step.Node, member, index, step.Depth + 1));
        }
    }

    /// <summary>
    /// The named variants of a container: what the file declares it holds.
    /// </summary>
    /// <remarks>
    /// Each carries the class name as a string alongside the pointer, written by
    /// whatever produced the file. The two can disagree in principle, which is
    /// worth checking rather than trusting.
    /// </remarks>
    public static IEnumerable<hkRootLevelContainerNamedVariant> NamedVariants(hkRootLevelContainer root)
    {
        foreach (hkRootLevelContainerNamedVariant? variant in root.m_namedVariants)
            if (variant is not null) yield return variant;
    }
}
