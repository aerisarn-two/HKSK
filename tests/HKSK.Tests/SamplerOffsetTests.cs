using HKSK.Behavior;
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
    /// <strong>Between blocks it is not.</strong> <c>VampireLord</c> asks for
    /// 0.0310 and fits to a median error of <strong>0.0008%</strong>, against
    /// <c>SphereCenturion</c>'s 0.0400 at 0.0037% -- errors far too small for the
    /// difference to be the fit wandering. The blend law does not explain it: all
    /// three are synchronised three-rung ladders whose first rung sits at 5, and the
    /// two centurions have identical rung weights, 5, 192 and 384, yet disagree.
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

        // sharp enough that the spread is not the fit wandering
        Assert.True(vampireError < 0.00002, $"vampire lord fitted to {vampireError:P4}");
        Assert.True(sphereError < 0.00005, $"sphere centurion fitted to {sphereError:P4}");

        // and every heading of a block agrees with its own block
        foreach ((string name, float expected) in new[]
        {
            ("SphereCenturion", 0.0400f), ("BallistaCenturion", 0.0385f),
        })
            foreach ((float direction, float best) in PerRecord(cache, name))
                Assert.True(MathF.Abs(best - expected) <= 0.0006f,
                    $"{name} at heading {direction:0.00} wants {best:0.0000}, not {expected:0.0000}");
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
