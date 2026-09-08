using HKSK.Cache;
using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The rule the whole library rests on: a clip's cache index is the position of
/// its animation in the character file's animation list.
/// </summary>
/// <remarks>
/// Nothing in either file states this. It was worked out from the shipped data
/// and is checked here against it, because if it is wrong then every edit that
/// touches an index is wrong too.
///
/// The clearest single case is the chaurus, whose character file lists 42
/// animations and whose cache numbers 40 of them: indices 27 and 29 are simply
/// never used, because no clip plays <c>Idle_SleepExit</c> or
/// <c>Idle_SleepStart</c>. The numbering does not close the gaps, which is what
/// makes it positional rather than a dense sequence of its own.
/// </remarks>
public class CacheIndexTests
{
    [CorpusFact]
    public void TheChickensClipsAreNumberedByTheirAnimationsPosition()
    {
        ActorProject chicken = Open("ChickenProject");

        Assert.True(chicken.HasHavok, "the chicken's Havok files should resolve");
        Assert.Equal(20, chicken.Character!.AnimationNames.Count);

        // Spot values, read out of the shipped files by hand.
        AssertClip(chicken, "MainIdle", 12, "Animations\\MT_Idle.HKX");
        AssertClip(chicken, "Idle Fulbody2[mirror]", 4, "Animations\\Idle_Fulbody2.HKX");
        AssertClip(chicken, "IdleHeadTurn1", 6, "Animations\\Idle_Head1.HKX");
        AssertClip(chicken, "TurnRight[mirrored]", 18, "Animations\\TurnLoopingL.HKX");
    }

    /// <summary>
    /// Two clips over one animation share its slot, and therefore its root
    /// motion. The chicken walks and walks slowly off the same file.
    /// </summary>
    [CorpusFact]
    public void ClipsThatShareAnAnimationShareItsSlot()
    {
        ActorProject chicken = Open("ChickenProject");

        Clip walk = chicken.Clip("Forward_Walk")!;
        Clip slow = chicken.Clip("Forward_WalkSlow")!;

        Assert.Equal(walk.CacheIndex, slow.CacheIndex);
        Assert.Same(walk.Slot, slow.Slot);
        Assert.Equal("Animations\\WalkForward.HKX", walk.Slot!.StoredName);
    }

    /// <summary>
    /// The chaurus case: unused positions stay unused rather than being closed up.
    /// </summary>
    [CorpusFact]
    public void UnreferencedAnimationsLeaveGapsInTheNumbering()
    {
        ActorProject chaurus = Open("ChaurusProject");

        Assert.Equal(42, chaurus.Character!.AnimationNames.Count);

        var used = chaurus.Clips.Select(c => c.CacheIndex).ToHashSet();
        Assert.DoesNotContain(27, used);
        Assert.DoesNotContain(29, used);

        // And those two positions are the sleep animations no clip plays.
        Assert.Equal("Animations\\Idle_SleepExit.HKX", chaurus.Animations[27].StoredName);
        Assert.Equal("Animations\\Idle_SleepStart.HKX", chaurus.Animations[29].StoredName);

        // The highest index used is near the end of the list, not near 40 --
        // which it would be if the numbering were dense.
        Assert.Equal(41, used.Max());
    }

    /// <summary>
    /// The rule across every cached project in the game, stated as numbers so a
    /// regression shows up as a change rather than a vague failure.
    /// </summary>
    [CorpusFact]
    public void TheRuleHoldsAcrossTheShippedGame()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int resolved = 0, exact = 0, clips = 0, mismatches = 0;
        var drifted = new List<string>();

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasHavok) continue;
            resolved++;

            int wrong = ConsistencyReport.Check(project).Count(f => f.Kind == "index-mismatch");
            clips += project.Clips.Count(c => c.Generator is not null);
            mismatches += wrong;

            if (wrong == 0) exact++;
            else drifted.Add($"{project.Name} ({wrong})");
        }

        // Every project carrying a cache has its Havok files in the corpus.
        Assert.Equal(49, resolved);

        // 44 agree on every single clip. The five that do not are Bethesda's own
        // drift, not a different rule -- the horse and the werewolf are numbered
        // against animation lists longer than the ones their character files now
        // carry, at a constant offset.
        Assert.Equal(44, exact);
        Assert.Equal(
            new[] { "DefaultFemale (3)", "DefaultMale (3)", "FirstPerson (1)",
                    "HorseProject (31)", "WerewolfBeastProject (200)" },
            drifted.OrderBy(d => d, StringComparer.Ordinal).ToArray());

        Assert.Equal(10556, clips);
        Assert.Equal(238, mismatches);
        Assert.True(mismatches / (double)clips < 0.03,
            $"the rule should hold for well over 97% of clips, saw {clips - mismatches} of {clips}");
    }

    /// <summary>
    /// Root motion is keyed by the same index, so it belongs to the animation.
    /// </summary>
    [CorpusFact]
    public void RootMotionIsKeyedByTheAnimationSlot()
    {
        ActorProject chicken = Open("ChickenProject");

        AnimationSlot walk = chicken.Animation("WalkForward")!;
        Assert.NotNull(walk.Motion);
        Assert.Equal(walk.Index, walk.Motion!.CacheIndex);

        // Every clip playing it reports the same motion.
        foreach (Clip clip in chicken.ClipsOf(walk))
            Assert.Same(walk.Motion, clip.Slot!.Motion);

        Assert.True(walk.Travels, "walking forward should travel");
    }

    private static ActorProject Open(string name) =>
        SkyrimCache.Load(Corpus.Root!).OpenActor(name)
        ?? throw new InvalidOperationException($"no project '{name}' in the corpus");

    private static void AssertClip(ActorProject project, string clip, int index, string animation)
    {
        Clip found = project.Clip(clip) ?? throw new InvalidOperationException($"no clip '{clip}'");

        Assert.Equal(index, found.CacheIndex);
        Assert.Equal(animation, found.Slot!.StoredName);
        Assert.Equal(animation, found.BehaviorAnimation);
    }
}
