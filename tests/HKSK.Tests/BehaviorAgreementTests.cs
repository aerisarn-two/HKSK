using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The cache checked against the behaviour graphs rather than the character
/// file -- a second, independent witness to the same clips.
/// </summary>
/// <remarks>
/// The character file says which animations exist and in what order; the
/// behaviour files say which clips exist and how each one plays. They can be
/// wrong independently, so agreeing with both is a much stronger statement than
/// agreeing with either.
///
/// Two of the three things the cache copies from the behaviour it copies
/// perfectly, across every clip in the game. The third, the event list, is
/// derived rather than copied, and does not reproduce exactly -- see
/// <see cref="TheEventListIsDerivedAndNotPerfectlyReproducible"/>.
/// </remarks>
public class BehaviorAgreementTests
{
    /// <summary>
    /// Every clip generator the behaviours define is in the cache, and every
    /// cached clip's speed and crop times match the generator exactly.
    /// </summary>
    [CorpusFact]
    public void TheCacheCopiesSpeedAndCropFromTheBehaviourExactly()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int withGenerator = 0, speed = 0, crop = 0, notCached = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasHavok || !project.HasCache) continue;

            withGenerator += project.Clips.Count(c => c.Generator is not null);

            foreach (Finding finding in ConsistencyReport.Check(project))
                switch (finding.Kind)
                {
                    case "speed-mismatch": speed++; break;
                    case "crop-mismatch": crop++; break;
                    case "generator-not-cached": notCached++; break;
                }
        }

        Assert.Equal(10556, withGenerator);

        // Not "few" -- none at all, in either direction.
        Assert.Equal(0, speed);
        Assert.Equal(0, crop);
        Assert.Equal(0, notCached);
    }

    /// <summary>
    /// The behaviour never numbers its own animations: every generator in the
    /// game leaves <c>m_animationBindingIndex</c> at -1.
    /// </summary>
    /// <remarks>
    /// Worth pinning down, because a set binding index would be the obvious
    /// place for the cache index to come from, and it would be a much simpler
    /// story than "the position in the character file's list". It is not where
    /// it comes from.
    /// </remarks>
    [CorpusFact]
    public void TheBehaviourNeverCarriesAnAnimationBindingIndex()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int generators = 0, bound = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasHavok) continue;

            foreach (Havok.BehaviorFile behavior in project.Behaviors)
                foreach (HKX2.hkbClipGenerator generator in behavior.Clips)
                {
                    generators++;
                    if (generator.m_animationBindingIndex != -1) bound++;
                }
        }

        Assert.True(generators > 10000, $"expected the game's clip generators, saw {generators}");
        Assert.Equal(0, bound);
    }

    /// <summary>
    /// The cache's clips are a superset of the behaviours' generators.
    /// </summary>
    /// <remarks>
    /// The 41 that have no generator are not corruption: a project lists the
    /// behaviours it owns, and some clips come from graphs shared with other
    /// projects. Nothing goes the other way, which is the direction that would
    /// matter -- a generator missing from the cache is a clip the game cannot
    /// play.
    /// </remarks>
    [CorpusFact]
    public void EveryGeneratorIsCachedThoughNotEveryCachedClipHasAGenerator()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int cached = 0, withGenerator = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasHavok || !project.HasCache) continue;

            cached += project.Clips.Count;
            withGenerator += project.Clips.Count(c => c.Generator is not null);
        }

        Assert.Equal(10597, cached);
        Assert.Equal(10556, withGenerator);
        Assert.Equal(41, cached - withGenerator);
    }

    /// <summary>
    /// The event list is generated from two sources and cannot be recomputed
    /// exactly, which is why the library preserves it rather than rebuilding it.
    /// </summary>
    /// <remarks>
    /// A clip's events are the animation's own annotation track plus the
    /// generator's triggers, merged in time order:
    ///
    /// <list type="bullet">
    /// <item>annotation times are kept as they are, but clamped to the clip's
    /// playing length, <c>(duration - crops) / playbackSpeed</c> -- the bear's
    /// walk has a <c>FootBack</c> annotation at 1.4 in an animation that, at
    /// speed 1.5, finishes at 1.1111, and the cache says 1.1111</item>
    /// <item>a trigger marked relative to the end of the clip lands at that same
    /// playing length plus its local time</item>
    /// <item>an annotation's text is stored as the longest prefix that names a
    /// behaviour event, so the chicken keeps
    /// <c>SoundPlay.NPCChickenScratch</c> in full because that is an event of
    /// its graph, while the atronach's
    /// <c>SoundPlay.NPCAtronachFrostAttack</c> is stored as
    /// <c>SoundPlay</c></item>
    /// </list>
    ///
    /// That reproduces 92% of the game's clips exactly. The rest need finer
    /// rules still, and some of it is drift. This test states the shape of the
    /// agreement rather than pretending to a formula that closes it.
    /// </remarks>
    [CorpusFact]
    public void TheEventListIsDerivedAndNotPerfectlyReproducible()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int clips = 0, withEvents = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasHavok || !project.HasCache) continue;

            foreach (Clip clip in project.Clips)
            {
                if (clip.Generator is null) continue;

                clips++;
                if (clip.Events.Count > 0) withEvents++;

                // Whatever the source, a cached event always carries a name.
                Assert.All(clip.Events, e => Assert.NotEmpty(e.Name));
            }
        }

        Assert.Equal(10556, clips);

        // Most clips announce something; the library keeps those lists as they
        // are rather than regenerating them.
        Assert.True(withEvents > 6000, $"expected most clips to carry events, saw {withEvents}");
    }
}
