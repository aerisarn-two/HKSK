using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Whether moving between locomotion states changes which animations play.
/// </summary>
/// <remarks>
/// The speed table is keyed on <c>iState</c>, and <c>iState</c> is what picks the
/// locomotion state below the generator carrying the speed sampler. If those
/// states shared their animations there would be nothing for separate blocks to
/// record; these measure that they do not.
/// </remarks>
public sealed class LocomotionAnimationTests
{
    /// <summary>The animations reachable below each locomotion state of a project.</summary>
    private static List<(string Name, HashSet<string> Clips)> ClipsPerState(ProjectWalk walk)
    {
        var variables = new ProjectVariables(walk.Steps);

        // one pass: every clip, tagged with the blenders above it
        var under = new Dictionary<IHavokObject, HashSet<string>>(ReferenceEqualityComparer.Instance);

        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not hkbClipGenerator clip) continue;

            foreach (ProjectStep above in walk.Ancestors(clip))
            {
                if (above.Node is not hkbBlenderGenerator) continue;

                if (!under.TryGetValue(above.Node, out HashSet<string>? clips))
                    under[above.Node] = clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                clips.Add(clip.m_animationName);
            }
        }

        var states = new List<(string, HashSet<string>)>();
        foreach (LocomotionState state in LocomotionStates.In(walk, variables))
        {
            var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpeedConsumer blend in state.Blends)
                if (under.TryGetValue(blend.Node, out HashSet<string>? found)) clips.UnionWith(found);

            states.Add(($"{state.Machine.m_name}#{state.State.m_stateId}", clips));
        }

        return states;
    }

    /// <summary>
    /// A creature's locomotion states almost always draw on entirely different
    /// animations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Over the 21 creature projects with more than one locomotion state, 137 of
    /// the 149 pairs share <strong>no</strong> animation at all. So switching
    /// <c>iState</c> does not merely re-blend the same clips at a different speed:
    /// it swaps the clip set wholesale, which is why each key needs its own curve.
    /// </para>
    /// <para>
    /// Six pairs are exceptions worth knowing, because a state that borrows another's
    /// animations produces the same curve and needs no block of its own:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the frost atronach's combat and non-combat locomotion are
    /// the same eight clips -- and its <c>AtronachFrostBlocking</c> movement type
    /// ships no block;</description></item>
    /// <item><description>the draugr's magic-equip stance reuses the hand-to-hand
    /// set exactly, in both draugr projects;</description></item>
    /// <item><description>the troll's <c>MT</c> and <c>H2H_Idle</c> states are the
    /// same thirteen clips;</description></item>
    /// <item><description>two of the werewolf's attack-blend states are duplicated
    /// between <c>Behavior16</c> and <c>MovementBehavior00</c>.</description></item>
    /// </list>
    /// </remarks>
    [CorpusFact]
    public void ACreaturesLocomotionStatesUseDifferentAnimations()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int pairs = 0, disjoint = 0, identical = 0, partial = 0, projects = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            if (at.Name is "DefaultMale" or "DefaultFemale" or "FirstPerson") continue;

            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            var states = ClipsPerState(walk);
            if (states.Count < 2) continue;
            projects++;

            for (int i = 0; i < states.Count; i++)
                for (int j = i + 1; j < states.Count; j++)
                {
                    int shared = states[i].Clips.Intersect(states[j].Clips).Count();
                    pairs++;

                    if (shared == 0) disjoint++;
                    else if (shared == states[i].Clips.Count && shared == states[j].Clips.Count) identical++;
                    else partial++;
                }
        }

        Assert.Equal(21, projects);
        Assert.Equal(149, pairs);
        Assert.Equal(137, disjoint);
        Assert.Equal(6, identical);
        Assert.Equal(6, partial);
    }

    /// <summary>
    /// The gait pairs in particular swap their clips entirely.
    /// </summary>
    /// <remarks>
    /// The falmer's walk and run states, the giant's combat walk and run, the
    /// benthic lurker's, and the quadrupeds' forward walk and run: every one of
    /// them is two sets of animations with nothing in common. A gait change is a
    /// different set of clips, not the same clips played faster, which is what makes
    /// the two halves of a gait-spanning record so far apart.
    /// </remarks>
    [CorpusFact]
    public void AGaitPairSwapsItsAnimationsEntirely()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        foreach ((string project, string machine) in new[]
        {
            ("FalmerProject", "1HM_Locomotion_Behavior"),
            ("GiantProject", "CombatLocomotionBehavior"),
            ("BenthicLurkerProject", "CombatLocomotionBehavior"),
            ("DogProject", "ForwardLocomotionBehavior"),
            ("BearProject", "ForwardLocomotionBehavior"),
            ("DeerProject", "ForwardLocomotionBehavior"),
        })
        {
            var states = ClipsPerState(ProjectWalk.Of(cache.FindProjectFile(project)!))
                .Where(s => s.Name.StartsWith(machine + "#", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, states.Count);
            Assert.NotEmpty(states[0].Clips);
            Assert.NotEmpty(states[1].Clips);
            Assert.Empty(states[0].Clips.Intersect(states[1].Clips));
        }
    }

    /// <summary>
    /// The frost atronach's two states are the clearest case of a borrowed set.
    /// </summary>
    /// <remarks>
    /// Its combat and non-combat locomotion are the same eight animations, and its
    /// speed table ships one block where it declares two movement types. That is
    /// the shape a missing block takes when nothing would have been different --
    /// suggestive rather than a rule, since the draugr pair proved no rule can be
    /// exact.
    /// </remarks>
    [CorpusFact]
    public void TheFrostAtronachsTwoStatesShareOneAnimationSet()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile("AtronachFrostProject")!);

        var states = ClipsPerState(walk);
        Assert.Equal(2, states.Count);
        Assert.Equal(8, states[0].Clips.Count);
        Assert.Equal(states[0].Clips, states[1].Clips);

        // two movement types declared, one block shipped
        var constants = StateConstants.Of(walk, BehaviorRoot.Of(cache.FindProjectFile("AtronachFrostProject")!)!.Value);
        Assert.Equal(2, constants.Count);

        var keys = cache.SpeedData!.Block("AtronachFrostProject")!.Entries
            .Where(e => e.Records.Count > 0).Select(e => (int)e.Key).ToList();
        Assert.Equal([0], keys);
    }
}
