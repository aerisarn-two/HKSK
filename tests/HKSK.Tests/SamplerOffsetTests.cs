using HKSK.Behavior;
using HKX2;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The shift between the goal speed a shipped record stores and the response it
/// recorded there.
/// </summary>
public sealed class SamplerOffsetTests
{
    /// <summary>
    /// The offset is one number within a block and a different number between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SpeedLadder.SamplerOffset"/> is the rebuild's only fitted
    /// constant, obtained by minimising the error against the shipped file, and it
    /// has been described as a property of Bethesda's sampler -- one global number,
    /// the same for every creature. Fitting it per block instead says the first half
    /// of that and not the second.
    /// </para>
    /// <para>
    /// <strong>Within a block it is one number.</strong> Fitting each of a block's
    /// nineteen records separately gives the same answer for all of them:
    /// <c>SphereCenturion</c> asks for 0.0400 at every heading and
    /// <c>BallistaCenturion</c> for 0.0385, each to within the 0.0005 the sweep can
    /// resolve.
    /// </para>
    /// <para>
    /// <strong>Between blocks it is not, and the basins prove it.</strong> Comparing
    /// each block's error at its own best against the error elsewhere -- the span
    /// over which it stays within twice its minimum -- <c>SphereCenturion</c> holds
    /// 0.0385 to 0.0415 and <c>VampireLord</c> holds 0.0295 to 0.0325. Equally tight
    /// at 0.0030 wide, and <strong>disjoint</strong>: no offset satisfies both. It
    /// is the widths that matter and not the depths, since the two sit at very
    /// different error levels -- the vampire lord's worst is still better than the
    /// centurion's best -- and a shallow minimum would be no evidence at all.
    /// </para>
    /// <para>
    /// The blend law does not explain it: both are synchronised three-rung ladders
    /// whose first rung sits at 5, and the two centurions have identical rung
    /// weights, 5, 192 and 384, yet disagree.
    /// </para>
    /// <para>
    /// So the global 0.0404 is a compromise and not a law. It is kept anyway,
    /// because fitting one per creature would be fitting to the answer, and it costs
    /// little: on these blocks it loses 0.002% to 0.004% of accuracy against each
    /// block's own best.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheSamplerOffsetIsPerBlockAndNotGlobal()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        (float sphere, double sphereError) = Fit(cache, "SphereCenturion");
        (float ballista, double ballistaError) = Fit(cache, "BallistaCenturion");
        (float vampire, double vampireError) = Fit(cache, "VampireLord");

        Assert.Equal(0.0400f, sphere, 4);
        Assert.Equal(0.0385f, ballista, 4);
        Assert.Equal(0.0315f, vampire, 4);

        // each block's basin: where its error stays within twice its own minimum
        (float sphereLow, float sphereHigh) = Basin(cache, "SphereCenturion", sphereError);
        (float vampireLow, float vampireHigh) = Basin(cache, "VampireLord", vampireError);

        Assert.Equal(0.0385f, sphereLow, 4);
        Assert.Equal(0.0415f, sphereHigh, 4);
        Assert.Equal(0.0295f, vampireLow, 4);
        Assert.Equal(0.0325f, vampireHigh, 4);

        // equally tight, and no offset satisfies both
        Assert.Equal(sphereHigh - sphereLow, vampireHigh - vampireLow, 4);
        Assert.True(vampireHigh < sphereLow, "the basins overlap after all");

        // and every heading of a block agrees with its own block
        foreach ((string name, float expected) in new[]
        {
            ("SphereCenturion", 0.0400f), ("BallistaCenturion", 0.0385f),
        })
            foreach ((float direction, float best) in PerRecord(cache, name))
                Assert.True(MathF.Abs(best - expected) <= 0.0006f,
                    $"{name} at heading {direction:0.00} wants {best:0.0000}, not {expected:0.0000}");
    }

    /// <summary>
    /// Four things the offset is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is not the <strong>grid</strong>. Every goal speed in the shipped file is
    /// an exact multiple of 0.5, so there is no fractional step for an offset of
    /// 0.03 to hide in -- and a block's records do not even share one grid, while
    /// the offset fitted to each of them is the same number.
    /// </para>
    /// <para>
    /// It is not <strong>stored</strong>. <c>BSSpeedSamplerModifier</c> carries four
    /// members, <c>state</c>, <c>direction</c>, <c>goalSpeed</c> and
    /// <c>speedOut</c>, and none of them is a shift.
    /// </para>
    /// <para>
    /// It is not <strong>damping</strong>, which was the best guess: a speed
    /// approaching its goal at a constant rate would be left short by a constant,
    /// which is exactly the pure offset with no scale that the corpus fit found.
    /// But <c>SphereCenturion</c>, <c>BallistaCenturion</c> and <c>Spriggan</c>
    /// carry no <c>hkbDampingModifier</c> anywhere, and they have three different
    /// offsets between them. The player's damping is a PID with integral action,
    /// <c>kI = 0.015</c>, which drives a steady-state error to zero rather than
    /// leaving one.
    /// </para>
    /// <para>
    /// And it is not the <strong>blend law</strong>: the two centurions are both
    /// synchronised three-rung ladders with the same rung weights, 5, 192 and 384,
    /// and ask for 0.0400 and 0.0385.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheOffsetIsNotTheGridNorTheSamplerNorDamping()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        // every goal speed in the file sits on a half unit
        int points = 0;
        foreach (string name in cache.SpeedData!.ProjectNames)
            foreach (SpeedEntry entry in cache.SpeedData.Block(name)!.Entries)
                foreach (SpeedRecord record in entry.Records)
                    foreach (SpeedPoint point in record.Points)
                    {
                        points++;
                        float twice = point.X * 2f;
                        Assert.True(MathF.Abs(twice - MathF.Round(twice)) < 1e-3f,
                            $"{name} carries a goal speed of {point.X}");
                    }

        Assert.Equal(18302, points);

        // and the creatures that pin the offset most sharply damp nothing at all
        foreach (string name in new[] { "SphereCenturion", "BallistaCenturion", "Spriggan" })
        {
            ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile(name)!);
            Assert.DoesNotContain(walk.Steps, step => step.Node is hkbDampingModifier);
        }
    }

    /// <summary>Where a block's error stays within twice its own best.</summary>
    private static (float Low, float High) Basin(SkyrimCache cache, string project, double best)
    {
        var (arms, entry) = Load(cache, project);
        float low = 1f, high = 0f;

        for (int step = 0; step <= 400; step++)
        {
            float offset = step * 0.0005f;
            List<double> errors = [];

            foreach (SpeedRecord record in entry.Records)
                foreach (SpeedPoint point in record.Points)
                {
                    if (point.Y <= 0f) continue;
                    double y = SpeedSampler.Sample(arms, record.Direction, point.X - offset);
                    if (y > 0) errors.Add(Math.Abs(y - point.Y) / point.Y);
                }

            errors.Sort();
            if (errors[errors.Count / 2] > 2 * best) continue;

            low = MathF.Min(low, offset);
            high = MathF.Max(high, offset);
        }

        return (low, high);
    }

    private static (float Offset, double Error) Fit(SkyrimCache cache, string project)
    {
        var (arms, entry) = Load(cache, project);
        double best = double.MaxValue;
        float at = 0f;

        for (int step = 0; step <= 400; step++)
        {
            float offset = step * 0.0005f;
            List<double> errors = [];

            foreach (SpeedRecord record in entry.Records)
                foreach (SpeedPoint point in record.Points)
                {
                    if (point.Y <= 0f) continue;
                    double y = SpeedSampler.Sample(arms, record.Direction, point.X - offset);
                    if (y > 0) errors.Add(Math.Abs(y - point.Y) / point.Y);
                }

            errors.Sort();
            double median = errors[errors.Count / 2];
            if (median < best) { best = median; at = offset; }
        }

        return (at, best);
    }

    private static IEnumerable<(float Direction, float Offset)> PerRecord(SkyrimCache cache, string project)
    {
        var (arms, entry) = Load(cache, project);

        foreach (SpeedRecord record in entry.Records)
        {
            double best = double.MaxValue;
            float at = 0f;

            for (int step = 0; step <= 400; step++)
            {
                float offset = step * 0.0005f;
                List<double> errors = [];

                foreach (SpeedPoint point in record.Points)
                {
                    if (point.Y <= 0f) continue;
                    double y = SpeedSampler.Sample(arms, record.Direction, point.X - offset);
                    if (y > 0) errors.Add(Math.Abs(y - point.Y) / point.Y);
                }

                if (errors.Count == 0) break;
                errors.Sort();
                if (errors[errors.Count / 2] < best) { best = errors[errors.Count / 2]; at = offset; }
            }

            yield return (record.Direction, at);
        }
    }

    private static (List<(float Direction, SpeedLadder Ladder)> Arms, SpeedEntry Entry) Load(
        SkyrimCache cache, string project)
    {
        var actor = (ActorProject)cache.OpenActor(project)!;
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile(project)!);
        SpeedEntry entry = cache.SpeedData!.Block(project)!.Entries.First(e => e.Records.Count > 0);

        var arms = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
            .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
            .Where(st => st.Arms.Count > 0)
            .First(st => st.State.Key == (int)entry.Key).Arms;

        return (arms, entry);
    }
}
