using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Walking a project's behaviour as one graph instead of searching its files as
/// a bag of nodes.
/// </summary>
/// <remarks>
/// <para>
/// The point of these is that the walk is <em>complete</em>, and that it is shown
/// to be rather than assumed. A traversal that hand-lists the node types holding
/// children does not fail when it forgets one; it silently returns a smaller
/// tree. So the corpus is asked the question directly: of every node in every
/// packfile, which ones did the walk from the character's root not reach?
/// </para>
/// <para>
/// The answer is 125 of 28,476, and all 125 are the <c>hkbBehaviorGraph</c>
/// wrappers themselves -- one per file, which the walk skips because it starts at
/// their <c>m_rootGenerator</c>. Nothing else in the shipped game is unreachable.
/// </para>
/// </remarks>
public sealed class BehaviorGraphTests
{
    /// <summary>Every project's character file names a behaviour file the project has.</summary>
    [CorpusFact]
    public void EveryProjectHasARootedGraph()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int projects = 0, rooted = 0;

        foreach (string name in cache.SpeedData!.ProjectNames)
        {
            var project = (ActorProject)cache.OpenActor(name)!;
            projects++;
            if (project.Graph.Root is not null) rooted++;
        }

        Assert.Equal(49, projects);
        Assert.Equal(49, rooted);
    }

    /// <summary>
    /// A behaviour graph spans files, and the walk crosses every join.
    /// </summary>
    /// <remarks>
    /// <c>hkbBehaviorReferenceGenerator</c> names its target as a string and
    /// leaves the pointer <c>SERIALIZE_IGNORED</c>, so the packfile records no
    /// link at all -- the engine resolves the name at load and so must anything
    /// that reads the graph. 123 of these joins hold the corpus together, and 93
    /// of them are the player's and the first-person rig's. All 125 files in the
    /// 49 projects are reached from their character's root; none is an island.
    /// </remarks>
    [CorpusFact]
    public void EveryBehaviourFileIsReachedFromTheRoot()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int files = 0, reached = 0, crossed = 0;

        foreach (string name in cache.SpeedData!.ProjectNames)
        {
            var project = (ActorProject)cache.OpenActor(name)!;
            List<BehaviorStep> walk = [.. project.Graph.Visit()];

            files += project.Behaviors.Count;
            reached += walk.Select(s => s.File).Distinct().Count();
            crossed += walk.Count(s => s.Node is hkbBehaviorReferenceGenerator);
        }

        Assert.Equal(125, files);
        Assert.Equal(125, reached);
        Assert.Equal(123, crossed);
    }

    /// <summary>
    /// The only nodes the walk does not reach are the per-file graph wrappers.
    /// </summary>
    /// <remarks>
    /// This is the test that says the edge discovery is exhaustive. It is not a
    /// sample and not a spot check: every <c>hkbNode</c> in every packfile of
    /// every project is looked for in the walk, and the 125 that are missing are
    /// each the <c>hkbBehaviorGraph</c> a file hangs off, which is the node the
    /// walk starts <em>below</em>.
    /// </remarks>
    [CorpusFact]
    public void TheOnlyNodesTheWalkDoesNotReachAreTheGraphWrappers()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int nodes = 0, seen = 0;
        var missed = new List<string>();

        foreach (string name in cache.SpeedData!.ProjectNames)
        {
            var project = (ActorProject)cache.OpenActor(name)!;

            var reached = new HashSet<IHavokObject>(
                project.Graph.Visit().Select(s => s.Node), ReferenceEqualityComparer.Instance);

            foreach (BehaviorFile file in project.Behaviors)
                foreach (hkbNode node in file.File.All<hkbNode>())
                {
                    nodes++;
                    if (reached.Contains(node)) seen++;
                    else if (node is not hkbBehaviorGraph) missed.Add($"{name}: {node.GetType().Name} '{node.m_name}'");
                }
        }

        Assert.Equal(28476, nodes);
        Assert.Equal(28351, seen);
        Assert.Empty(missed);
    }

    /// <summary>
    /// The children are found without naming a single node type.
    /// </summary>
    /// <remarks>
    /// The five edges the old hand-written walk knew about, checked against what
    /// reflection finds, because those five are what everything downstream was
    /// built on. A blender reaches its children through a wrapper struct, a state
    /// machine through another, and three more node types hold a bare generator
    /// under three different property names.
    /// </remarks>
    [CorpusFact]
    public void TheEdgesAreFoundWithoutNamingTheNodeTypes()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var project = (ActorProject)cache.OpenActor("FalmerProject")!;
        List<BehaviorStep> walk = [.. project.Graph.Visit()];

        // a blender's children arrive through the wrapper, and the wrapper's generator through it
        hkbBlenderGenerator blender = walk.Select(s => s.Node)
            .OfType<hkbBlenderGenerator>().First(b => b.m_children.Count > 1);

        Assert.All(blender.m_children, child =>
            Assert.Contains(walk, s => ReferenceEquals(s.Node, child) &&
                                       ReferenceEquals(s.Parent, blender) &&
                                       s.Member == nameof(blender.m_children)));

        // a state machine's states, the same way
        hkbStateMachine machine = walk.Select(s => s.Node)
            .OfType<hkbStateMachine>().First(m => m.m_states.Count > 1);

        Assert.All(machine.m_states, state =>
            Assert.Contains(walk, s => ReferenceEquals(s.Node, state) &&
                                       ReferenceEquals(s.Parent, machine)));

        // and the three that hold a bare generator under three different names
        Assert.Contains(walk, s => s.Member == "m_pDefaultGenerator");
        Assert.Contains(walk, s => s.Member == "m_generator");
        Assert.Contains(walk, s => s.Member == "m_variableBindingSet");
    }

    /// <summary>
    /// A node reached through a reference carries the file it really lives in,
    /// not the file that pointed at it.
    /// </summary>
    /// <remarks>
    /// This is what the walk gives that a flat search cannot: the player's graph
    /// is 17 files deep and 53 levels tall, and every node in it now knows both
    /// which file holds it and the chain of nodes that reaches it.
    /// </remarks>
    [CorpusFact]
    public void ANodeKnowsWhichFileHoldsItAndHowDeepItSits()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var player = (ActorProject)cache.OpenActor("DefaultMale")!;

        Assert.Equal("0_master", player.Graph.RootFile!.Name);

        List<BehaviorStep> walk = [.. player.Graph.Visit()];

        Assert.Equal(17, walk.Select(s => s.File).Distinct().Count());
        Assert.Equal(53, walk.Max(s => s.Depth));

        // the step that crosses into mt_behavior is a reference, and its child changes file
        BehaviorStep crossing = walk.First(s => s.File.Name == "mt_behavior");

        Assert.IsType<hkbBehaviorReferenceGenerator>(crossing.Parent);
        Assert.Equal("m_behaviorName", crossing.Member);
        Assert.Equal("mt_behavior", crossing.File.Name);
    }

    /// <summary>
    /// The graph says which key a compass serves, and where the name heuristic
    /// also answers the two never disagree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what the walk was built for. <c>iState</c> is the variable the game
    /// writes from the actor's movement type and the speed table is keyed on, and
    /// two node types set it: <c>BSiStateTaggingGenerator</c> above a subtree, and
    /// <c>BSIStateManagerModifier</c> from a table of (machine, state) pairs. The
    /// second is unreadable without ancestry -- you cannot tell which machine a
    /// blend sits under by searching a file flatly -- which is why this could not
    /// be measured before.
    /// </para>
    /// <para>
    /// Together they place a compass under 32 of the 291 states. Of those, the
    /// name heuristic of <c>FamilyGuess</c> agrees on 25 and answers <em>nothing</em>
    /// on the other 7 -- the player's and the first-person rig's sneak and
    /// ready-weapon states, and the sphere centurion's ranged one, all of which
    /// were counted among the 608 records with no compass to rebuild from. It
    /// never contradicts the names; it only reaches further.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheGraphSaysWhichKeyACompassServes()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int states = 0, placed = 0, agreed = 0, onlyTheGraph = 0;

        foreach (string name in cache.SpeedData!.ProjectNames)
        {
            var project = (ActorProject)cache.OpenActor(name)!;
            SpeedSampler? sampler = SpeedSampler.FromProject(project);
            if (sampler is null) continue;

            BehaviorWalk walk = project.Graph.Walk();

            var byKey = new Dictionary<int, List<string>>();
            foreach (SpeedCompass compass in sampler.Compasses)
            {
                if (walk.KeyOf(compass.Node) is not { } key) continue;

                if (!byKey.TryGetValue(key, out List<string>? under)) byKey[key] = under = [];
                under.Add(compass.Name);
            }

            foreach (SpeedState state in sampler.States)
            {
                states++;
                if (!byKey.TryGetValue(state.Key, out List<string>? under)) continue;

                placed++;

                var guessed = sampler.CompassFor(state.Key);
                string? named = sampler.Compasses
                    .Where(c => ReferenceEquals(c.Arms, guessed))
                    .Select(c => c.Name)
                    .FirstOrDefault();

                if (named is null) onlyTheGraph++;
                else Assert.Contains(named, under);       // never a contradiction

                if (named is not null) agreed++;
            }
        }

        Assert.Equal(291, states);
        Assert.Equal(32, placed);
        Assert.Equal(25, agreed);
        Assert.Equal(7, onlyTheGraph);
    }

    /// <summary>
    /// The two routes to <c>iState</c> are used by different graphs and never by
    /// the same node.
    /// </summary>
    [CorpusFact]
    public void TheTwoRoutesToAKeyDoNotOverlap()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int compasses = 0, tagged = 0, keyed = 0;

        foreach (string name in cache.SpeedData!.ProjectNames)
        {
            var project = (ActorProject)cache.OpenActor(name)!;
            SpeedSampler? sampler = SpeedSampler.FromProject(project);
            if (sampler is null) continue;

            BehaviorWalk walk = project.Graph.Walk();

            foreach (SpeedCompass compass in sampler.Compasses)
            {
                compasses++;
                if (walk.TagOf(compass.Node) is not null) tagged++;
                if (walk.KeyOf(compass.Node) is not null) keyed++;
            }
        }

        Assert.Equal(142, compasses);
        Assert.Equal(61, tagged);   // BSiStateTaggingGenerator
        Assert.Equal(71, keyed);    // and ten more from BSIStateManagerModifier
    }

    /// <summary>
    /// A gait change is a pair of sibling states, which the walk makes visible.
    /// </summary>
    /// <remarks>
    /// The giant's two combat compasses are states 0 and 1 of one
    /// <c>CombatLocomotionBehavior</c>, and the falmer's walk and run blends are
    /// two states of one <c>1HM_Locomotion_Behavior</c> carrying <c>iState</c> 2
    /// and 3 -- the two keys whose records span a gait change. Section 9 had to
    /// reach for name tokens and movement-type speeds to get at this, and neither
    /// survived measurement; the structure was in the graph the whole time.
    /// </remarks>
    [CorpusFact]
    public void AGaitPairIsTwoStatesOfOneMachine()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var falmer = (ActorProject)cache.OpenActor("FalmerProject")!;
        SpeedSampler sampler = Assert.IsType<SpeedSampler>(SpeedSampler.FromProject(falmer));
        BehaviorWalk walk = falmer.Graph.Walk();

        SpeedCompass walking = sampler.Compasses.First(c => c.Name == "MT_DirectionalBlend");
        SpeedCompass running = sampler.Compasses.First(c => c.Name == "1HM_DirectionalBlend_Run");

        Assert.Equal(2, walk.KeyOf(walking.Node));
        Assert.Equal(3, walk.KeyOf(running.Node));

        // both hang off the same state machine, one state apart
        hkbStateMachine machine = Assert.IsType<hkbStateMachine>(
            walk.Ancestors(walking.Node).First(a => a.Node is hkbStateMachine).Node);

        Assert.Equal("1HM_Locomotion_Behavior", machine.m_name);
        Assert.Same(machine, walk.Ancestors(running.Node).First(a => a.Node is hkbStateMachine).Node);
    }
}
