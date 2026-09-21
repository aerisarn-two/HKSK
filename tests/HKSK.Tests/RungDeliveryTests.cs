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
    /// and it turns out to be very near: <strong>33 of the 40 projects with a ladder
    /// sit within 1% at the median</strong>, most of them exactly 1.
    /// </para>
    /// <para>
    /// That makes the outliers worth naming, because a creature whose clips do not
    /// deliver their own rung weights is either interesting or broken.
    /// <c>HorseProject</c> used to be the extreme, and was only misread: see
    /// <see cref="TheHorsesMotionIsReadAtItsCachesOwnNumbers"/>.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void ARungsWeightIsTheSpeedItsClipDelivers()
    {
        var medians = Medians();

        Assert.Equal(40, medians.Count);
        Assert.Equal(33, medians.Count(m => Math.Abs(m.Value - 1) <= 0.01));
        Assert.Equal(34, medians.Count(m => Math.Abs(m.Value - 1) <= 0.05));
    }

    /// <summary>
    /// The horse delivers its rung weights exactly, once its motion is read at the numbers
    /// its cache gives its clips rather than at its character's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its forward ladder's rungs are 5, 125.112, 214, 303.906 and 450, and its movement
    /// type asks for a forward walk of <strong>125.11</strong> and a run of
    /// <strong>450</strong>. The shipped speed table reads very nearly the identity:
    /// 121.46 at a goal speed of 125, 312.57 at 324.5.
    /// </para>
    /// <para>
    /// This used to be the extreme outlier, at 2.4291, and the horse's cache was taken to
    /// be damaged: <c>WalkForward</c> travelling 182.344 in 0.6 seconds, <c>RunForward</c>
    /// travelling zero, <c>SprintForward</c> with no motion at all. <strong>It is not
    /// damaged; it is numbered against another list.</strong> The cache gives the horse's
    /// clips slots up to 88 and its character lists 51 animations, so reading a clip's
    /// motion at the slot the character lists its animation at reads another animation's:
    /// the 182.344 is <c>TrotForward</c>'s. At the cache's own numbers the walk travels
    /// 137.62, the run 212.32 and the sprint 334.60, the walk's median ratio is 1.000, and
    /// the rebuilt table holds all 289 of the horse's shipped points
    /// (<see cref="ActorProject.MotionOf(HKX2.hkbClipGenerator)"/>).
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheHorsesMotionIsReadAtItsCachesOwnNumbers()
    {
        var medians = Medians();

        Assert.Equal(1.0, medians["HorseProject"], 3);
        Assert.All(medians, m => Assert.True(m.Value < 1.21));

        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var horse = (ActorProject)cache.OpenActor("HorseProject")!;

        Assert.Equal(51, horse.Character!.AnimationNames.Count);
        Assert.Equal(88, horse.Data.Block.Clips.Max(c => c.CacheIndex));

        float Travel(string clip) => horse.MotionOf(horse.Clip(clip)!.Entry)!.Travel;
        Assert.Equal(137.62, Travel("WalkForward"), 2);
        Assert.Equal(212.32, Travel("RunForward"), 2);
        Assert.Equal(334.60, Travel("SprintForward"), 2);

        // what the character's numbering finds instead: the trot's travel, and nothing
        Assert.Equal(Travel("TrotForward"), horse.Animation("WalkForward")!.Motion!.Travel);
        Assert.Equal(0f, horse.Animation("RunForward")!.Motion!.Travel);
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
