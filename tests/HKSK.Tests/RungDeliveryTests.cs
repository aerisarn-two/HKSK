using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// A rung's authored weight against what its clip actually delivers.
/// </summary>
public sealed class RungDeliveryTests
{
    /// <summary>
    /// A rung's weight is the speed its clip delivers, for nearly every creature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ladder child's weight is a position on the speed axis and the clip beneath
    /// it travels at some speed of its own, and the two are meant to be the same
    /// number -- <c>docs/speed-data.md</c> §0 -- with the speed table existing to
    /// record where they are not. How near they actually are had not been measured,
    /// and it turns out to be very near: <strong>32 of the 40 projects with a ladder
    /// sit within 1% at the median</strong>, most of them exactly 1.
    /// </para>
    /// <para>
    /// That makes the outliers worth naming, because a creature whose clips do not
    /// deliver their own rung weights is either interesting or broken.
    /// <c>HorseProject</c> is the extreme and it is broken: see
    /// <see cref="TheHorseIsTheOutlierAndItsCacheIsWhy"/>.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void ARungsWeightIsTheSpeedItsClipDelivers()
    {
        var medians = Medians();

        Assert.Equal(40, medians.Count);
        Assert.Equal(32, medians.Count(m => Math.Abs(m.Value - 1) <= 0.01));
        Assert.Equal(33, medians.Count(m => Math.Abs(m.Value - 1) <= 0.05));
    }

    /// <summary>
    /// The horse's clips claim to travel two and a half times their rung weights,
    /// and three separate things say they do not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its forward ladder's rungs are 5, 125.112, 214, 303.906 and 450, and its
    /// movement type asks for a forward walk of <strong>125.11</strong> and a run of
    /// <strong>450</strong> -- the rungs are authored at the game's own numbers. The
    /// shipped speed table then reads very nearly the identity: 121.46 at a goal
    /// speed of 125, 312.57 at 324.5. So the graph and the masters and the table all
    /// agree that the horse travels at about what it is asked for.
    /// </para>
    /// <para>
    /// The animation cache does not. It records <c>WalkForward</c> as 182.344 units
    /// in 0.6 seconds, which is 303.9 -- and every rung playing that animation is out
    /// by the same 2.429, while every rung playing <c>TrotForward</c> is out by the
    /// same 1.5387. A constant per animation, not per rung, so it is the recorded
    /// motion and not the blend.
    /// </para>
    /// <para>
    /// <strong>The horse's cache is independently known to be damaged.</strong>
    /// <c>RunForward</c> is recorded as travelling zero and <c>SprintForward</c>
    /// carries no motion block at all, so two of its four gaits are already missing.
    /// The cache duration agrees with the animation's own for <c>WalkForward</c>, so
    /// it is the displacement that is wrong rather than the span.
    /// </para>
    /// <para>
    /// This is why <c>HorseProject</c> holds 0 of its 289 curve points and why no
    /// correction here would help: nothing in the behaviour, the masters or the
    /// animations disagrees with the shipped table. Only the cache does, and the
    /// cache is the one input that cannot be checked against anything else.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheHorseIsTheOutlierAndItsCacheIsWhy()
    {
        var medians = Medians();

        Assert.Equal(2.4291, medians["HorseProject"], 3);
        Assert.All(medians.Where(m => m.Key != "HorseProject"), m => Assert.True(m.Value < 1.21));

        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var horse = (ActorProject)cache.OpenActor("HorseProject")!;

        Assert.Equal(0f, horse.Animation("RunForward")!.Motion!.Translations[^1].Value.Length());
        Assert.Null(horse.Animation("SprintForward")!.Motion);
    }

    private static Dictionary<string, double> Medians()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Dictionary<string, double> medians = [];

        foreach (CacheProject project in cache.OpenAll())
        {
            if (project is not ActorProject actor) continue;
            if (SpeedSampler.FromProject(actor) is not { } sampler) continue;

            List<double> ratios = [];
            foreach ((string _, SpeedLadder ladder) in sampler.Ladders)
                foreach (SpeedRung rung in ladder.Rungs)
                    if (rung.Weight > 1f && rung.Delivered > 0f)
                        ratios.Add(rung.Delivered / rung.Weight);

            if (ratios.Count == 0) continue;
            ratios.Sort();
            medians[project.Name] = ratios[ratios.Count / 2];
        }

        return medians;
    }
}
