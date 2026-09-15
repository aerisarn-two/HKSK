using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Finding where each project's locomotion is rooted, by visiting it from its
/// packfile.
/// </summary>
/// <remarks>
/// <c>BSSpeedSamplerModifier</c> is the only node that reads the speed table, and
/// it is a modifier rather than a generator: it runs alongside one and writes the
/// sampled speed into a variable the blends below read. So the locomotion root is
/// the generator it modifies, and the subtree its output is in force over is that
/// generator's own.
/// </remarks>
public sealed class LocomotionRootTests
{
    /// <summary>
    /// Every speed sampler in the game is the modifier of a
    /// <c>hkbModifierGenerator</c>, and nothing else holds one.
    /// </summary>
    /// <remarks>
    /// 44 samplers over the 49 projects, all reached by visiting from the project
    /// packfile, and all 44 in a <c>hkbModifierList</c> that is the
    /// <c>m_modifier</c> of a <c>hkbModifierGenerator</c>. Not one sits anywhere
    /// else, so the locomotion root is found structurally and not by a name or a
    /// file.
    /// </remarks>
    [CorpusFact]
    public void EverySpeedSamplerIsTheModifierOfAGenerator()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int samplers = 0, roots = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];

            var found = walk.Select(s => s.Node).OfType<BSSpeedSamplerModifier>().ToList();
            List<LocomotionRoot> located = [.. Locomotion.RootsIn(walk)];

            // one root per sampler, and the root really does carry that sampler
            Assert.Equal(found.Count, located.Count);
            Assert.All(located, r =>
            {
                Assert.Contains(r.Sampler, found);
                hkbModifierList list = Assert.IsType<hkbModifierList>(r.Generator.m_modifier);
                Assert.Contains(r.Sampler, list.m_modifiers);
            });

            samplers += found.Count;
            roots += located.Count;
        }

        Assert.Equal(44, samplers);
        Assert.Equal(44, roots);
    }

    /// <summary>
    /// 41 of the 49 projects have one, and the eight that do not are the ones that
    /// do not walk.
    /// </summary>
    /// <remarks>
    /// The flame and storm atronachs, the chaurus flyer, the dragon, the dragon
    /// priest, the ice wraith, the wisp and the witchlight float or fly, and none
    /// of them reads the speed table at all. That is why their speed blocks are
    /// not worth rebuilding rather than a failure to rebuild them.
    /// </remarks>
    [CorpusFact]
    public void EightProjectsHaveNoLocomotionRootAtAll()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var without = cache.LocateSpeedProjects()
            .Where(at => !Locomotion.RootsOf(at.ProjectFile!).Any())
            .Select(at => at.Name)
            .Order()
            .ToList();

        Assert.Equal(
            ["AtronachFlame", "AtronachStormProject", "ChaurusFlyer", "Dragon_Priest",
             "DragonProject", "IceWraithProject", "WispProject", "WitchlightProject"],
            without);
    }

    /// <summary>
    /// The name of the root is not a way to find it: five spellings for one role.
    /// </summary>
    /// <remarks>
    /// <c>RootModifierGenerator</c> 38 times, then <c>Root Mod Gen</c> three times,
    /// and <c>RootModGen</c>, <c>NetchRootModifierGenerator</c> and
    /// <c>MG_RootState</c> once each. Anything matching on the name loses six of
    /// the 44, and matching on a token loses the netch and the last one outright.
    /// </remarks>
    [CorpusFact]
    public void TheRootIsNotFoundByItsName()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var names = cache.LocateSpeedProjects()
            .SelectMany(at => Locomotion.RootsOf(at.ProjectFile!))
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(5, names.Count);
        Assert.Equal(38, names["RootModifierGenerator"]);
        Assert.Equal(3, names["Root Mod Gen"]);
        Assert.Equal(1, names["RootModGen"]);
        Assert.Equal(1, names["NetchRootModifierGenerator"]);
        Assert.Equal(1, names["MG_RootState"]);
    }

    /// <summary>
    /// What the sampler's output is in force over is a whole behaviour, not a
    /// blend.
    /// </summary>
    /// <remarks>
    /// 41 of the 44 roots generate a <c>hkbStateMachine</c> -- usually the one
    /// called <c>RootBehavior</c> -- and the other three generate the player's
    /// <c>RootBoneSwitch_MSG</c>, a manual selector. So the sampled speed is
    /// written once at the top of everything the creature does, and the blends
    /// that read it are wherever they happen to be below that.
    /// </remarks>
    [CorpusFact]
    public void TheRootGeneratesTheCreaturesWholeBehaviour()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var kinds = cache.LocateSpeedProjects()
            .SelectMany(at => Locomotion.RootsOf(at.ProjectFile!))
            .GroupBy(r => r.Subtree?.GetType().Name ?? "(none)")
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(41, kinds["hkbStateMachine"]);
        Assert.Equal(3, kinds["hkbManualSelectorGenerator"]);
        Assert.Equal(2, kinds.Count);
    }

    /// <summary>
    /// The player has two, and the second is only reachable across a file
    /// boundary.
    /// </summary>
    /// <remarks>
    /// Both player sexes and the first-person rig carry one root in
    /// <c>0_Master</c> for their own locomotion and a second in
    /// <c>horsebehavior</c> for riding -- which is where their speed table's keys
    /// 60 to 63 come from, the mounted movement types. A reader that stopped at
    /// the root behaviour file would never see it, and that is the whole reason
    /// the visitor crosses references.
    /// </remarks>
    [CorpusFact]
    public void ThePlayersSecondRootIsInTheHorsesGraph()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        foreach (string who in new[] { "DefaultMale", "DefaultFemale", "FirstPerson" })
        {
            List<LocomotionRoot> roots = [.. Locomotion.RootsOf(cache.FindProjectFile(who)!)];

            Assert.Equal(2, roots.Count);
            Assert.Equal(["0_master", "horsebehavior"], roots.Select(r => r.FileName).Order());

            LocomotionRoot mounted = roots.First(r => r.FileName == "horsebehavior");
            Assert.Equal("MountBehavior", (mounted.Subtree as hkbNode)?.m_name);
        }
    }
}
