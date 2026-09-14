using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Opening each project's root behaviour packfile and visiting it from its
/// <c>hkRootLevelContainer</c>.
/// </summary>
/// <remarks>
/// The whole chain in one place: locate the project, follow it to its character
/// file and on to the one behaviour file that names, open that with HKX2, take
/// the root container, and check that everything the container declares can
/// actually be reached by visiting.
/// </remarks>
public sealed class HavokVisitorTests
{
    /// <summary>
    /// Every named variant of every root behaviour file is reached by the visitor,
    /// and is the class the file says it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container is the only thing a packfile declares: a name, a class name,
    /// and a pointer, per variant. Everything else in the file hangs below those
    /// pointers, so a variant the visitor cannot reach would mean the traversal is
    /// wrong at the very first step.
    /// </para>
    /// <para>
    /// The shape is uniform across the shipped game: each of the 49 root behaviour
    /// files declares exactly one variant and it is an <c>hkbBehaviorGraph</c>.
    /// The class name is a string written beside the pointer and could in
    /// principle disagree with what is actually there, so it is checked rather
    /// than trusted -- it agrees 49 times out of 49.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void EveryRootBehaviourFileDeclaresOneGraphAndTheVisitorReachesIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int files = 0, variants = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            BehaviorRoot root = Assert.IsType<BehaviorRoot>(BehaviorRoot.Of(at.ProjectFile!));

            // opened through HKX2, as a packfile, not through the project model
            HavokFile file = HavokFile.Load(root.BehaviorFile);
            hkRootLevelContainer container = file.Root;

            var reached = new HashSet<IHavokObject>(
                HavokVisitor.Visit(container).Select(s => s.Node), ReferenceEqualityComparer.Instance);

            hkRootLevelContainerNamedVariant variant =
                Assert.Single(HavokVisitor.NamedVariants(container));

            Assert.NotNull(variant.m_variant);
            Assert.Equal("hkbBehaviorGraph", variant.m_className);
            Assert.Equal(variant.m_className, variant.m_variant.GetType().Name);
            Assert.Contains(variant.m_variant, reached);

            files++;
            variants++;
        }

        Assert.Equal(49, files);
        Assert.Equal(49, variants);
    }

    /// <summary>
    /// The same holds for every behaviour file in the corpus, not just the 49 the
    /// characters enter at.
    /// </summary>
    /// <remarks>
    /// 125 files, one <c>hkbBehaviorGraph</c> each. A referenced graph is a
    /// complete packfile in its own right and not a fragment, which is why
    /// crossing a <c>hkbBehaviorReferenceGenerator</c> means starting again at
    /// another container's root generator.
    /// </remarks>
    [CorpusFact]
    public void EveryBehaviourFileInTheCorpusHasTheSameShape()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int files = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (BehaviorFile behavior in cache.OpenActor(at.Name)!.Behaviors)
            {
                hkRootLevelContainerNamedVariant variant =
                    Assert.Single(HavokVisitor.NamedVariants(behavior.File.Root));

                Assert.IsType<hkbBehaviorGraph>(variant.m_variant);
                files++;
            }

        Assert.Equal(125, files);
    }

    /// <summary>
    /// The visitor reaches every object in the file, checked against an
    /// independent traversal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HavokFile"/> flattens a packfile on load by its own walk, over
    /// every property of every object rather than over the Havok-typed ones. That
    /// is a different piece of code answering the same question, so the two
    /// agreeing is worth more than either agreeing with itself: 218,330 objects
    /// across the 125 behaviour files, and the two traversals reach exactly the
    /// same set.
    /// </para>
    /// <para>
    /// If the reflection that finds a node's children ever misses a property
    /// shape, this is the test that says so.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheVisitorReachesEveryObjectTheFileHolds()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int objects = 0;
        var unreached = new List<string>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (BehaviorFile behavior in cache.OpenActor(at.Name)!.Behaviors)
            {
                var reached = new HashSet<IHavokObject>(
                    HavokVisitor.Visit(behavior.File.Root).Select(s => s.Node),
                    ReferenceEqualityComparer.Instance);

                foreach (IHavokObject held in behavior.File.Objects)
                {
                    objects++;
                    if (!reached.Contains(held))
                        unreached.Add($"{behavior.Name}: {held.GetType().Name}");
                }

                Assert.Equal(behavior.File.Objects.Count, reached.Count);
            }

        Assert.Equal(218330, objects);
        Assert.Empty(unreached);
    }

    /// <summary>
    /// The visit carries where each object came from, so a file can be read as a
    /// tree rather than a bag.
    /// </summary>
    [CorpusFact]
    public void AVisitedObjectKnowsWhatHoldsIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        BehaviorRoot root = BehaviorRoot.Of(cache.FindProjectFile("FalmerProject")!)!.Value;
        HavokFile file = HavokFile.Load(root.BehaviorFile);

        List<HavokStep> steps = [.. HavokVisitor.Visit(file.Root)];

        // the container is the root, reached from nothing
        HavokStep first = steps[0];
        Assert.Same(file.Root, first.Node);
        Assert.Null(first.Parent);
        Assert.Equal(0, first.Depth);

        // the graph hangs off the container's variant list, by index
        HavokStep graph = steps.First(s => s.Node is hkbBehaviorGraph);
        HavokStep named = steps.First(s => s.Node is hkRootLevelContainerNamedVariant);

        Assert.Equal("m_namedVariants", named.Member);
        Assert.Equal(0, named.Index);
        Assert.Same(file.Root, named.Parent);

        Assert.Equal("m_variant", graph.Member);
        Assert.Same(named.Node, graph.Parent);
    }
}
