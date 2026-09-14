using System.Numerics;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The locomotion blend response, as <c>docs/speed-data.md</c> §6 models it.
/// </summary>
/// <remarks>
/// The synthetic cases are exact by construction and pin the three properties
/// that were each wrong at some point: travel blends as a vector, duration blends
/// separately, and the curve clamps at both ends of the ladder.
/// </remarks>
public class SpeedLadderTests
{
    private static SpeedRung Rung(float w, float travel, float duration) =>
        new(w, new Vector3(0f, travel, 0f), duration);

    /// <summary>
    /// Equal durations and collinear travel make the response the identity.
    /// </summary>
    /// <remarks>
    /// This is the shape of `SphereCenturion`'s upper segment, and it is the case
    /// that isolates everything else: with the durations equal the duration rule
    /// cannot act, and with the travel collinear the vector blend reduces to a
    /// scalar one.
    /// </remarks>
    [Fact]
    public void EqualDurationsAndCollinearTravelGiveTheIdentity()
    {
        var ladder = new SpeedLadder([Rung(192f, 192f, 1f), Rung(384f, 384f, 1f)]);

        Assert.Equal(192f, ladder.Evaluate(192f), 3);
        Assert.Equal(288f, ladder.Evaluate(288f), 3);
        Assert.Equal(324.5f, ladder.Evaluate(324.5f), 3);
        Assert.Equal(384f, ladder.Evaluate(384f), 3);
    }

    /// <summary>
    /// Travel is a vector: blending two headings shortens the resultant.
    /// </summary>
    /// <remarks>
    /// Two clips of equal speed 45° apart, blended half way, do not travel at
    /// that speed -- they travel at cos(22.5°) of it, 0.9239. Collapsing travel to
    /// a magnitude before interpolating misses this, and it is an 8% error on
    /// every record that mixes headings, which is 15 of every 19.
    /// </remarks>
    [Fact]
    public void TravelBlendsAsAVectorNotAMagnitude()
    {
        float s = MathF.Sqrt(0.5f);
        var ladder = new SpeedLadder(
        [
            new SpeedRung(0f, new Vector3(0f, 100f, 0f), 1f),
            new SpeedRung(100f, new Vector3(100f * s, 100f * s, 0f), 1f),
        ]);

        // Both rungs deliver 100; the midpoint does not.
        Assert.Equal(100f, ladder.Evaluate(0f), 3);
        Assert.Equal(100f, ladder.Evaluate(100f), 3);

        float expected = 100f * MathF.Cos(MathF.PI / 8f);   // cos(22.5 degrees)
        Assert.Equal(expected, ladder.Evaluate(50f), 2);
        Assert.True(ladder.Evaluate(50f) < 100f, "a blend of two headings is slower than either");
    }

    /// <summary>
    /// Unequal durations bend the segment below its chord, never above.
    /// </summary>
    /// <remarks>
    /// The blend returns a duration-weighted average of the two speeds rather
    /// than a plain one, so the deviation is
    /// <c>(s_a - s_b)(D_a - D_b) / (2(D_a + D_b))</c>. A faster rung is a shorter
    /// clip, so those differences always carry opposite signs and the result is
    /// always under the chord. That sign is why every chord-based model tried
    /// against the shipped data came in short.
    /// </remarks>
    [Fact]
    public void UnequalDurationsBendTheSegmentBelowItsChord()
    {
        var ladder = new SpeedLadder([Rung(5f, 100f, 20f), Rung(100f, 100f, 1f)]);

        float lo = ladder.Evaluate(5f), hi = ladder.Evaluate(100f);
        float mid = ladder.Evaluate(52.5f);
        float chord = (lo + hi) / 2f;

        Assert.Equal(5f, lo, 3);
        Assert.Equal(100f, hi, 3);
        Assert.True(mid < chord, $"expected {mid} below the chord {chord}");

        // The same clip at both ends, so only the duration ramps: 100 / lerp(20, 1).
        Assert.Equal(100f / (20f + 0.5f * (1f - 20f)), mid, 3);
    }

    [Fact]
    public void TheCurveClampsAtBothEndsOfTheLadder()
    {
        var ladder = new SpeedLadder([Rung(5f, 50f, 10f), Rung(100f, 100f, 1f)]);

        Assert.Equal(5f, ladder.Evaluate(0f), 3);       // below the bottom rung
        Assert.Equal(5f, ladder.Evaluate(-99f), 3);
        Assert.Equal(100f, ladder.Evaluate(1000f), 3);  // above the top
        Assert.Equal(100f, ladder.Saturation, 3);
    }

    [Fact]
    public void AnEmptyOrSingleRungLadderIsWellDefined()
    {
        Assert.Equal(0f, new SpeedLadder([]).Evaluate(50f));
        Assert.Equal(25f, new SpeedLadder([Rung(10f, 25f, 1f)]).Evaluate(999f), 3);
    }

    /// <summary>
    /// A ladder read out of a real blender reproduces the shipped table.
    /// </summary>
    /// <remarks>
    /// `SphereCenturion` is the clearest case in the game: three rungs, one clip
    /// reused at 0.026 and 1, a second at 1, so the upper segment has equal
    /// durations and collinear travel and the model there is the identity.
    ///
    /// The tolerance is the documented accuracy of §6, not a target: exact where
    /// the durations match, a fraction of a percent where they do not.
    /// </remarks>
    [CorpusFact]
    public void ALadderFromTheGameReproducesItsShippedCurve()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ActorProject actor = Assert.IsType<ActorProject>(cache.OpenActor("SphereCenturion"));

        hkbBlenderGenerator? blender = actor.Behaviors
            .SelectMany(b => b.File.All<hkbBlenderGenerator>())
            .FirstOrDefault(b => b.m_name == "MT_Forward_Blend");

        Assert.NotNull(blender);

        SpeedLadder ladder = SpeedLadder.FromBlender(blender!, actor);
        Assert.Equal(3, ladder.Rungs.Count);
        Assert.Equal([5f, 192f, 384f], ladder.Rungs.Select(r => r.Weight));

        // The rungs deliver what the movement type claims, to the cache's precision.
        Assert.Equal(4.992f, ladder.Rungs[0].Delivered, 2);
        Assert.Equal(192f, ladder.Rungs[1].Delivered, 2);
        Assert.Equal(384f, ladder.Rungs[2].Delivered, 2);

        // The equal-duration segment: the model is the identity and the shipped
        // table agrees to better than a hundredth of a percent.
        float shipped = cache.SpeedData!.Sample("SphereCenturion", 0, 0f, 324.5f);
        Assert.Equal(324.4792f, shipped, 3);
        Assert.InRange(MathF.Abs(ladder.Evaluate(324.5f) - shipped) / shipped, 0f, 0.0002f);
    }

    /// <summary>
    /// The shipped table is the blend law read <see cref="SpeedLadder.SamplerOffset"/>
    /// earlier, and reading it there reproduces the file.
    /// </summary>
    /// <remarks>
    /// These are the points that used to carry the whole of §6's residual: the
    /// steep top of a segment whose durations differ by 38x, where the plain law
    /// runs 0.06% to 0.76% high. The bound here is 0.05%, which is a fortieth of
    /// what it was, and every point clears it.
    /// </remarks>
    [CorpusFact]
    public void TheSamplerOffsetReproducesTheShippedTable()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ActorProject actor = Assert.IsType<ActorProject>(cache.OpenActor("SphereCenturion"));

        hkbBlenderGenerator? blender = actor.Behaviors
            .SelectMany(b => b.File.All<hkbBlenderGenerator>())
            .FirstOrDefault(b => b.m_name == "MT_Forward_Blend");
        Assert.NotNull(blender);

        SpeedLadder ladder = SpeedLadder.FromBlender(blender!, actor);
        SpeedProjectBlock block = Assert.IsType<SpeedProjectBlock>(cache.SpeedData!.Block("SphereCenturion"));
        SpeedRecord record = block.Entries.SelectMany(e => e.Records).First(r => r.Direction == 0f);

        // Only the points the ladder actually spans: outside it the record is
        // answering for a state this blender does not serve (§5.3).
        var spanned = record.Points
            .Where(p => p.X > ladder.Rungs[0].Weight && p.X < ladder.Rungs[^1].Weight)
            .ToList();
        // 9 up the steep segment, then the terminal point, which this record
        // stores twice (§10).
        Assert.Equal(11, spanned.Count);

        foreach (SpeedPoint p in spanned)
        {
            float plain = ladder.Evaluate(p.X);
            float tabulated = ladder.Tabulate(p.X);
            Assert.InRange(MathF.Abs(tabulated - p.Y) / p.Y, 0f, 0.0005f);

            // and it is an improvement everywhere, not a wash that happens to
            // average out
            Assert.True(MathF.Abs(tabulated - p.Y) <= MathF.Abs(plain - p.Y),
                $"x={p.X}: offset made it worse ({tabulated} vs {plain}, shipped {p.Y})");
        }

        // The worst point in the file for the plain law: the top of the segment.
        SpeedPoint top = spanned.Single(p => p.X == 192f);
        Assert.InRange(MathF.Abs(ladder.Evaluate(top.X) - top.Y) / top.Y, 0.007f, 0.008f);
        Assert.InRange(MathF.Abs(ladder.Tabulate(top.X) - top.Y) / top.Y, 0f, 0.0005f);
    }

    /// <summary>
    /// Below its lowest rung a ladder holds the floor child's speed flat, which is
    /// what Havok does -- measured, not assumed.
    /// </summary>
    /// <remarks>
    /// A blender with children at 5 and 100 delivers the floor clip's speed for
    /// every parameter from 0 to 5, with no partial weighting and no ramp from the
    /// origin (docs/speed-data.md §6.1). The engine interpolates from the origin
    /// when it reads a table below its first point (§4.2), but that is the query
    /// side, not the blend.
    /// </remarks>
    [Fact]
    public void BelowTheLowestRungTheFloorHoldsFlat()
    {
        var ladder = new SpeedLadder([
            new SpeedRung(5f, new Vector3(0f, 5f, 0f), 1f),
            new SpeedRung(100f, new Vector3(0f, 100f, 0f), 1f)]);

        foreach (float x in new[] { 0f, 1f, 2.5f, 4.9f, 5f })
            Assert.Equal(5f, ladder.Evaluate(x), 4);

        Assert.Equal(50f, ladder.Evaluate(50f), 4);
    }
}
