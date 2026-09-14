using HKSK.Havok;
using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// One object reached while visiting a project's whole behaviour, across files.
/// </summary>
/// <param name="Node">The object itself.</param>
/// <param name="File">The packfile holding it, which changes at a reference.</param>
/// <param name="Parent">The object holding it, or null at a file's container.</param>
/// <param name="Member">The property of <paramref name="Parent"/> it came from.</param>
/// <param name="Index">Its position when that property is a list, or -1 when it is not.</param>
/// <param name="Depth">How far below the first container it sits.</param>
public readonly record struct ProjectStep(
    IHavokObject Node,
    string File,
    IHavokObject? Parent,
    string Member,
    int Index,
    int Depth)
{
    /// <summary>The packfile's own name, without folder or extension.</summary>
    public string FileName => Path.GetFileNameWithoutExtension(File);

    /// <summary>The object's name, for the node types that carry one.</summary>
    public string? Name => (Node as hkbNode)?.m_name;

    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Node.GetType().Name}" +
        (Name is null or "" ? "" : $" '{Name}'") + $"  [{FileName}]";
}

/// <summary>
/// Visits everything a project's behaviour is made of, opening referenced files
/// as it meets them.
/// </summary>
/// <remarks>
/// <para>
/// This follows the same chain a reader has to: a project names one character
/// file, that names one behaviour file, and that file's graph reaches others by
/// <c>hkbBehaviorReferenceGenerator</c>, which stores a <em>path</em> and leaves
/// the pointer <c>SERIALIZE_IGNORED</c>. Nothing in a packfile links the two, so
/// the reference is resolved the way the engine resolves it -- against the
/// project's own folder -- and the target is opened then and there.
/// </para>
/// <para>
/// It differs from <see cref="BehaviorGraph"/> in two ways that matter. It starts
/// at each file's <c>hkRootLevelContainer</c> rather than at the behaviour graph's
/// root generator, so the per-file <c>hkbBehaviorGraph</c> wrappers are visited
/// too; and it takes nothing from the animation cache, which lists a project's
/// behaviour files but does not say which is the entry point or which references
/// which. The set of files it opens is therefore a result rather than an input,
/// and can be compared against the cache's list to see whether the two agree.
/// </para>
/// <para>
/// A file is opened once however many references point at it, and an object is
/// visited once. Identity is by reference, and objects from different files are
/// always distinct: each packfile is deserialised into its own object graph, so
/// two files that both hold a node called <c>Root</c> hold two different nodes.
/// </para>
/// </remarks>
public static class ProjectVisitor
{
    /// <summary>
    /// Visits a project's behaviour from its packfile, across every referenced file.
    /// </summary>
    /// <param name="projectHkx">Path to a project packfile, e.g. <c>defaultmale.hkx</c>.</param>
    /// <returns>Nothing at all when the project does not resolve to a root behaviour.</returns>
    public static IEnumerable<ProjectStep> Visit(string projectHkx)
    {
        if (BehaviorRoot.Of(projectHkx) is not { } root) return [];

        return Visit(root.BehaviorFile, Path.GetDirectoryName(projectHkx)!);
    }

    /// <summary>
    /// The same, from a behaviour file and the folder its references resolve
    /// against.
    /// </summary>
    /// <remarks>
    /// The folder is the <em>project's</em>, not the file's. A referenced graph
    /// sits beside the one referencing it in the shipped game, but the stored
    /// paths are written relative to the project, and the quadrupeds prove the
    /// difference matters: the dog's graph reaches
    /// <c>Behaviors\QuadrupedBehavior.hkx</c> while the wolf's reaches
    /// <c>Behaviors Wolf\QuadrupedBehavior.hkx</c>, two files with one name.
    /// </remarks>
    public static IEnumerable<ProjectStep> Visit(string behaviorHkx, string projectFolder)
    {
        var opened = new Dictionary<string, HavokFile>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<ProjectStep>();

        if (Open(opened, behaviorHkx) is not { } first) yield break;

        stack.Push(new ProjectStep(first.Root, first.Path, null, "", -1, 0));

        while (stack.Count > 0)
        {
            ProjectStep step = stack.Pop();
            if (!seen.Add(step.Node)) continue;

            yield return step;

            // A reference is a leaf in the packfile and an edge in the engine.
            if (step.Node is hkbBehaviorReferenceGenerator reference)
            {
                string? path = HavokPath.Resolve(projectFolder, reference.m_behaviorName);
                HavokFile? target = path is null ? null : Open(opened, path);

                if (target is not null)
                    stack.Push(new ProjectStep(
                        target.Root, target.Path, step.Node,
                        nameof(reference.m_behaviorName), -1, step.Depth + 1));

                continue;
            }

            // reversed so the first child is popped first
            foreach ((string member, int index, IHavokObject child) in HavokEdges.Of(step.Node).Reverse())
                stack.Push(new ProjectStep(child, step.File, step.Node, member, index, step.Depth + 1));
        }
    }

    private static HavokFile? Open(Dictionary<string, HavokFile> opened, string path)
    {
        string key = Path.GetFullPath(path);
        if (opened.TryGetValue(key, out HavokFile? already)) return already;

        if (!File.Exists(key)) return null;

        HavokFile file = HavokFile.Load(key);
        opened[key] = file;

        return file;
    }
}
