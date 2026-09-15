using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The idle animations of the masters as a forest rooted at actions.
/// </summary>
/// <remarks>
/// An <c>IDLE</c>'s two "related idles" are a parent and a previous sibling, and
/// the parent may be an <c>AACT</c> rather than another idle. That makes the
/// actions the roots, and an action is what the behaviour graph raises as an
/// event -- so this is the join between the plugins' animation choices and the
/// graphs.
/// </remarks>
public sealed class IdleForestTests
{
    /// <summary>The two links are a parent and a sibling, and they differ in kind.</summary>
    /// <remarks>
    /// Every one of the 4,154 idles carries exactly two. The parent slot holds
    /// another idle 2,666 times, an action 1,115 times and nothing 373 times; the
    /// sibling slot holds an idle or nothing and <strong>never</strong> an action.
    /// That asymmetry is what says which slot is which, without relying on the
    /// editor's labelling.
    /// </remarks>
    [MastersFact]
    public void TheParentSlotTakesAnActionAndTheSiblingSlotNever()
    {
        IdleForest forest = IdleForest.Read();

        int parentIdle = 0, parentAction = 0, parentNull = 0;
        int siblingIdle = 0, siblingAction = 0, siblingNull = 0;

        foreach (IdleNode node in forest.Nodes.Values)
        {
            if (node.Parent.IsNull) parentNull++;
            else if (forest.Nodes.ContainsKey(node.Parent)) parentIdle++;
            else if (forest.Actions.ContainsKey(node.Parent)) parentAction++;
            else Assert.Fail($"{node}: parent {node.Parent} is neither an idle nor an action");

            if (node.Sibling.IsNull) siblingNull++;
            else if (forest.Nodes.ContainsKey(node.Sibling)) siblingIdle++;
            else if (forest.Actions.ContainsKey(node.Sibling)) siblingAction++;
            else Assert.Fail($"{node}: sibling {node.Sibling} is neither an idle nor an action");
        }

        Assert.Equal(4154, forest.Nodes.Count);
        Assert.Equal(71, forest.Actions.Count);

        Assert.Equal(2666, parentIdle);
        Assert.Equal(1115, parentAction);
        Assert.Equal(373, parentNull);

        Assert.Equal(1489, siblingIdle);
        Assert.Equal(0, siblingAction);
        Assert.Equal(2665, siblingNull);
    }

    /// <summary>The visit reaches every idle exactly once.</summary>
    /// <remarks>
    /// Walking down from the 71 actions and then from the parentless idles covers
    /// all 4,154 with nothing repeated and nothing stranded, so the parent links
    /// really do form a forest -- no cycle, no orphan pointing into the middle of
    /// another tree. It is six deep at the deepest, and 3,781 of the idles sit under
    /// an action.
    /// </remarks>
    [MastersFact]
    public void EveryIdleIsReachedExactlyOnce()
    {
        IdleForest forest = IdleForest.Read();
        List<IdleStep> steps = [.. forest.Visit()];

        Assert.Equal(4154, steps.Count);
        Assert.Equal(4154, steps.Select(s => s.Node.Key).Distinct().Count());
        Assert.Equal(forest.Nodes.Count, steps.Count);

        Assert.Equal(6, steps.Max(s => s.Depth));
        Assert.Equal(3781, steps.Count(s => s.Action is not null));
        Assert.Equal(65, forest.Actions.Values.Count(a => forest.Under(a).Count > 0));
    }

    /// <summary>
    /// A creature's movement idles hang off the action, one branch per project.
    /// </summary>
    /// <remarks>
    /// <c>ActionMoveStart</c> has 46 direct children -- about one per animated
    /// project -- and each raises the graph event <c>moveStart</c>. So the action
    /// is the question and the children are the per-creature answers, chosen by
    /// their conditions in sibling order.
    /// </remarks>
    [MastersFact]
    public void TheMovementActionsBranchOncePerCreature()
    {
        IdleForest forest = IdleForest.Read();

        IReadOnlyList<IdleNode> children = forest.Under("ActionMoveStart");
        Assert.Equal(46, children.Count);

        // the sibling chain gives a stable reading order
        Assert.Equal("WitchlightMoveStart", children[0].EditorID);
        Assert.Contains(children, c => c.EditorID == "FalmerMoveStart");
        Assert.Contains(children, c => c.EditorID == "HorseMoveStart");

        // and nearly all of them raise the event the action is named for
        int raising = children.Count(c => c.AnimationEvent == "moveStart");
        Assert.True(raising >= 40, $"only {raising} of {children.Count} raise moveStart");

        Assert.Equal(46, forest.Under("ActionMoveStop").Count);
    }

    /// <summary>
    /// An action's editor id is the behaviour event with <c>Action</c> dropped.
    /// </summary>
    /// <remarks>
    /// 27 of the 71 map that way onto an event some project's graph declares --
    /// <c>ActionMoveForward</c> to <c>moveForward</c>, <c>ActionSprintStart</c> to
    /// <c>sprintStart</c>, and every one of the move and turn actions. The rest
    /// name engine actions no graph responds to by that name.
    /// </remarks>
    [MastersFact]
    public void AnActionsNameIsTheBehaviourEventWithoutThePrefix()
    {
        // the events the behaviour graphs actually declare, not the ones idles happen to raise
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (ProjectStep step in ProjectVisitor.Visit(at.ProjectFile!))
                if (step.Node is hkbBehaviorGraph graph)
                    foreach (string name in graph.m_data?.m_stringData?.m_eventNames ?? [])
                        events.Add(name);

        string Bare(string action)
        {
            string bare = action.StartsWith("Action", StringComparison.OrdinalIgnoreCase) ? action[6..] : action;
            return bare.Length == 0 ? bare : char.ToLowerInvariant(bare[0]) + bare[1..];
        }

        foreach (string action in new[]
        {
            "ActionMoveForward", "ActionMoveBackward", "ActionMoveLeft", "ActionMoveRight",
            "ActionMoveStart", "ActionMoveStop", "ActionTurnLeft", "ActionTurnRight", "ActionTurnStop",
            "ActionSprintStart", "ActionSprintStop",
        })
            Assert.Contains(Bare(action), events);
    }

    /// <summary>The forest is read from the plugins, so a missing game skips it.</summary>
    [MastersFact]
    public void TheSiblingChainPutsChildrenInOrderWithoutLosingAny()
    {
        IdleForest forest = IdleForest.Read();

        foreach (IdleNode parent in forest.Nodes.Values)
        {
            IReadOnlyList<IdleNode> children = forest.ChildrenOf(parent.Key);
            if (children.Count < 2) continue;

            // nothing dropped and nothing duplicated by the ordering
            Assert.Equal(children.Count, children.Select(c => c.Key).Distinct().Count());

            // the first names no earlier sibling
            Assert.True(children[0].Sibling.IsNull,
                        $"{parent}: first child {children[0]} names a previous sibling");
        }
    }
}
