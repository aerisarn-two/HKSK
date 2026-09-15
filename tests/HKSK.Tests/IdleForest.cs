using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace HKSK.Tests;

/// <summary>
/// One idle animation, as the forest sees it.
/// </summary>
/// <param name="Key">Its form key.</param>
/// <param name="EditorID">Its editor id, which is how everything refers to it.</param>
/// <param name="AnimationEvent">The behaviour event it raises, when it raises one.</param>
/// <param name="File">The animation file it plays, when it plays one.</param>
/// <param name="Parent">
/// What it hangs off: another idle, or an <c>AACT</c> action at the top of a tree.
/// Null form key when it hangs off nothing.
/// </param>
/// <param name="Sibling">
/// The idle checked immediately <em>before</em> this one among its parent's
/// children. Never an action.
/// </param>
public readonly record struct IdleNode(
    FormKey Key,
    string? EditorID,
    string? AnimationEvent,
    string? File,
    FormKey Parent,
    FormKey Sibling)
{
    public override string ToString() => EditorID ?? Key.ToString();
}

/// <summary>One node reached while visiting, with how it was reached.</summary>
/// <param name="Node">The idle.</param>
/// <param name="Action">The action at the root of its tree, when it has one.</param>
/// <param name="Depth">How far below the root it sits; a tree's root is 0.</param>
/// <param name="Index">Its position among its parent's children, in sibling order.</param>
public readonly record struct IdleStep(IdleNode Node, string? Action, int Depth, int Index)
{
    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Node}" + (Action is null ? "" : $"  [{Action}]");
}

/// <summary>
/// The idle animations of the masters as a forest, rooted at the actions that
/// trigger them.
/// </summary>
/// <remarks>
/// <para>
/// An <c>IDLE</c> record carries exactly two links and Bethesda's editor calls
/// them "related idles", which undersells them: the first is a <strong>parent</strong>
/// and the second a <strong>previous sibling</strong>, so together they describe a
/// forest with ordered children. Over the five masters the split is clean -- the
/// parent slot holds another idle 2,666 times, an <c>AACT</c> action 1,115 times
/// and nothing 373 times, while the sibling slot holds an idle or nothing and
/// <em>never</em> an action.
/// </para>
/// <para>
/// The actions are what ties the forest to the behaviour graphs: 65 of the 71
/// <c>AACT</c> records root at least one tree, and an action's editor id maps to a
/// behaviour event by dropping <c>Action</c> and lowering the first letter --
/// <c>ActionMoveStart</c> is the graph's <c>moveStart</c>. So an idle tree is the
/// set of animations the game may pick when it raises that event, and the
/// per-creature branching happens below the action: <c>ActionMoveStart</c> has 46
/// direct children, roughly one for each animated project.
/// </para>
/// <para>
/// The sibling chain runs <em>backwards</em> -- each child names the one checked
/// before it -- so the first child is the one naming nobody and the reading order
/// is recovered by walking the chain forwards. That order matters: the engine takes
/// the first child whose conditions pass.
/// </para>
/// </remarks>
public sealed class IdleForest
{
    private readonly Dictionary<FormKey, List<IdleNode>> _children;

    private IdleForest(
        Dictionary<FormKey, IdleNode> nodes,
        Dictionary<FormKey, string> actions,
        Dictionary<FormKey, List<IdleNode>> children)
    {
        Nodes = nodes;
        Actions = actions;
        _children = children;
    }

    /// <summary>Every idle, by form key, after the load order has been applied.</summary>
    public IReadOnlyDictionary<FormKey, IdleNode> Nodes { get; }

    /// <summary>Every action, by form key, with its editor id.</summary>
    public IReadOnlyDictionary<FormKey, string> Actions { get; }

    /// <summary>Reads the masters and builds the forest.</summary>
    public static IdleForest Read()
    {
        var nodes = new Dictionary<FormKey, IdleNode>();
        var actions = new Dictionary<FormKey, string>();

        foreach (string master in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, master);
            if (!File.Exists(path)) continue;

            using var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);

            foreach (IActionRecordGetter action in mod.Actions)
                actions[action.FormKey] = action.EditorID ?? action.FormKey.ToString();

            foreach (IIdleAnimationGetter idle in mod.IdleAnimations)
                nodes[idle.FormKey] = new IdleNode(
                    idle.FormKey,
                    idle.EditorID,
                    string.IsNullOrEmpty(idle.AnimationEvent) ? null : idle.AnimationEvent,
                    idle.Filename?.GivenPath,
                    Link(idle, 0),
                    Link(idle, 1));
        }

        return new IdleForest(nodes, actions, Group(nodes));

        static FormKey Link(IIdleAnimationGetter idle, int slot) =>
            slot < idle.RelatedIdles.Count && !idle.RelatedIdles[slot].IsNull
                ? idle.RelatedIdles[slot].FormKey
                : FormKey.Null;
    }

    /// <summary>
    /// The children of a node or an action, in the order the engine reads them.
    /// </summary>
    public IReadOnlyList<IdleNode> ChildrenOf(FormKey parent) =>
        _children.TryGetValue(parent, out List<IdleNode>? found) ? found : [];

    /// <summary>The trees rooted at an action, by its editor id.</summary>
    public IReadOnlyList<IdleNode> Under(string action)
    {
        foreach ((FormKey key, string name) in Actions)
            if (string.Equals(name, action, StringComparison.OrdinalIgnoreCase))
                return ChildrenOf(key);

        return [];
    }

    /// <summary>
    /// Every idle reachable from the actions and from the parentless roots, depth
    /// first, each once.
    /// </summary>
    public IEnumerable<IdleStep> Visit()
    {
        var seen = new HashSet<FormKey>();

        foreach ((FormKey key, string action) in Actions.OrderBy(a => a.Value, StringComparer.Ordinal))
            foreach (IdleStep step in Visit(key, action, seen))
                yield return step;

        // idles whose parent slot is empty are roots of their own
        foreach (IdleStep step in Visit(FormKey.Null, null, seen))
            yield return step;
    }

    /// <summary>The subtree below one node or action.</summary>
    public IEnumerable<IdleStep> Visit(FormKey root, string? action = null) =>
        Visit(root, action, []);

    private IEnumerable<IdleStep> Visit(FormKey root, string? action, HashSet<FormKey> seen)
    {
        var stack = new Stack<(IdleNode Node, int Depth, int Index)>();

        IReadOnlyList<IdleNode> top = ChildrenOf(root);
        for (int i = top.Count - 1; i >= 0; i--) stack.Push((top[i], 0, i));

        while (stack.Count > 0)
        {
            (IdleNode node, int depth, int index) = stack.Pop();
            if (!seen.Add(node.Key)) continue;

            yield return new IdleStep(node, action, depth, index);

            IReadOnlyList<IdleNode> children = ChildrenOf(node.Key);
            for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], depth + 1, i));
        }
    }

    /// <summary>
    /// Groups the idles under their parents and puts each group into sibling order.
    /// </summary>
    /// <remarks>
    /// The chain names the <em>previous</em> sibling, so it is followed forwards
    /// from the child naming nobody. A group whose chain is broken or cyclic keeps
    /// the members the chain could not place, appended in form order, so nothing is
    /// silently lost.
    /// </remarks>
    private static Dictionary<FormKey, List<IdleNode>> Group(Dictionary<FormKey, IdleNode> nodes)
    {
        var grouped = new Dictionary<FormKey, List<IdleNode>>();

        foreach (IdleNode node in nodes.Values)
        {
            if (!grouped.TryGetValue(node.Parent, out List<IdleNode>? list))
                grouped[node.Parent] = list = [];

            list.Add(node);
        }

        foreach ((FormKey parent, List<IdleNode> group) in grouped)
        {
            var next = new Dictionary<FormKey, IdleNode>();
            foreach (IdleNode node in group)
                if (!node.Sibling.IsNull) next.TryAdd(node.Sibling, node);

            var ordered = new List<IdleNode>(group.Count);
            var placed = new HashSet<FormKey>();

            foreach (IdleNode first in group.Where(n => n.Sibling.IsNull))
            {
                IdleNode at = first;
                while (placed.Add(at.Key))
                {
                    ordered.Add(at);
                    if (!next.TryGetValue(at.Key, out IdleNode after)) break;
                    at = after;
                }
            }

            foreach (IdleNode node in group)
                if (placed.Add(node.Key)) ordered.Add(node);

            grouped[parent] = ordered;
        }

        return grouped;
    }
}
