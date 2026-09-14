using HKSK.Havok;
using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// One node reached while visiting a graph, with how it was reached.
/// </summary>
/// <param name="Node">The object itself.</param>
/// <param name="File">The behaviour file it was read from, which changes at a reference.</param>
/// <param name="Parent">The node it hangs off, or null at the root.</param>
/// <param name="Member">The property of <paramref name="Parent"/> that holds it.</param>
/// <param name="Index">Its position when that property is a list, or -1 when it is not.</param>
/// <param name="Depth">How far below the root it sits.</param>
public readonly record struct BehaviorStep(
    IHavokObject Node,
    BehaviorFile File,
    IHavokObject? Parent,
    string Member,
    int Index,
    int Depth)
{
    /// <summary>The node's own name, for the node types that carry one.</summary>
    public string? Name => Node switch
    {
        hkbNode node => node.m_name,
        _ => null,
    };

    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Node.GetType().Name}" +
        (Name is null or "" ? "" : $" '{Name}'") +
        $"  [{File.Name}]";
}

/// <summary>
/// A project's behaviour files as one graph, walked from the character's root
/// generator downwards.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A behaviour graph is not a file.</strong> Bethesda split the player's
/// across seventeen of them and joined them back up with
/// <c>hkbBehaviorReferenceGenerator</c>, which names its target as a
/// <em>string</em> and leaves the pointer <c>SERIALIZE_IGNORED</c> for the engine
/// to resolve at load. Nothing in the packfile links the two, so a walk that
/// stays inside one file sees an island: 123 of these references stitch 13 of the
/// 49 projects together, and the player and the first-person rig account for 93
/// of them.
/// </para>
/// <para>
/// That is the difference between reading the graph and guessing at it. Treating
/// the files as islands forces a caller to identify a subtree by the name of the
/// file it sits in, or by the tokens in a node's name -- which works until a node
/// is called something the convention did not anticipate. Following the
/// references instead gives the one thing those heuristics were standing in for:
/// the path from the character's root to the node, through the states and blends
/// that actually reach it.
/// </para>
/// <para>
/// Cycles are real -- a state machine can transition back to an ancestor -- so a
/// node is visited once. Identity is by reference and not by value: the generated
/// classes compare structurally, and two distinct nodes with equal contents are
/// two nodes.
/// </para>
/// </remarks>
public sealed class BehaviorGraph
{
    private readonly Dictionary<string, BehaviorFile> _byName;

    private BehaviorGraph(IReadOnlyList<BehaviorFile> files, BehaviorFile? root, Dictionary<string, BehaviorFile> byName)
    {
        Files = files;
        RootFile = root;
        _byName = byName;
    }

    /// <summary>Every behaviour file the project carries.</summary>
    public IReadOnlyList<BehaviorFile> Files { get; }

    /// <summary>
    /// The file the character names, which is where a walk starts.
    /// </summary>
    /// <remarks>
    /// The character file's <c>m_behaviorFilename</c> names it. This matters
    /// beyond starting the walk: a project's files each carry their own copy of
    /// the graph variables, and only the root's copy is the one the game binds.
    /// </remarks>
    public BehaviorFile? RootFile { get; }

    /// <summary>The generator the root file's <c>hkbBehaviorGraph</c> hangs everything off.</summary>
    public hkbGenerator? Root =>
        RootFile?.File.First<hkbBehaviorGraph>()?.m_rootGenerator;

    /// <summary>
    /// Reads a project's behaviour files as one graph.
    /// </summary>
    /// <param name="files">The project's behaviour files.</param>
    /// <param name="behaviorFilename">
    /// What the character file calls its graph, e.g. <c>Behaviors\0_Master.hkx</c>.
    /// Only the last segment is used, because the stored paths disagree about the
    /// folder: <c>Behaviors Wolf\</c> and <c>Behaviors\</c> both occur.
    /// </param>
    public static BehaviorGraph Read(IReadOnlyList<BehaviorFile> files, string? behaviorFilename)
    {
        var byName = new Dictionary<string, BehaviorFile>(StringComparer.OrdinalIgnoreCase);
        foreach (BehaviorFile file in files)
            if (Key(file.Name) is { } key) byName.TryAdd(key, file);

        BehaviorFile? root = null;
        if (Key(behaviorFilename) is { } wanted) byName.TryGetValue(wanted, out root);

        return new BehaviorGraph(files, root ?? (files.Count == 1 ? files[0] : null), byName);
    }

    /// <summary>
    /// The name of a variable, as the file holding the node spells it.
    /// </summary>
    /// <remarks>
    /// <strong>A variable index is per file.</strong> Every behaviour file carries
    /// its own <c>hkbBehaviorGraphData</c> with its own variable table, and index 3
    /// is <c>Speed</c> in one and something unrelated in the next. A binding is
    /// therefore only readable together with the file its node lives in, which a
    /// flat search cannot supply and <see cref="BehaviorStep.File"/> can -- resolve
    /// one against the wrong table and the answer is not an error, it is a
    /// different variable.
    /// </remarks>
    public static string? VariableName(BehaviorFile file, int index)
    {
        IList<string>? names = file.File
            .First<hkbBehaviorGraphData>()?.m_stringData?.m_variableNames;

        return names is null || index < 0 || index >= names.Count ? null : names[index];
    }

    /// <summary>The file a <c>hkbBehaviorReferenceGenerator</c> points at, if the project has it.</summary>
    public BehaviorFile? Resolve(string? behaviorName) =>
        Key(behaviorName) is { } key && _byName.TryGetValue(key, out BehaviorFile? file) ? file : null;

    /// <summary>
    /// The whole graph walked once, with every node's path back to the root.
    /// </summary>
    public BehaviorWalk Walk() => new([.. Visit()]);

    /// <summary>
    /// Every node reachable from <see cref="Root"/>, depth first, crossing into
    /// referenced files as the engine would.
    /// </summary>
    public IEnumerable<BehaviorStep> Visit() =>
        Root is null || RootFile is null ? [] : Visit(Root, RootFile);

    /// <summary>The same, from a node of your choosing.</summary>
    public IEnumerable<BehaviorStep> Visit(IHavokObject from, BehaviorFile file)
    {
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<BehaviorStep>();
        stack.Push(new BehaviorStep(from, file, null, "", -1, 0));

        while (stack.Count > 0)
        {
            BehaviorStep step = stack.Pop();
            if (!seen.Add(step.Node)) continue;

            yield return step;

            // A reference is a leaf in the packfile and an edge in the engine.
            if (step.Node is hkbBehaviorReferenceGenerator reference)
            {
                BehaviorFile? target = Resolve(reference.m_behaviorName);
                hkbGenerator? next = target?.File.First<hkbBehaviorGraph>()?.m_rootGenerator;

                if (target is not null && next is not null)
                    stack.Push(new BehaviorStep(
                        next, target, step.Node, nameof(reference.m_behaviorName), -1, step.Depth + 1));

                continue;
            }

            // Pushed in reverse so the first child is popped first.
            foreach ((string member, int index, IHavokObject child) in BehaviorEdges.Of(step.Node).Reverse())
                stack.Push(new BehaviorStep(child, step.File, step.Node, member, index, step.Depth + 1));
        }
    }

    /// <summary>A stored behaviour path reduced to the part that identifies it.</summary>
    private static string? Key(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;

        int cut = stored.LastIndexOfAny(['\\', '/']);
        string leaf = cut < 0 ? stored : stored[(cut + 1)..];
        int dot = leaf.LastIndexOf('.');

        return dot < 0 ? leaf : leaf[..dot];
    }
}
