using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Finding the generator a modifier belongs to.
/// </summary>
/// <remarks>
/// A modifier generates no pose. It runs alongside a generator and changes
/// variables or the pose that generator produced, so what it is in force over is
/// decided by what is <em>above</em> it, not below.
/// </remarks>
public sealed class ModifierOwnersTests
{
    /// <summary>Every modifier in the game belongs to a generator.</summary>
    /// <remarks>
    /// 3,159 of them over the 49 projects and not one is left over, so a modifier
    /// with no generator above it is a thing that does not happen in vanilla --
    /// worth knowing before writing code that tolerates it.
    /// </remarks>
    [CorpusFact]
    public void EveryModifierBelongsToAGenerator()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int modifiers = 0, owned = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            var owners = new ModifierOwners(walk);

            var all = walk.Select(s => s.Node).OfType<hkbModifier>().ToList();
            modifiers += all.Count;

            Assert.All(all, m => Assert.NotNull(owners.OwnerOf(m)));
            owned += owners.All.Count;
        }

        Assert.Equal(3159, modifiers);
        Assert.Equal(3159, owned);
    }

    /// <summary>
    /// A modifier is often not the generator's own, but nested inside other
    /// modifiers.
    /// </summary>
    /// <remarks>
    /// Only 1,330 of the 3,159 are attached directly as
    /// <c>hkbModifierGenerator.m_modifier</c>. The rest sit one, two or three
    /// links down, through <c>hkbModifierList</c>, <c>hkbEventDrivenModifier</c>
    /// and <c>BSModifyOnceModifier</c>, so anything that only looks at a
    /// generator's immediate modifier sees well under half of them.
    /// </remarks>
    [CorpusFact]
    public void MostModifiersSitBelowOtherModifiers()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var distance = new SortedDictionary<int, int>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (ModifierOwner owner in new ModifierOwners(ProjectVisitor.Visit(at.ProjectFile!)).All)
            {
                distance.TryGetValue(owner.Distance, out int count);
                distance[owner.Distance] = count + 1;
            }

        Assert.Equal(1330, distance[0]);   // the generator's own m_modifier
        Assert.Equal(1269, distance[1]);
        Assert.Equal(467, distance[2]);
        Assert.Equal(93, distance[3]);
        Assert.Equal(4, distance.Count);   // and never deeper
    }

    /// <summary>
    /// The climb asks whether the holder is a modifier, not which property held
    /// it.
    /// </summary>
    /// <remarks>
    /// <c>hkbEventDrivenModifier</c> declares no modifier property of its own --
    /// it inherits <c>m_modifier</c> from <c>hkbModifierWrapper</c> -- so a search
    /// written from the declared properties of each class misses every attachment
    /// through one. There are 257 of them.
    /// </remarks>
    [CorpusFact]
    public void AModifierInheritedFromABaseClassStillHoldsAChild()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int throughEventDriven = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (ModifierOwner owner in new ModifierOwners(ProjectVisitor.Visit(at.ProjectFile!)).All)
                if (owner.Through.Any(m => m is hkbEventDrivenModifier)) throughEventDriven++;

        Assert.Equal(257, throughEventDriven);

        // the property really is the base class's
        Assert.Equal(
            typeof(hkbModifierWrapper),
            typeof(hkbEventDrivenModifier).GetProperty("m_modifier")!.DeclaringType);
    }

    /// <summary>
    /// The speed sampler resolves to the locomotion root, which is the same answer
    /// <see cref="Locomotion"/> reaches from the other direction.
    /// </summary>
    /// <remarks>
    /// <c>Locomotion</c> looks down from every generator for a sampler in its
    /// modifiers; this looks up from a modifier for its generator. Two directions,
    /// same 44 pairs, so neither is relying on the other being right.
    /// </remarks>
    [CorpusFact]
    public void ClimbingFromTheSamplerFindsTheSameRootAsSearchingForIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int matched = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            var owners = new ModifierOwners(walk);

            foreach (LocomotionRoot root in Locomotion.RootsIn(walk))
            {
                Assert.Same(root.Generator, owners.OwnerOf(root.Sampler));
                matched++;
            }

            // and from the other side: every sampler climbs to a locomotion root
            foreach (BSSpeedSamplerModifier sampler in walk.Select(s => s.Node).OfType<BSSpeedSamplerModifier>())
                Assert.Contains(Locomotion.RootsIn(walk), r => ReferenceEquals(r.Generator, owners.OwnerOf(sampler)));
        }

        Assert.Equal(44, matched);
    }

    /// <summary>A generator's modifiers can be listed back from it.</summary>
    /// <remarks>
    /// The falmer's locomotion root runs six modifiers, of which the speed sampler
    /// is one, and they are reachable from the generator without re-walking.
    /// </remarks>
    [CorpusFact]
    public void AGeneratorCanListTheModifiersItRuns()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<ProjectStep> walk = [.. ProjectVisitor.Visit(cache.FindProjectFile("FalmerProject")!)];

        var owners = new ModifierOwners(walk);
        LocomotionRoot root = Assert.Single(Locomotion.RootsIn(walk));

        List<ModifierOwner> under = [.. owners.Under(root.Generator)];

        Assert.Contains(under, m => ReferenceEquals(m.Modifier, root.Sampler));
        Assert.All(under, m => Assert.Same(root.Generator, m.Generator));
        Assert.NotEmpty(under);
    }
}
