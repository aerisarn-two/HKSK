using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What the shipped file looks like as data, before anything is rebuilt from it.
/// </summary>
public sealed class ShippedShapeTests
{
    /// <summary>
    /// A record rises with the goal speed, and eighteen of 1634 do not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record answers "how fast does it travel when asked for x", so it should
    /// never fall as x rises: a ladder clamps at its ends and interpolates between
    /// them, and both are monotone. Over the whole shipped file that holds for 1616
    /// of 1634 records.
    /// </para>
    /// <para>
    /// <strong>Eleven of the eighteen are the riekling's</strong>, and the other
    /// seven are small -- the lurker's two are the gait step of §6.2b, the daedra's
    /// and the draugr's are a couple of units.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void ShippedRecordsRiseWithTheGoalSpeed()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int records = 0, falling = 0;
        Dictionary<string, int> byProject = [];

        foreach (string name in cache.SpeedData!.ProjectNames)
            foreach (SpeedEntry entry in cache.SpeedData.Block(name)!.Entries)
                foreach (SpeedRecord record in entry.Records)
                {
                    if (record.Points.Count < 2) continue;
                    records++;
                    if (!Falls(record)) continue;

                    falling++;
                    byProject[name] = byProject.GetValueOrDefault(name) + 1;
                }

        Assert.Equal(1634, records);
        Assert.Equal(18, falling);
        Assert.Equal(11, byProject["RieklingProject"]);
    }

    /// <summary>
    /// The riekling's block is the shipped file's own anomaly, not a gap in the model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It holds 138 of its 1037 points and is the largest block of error left, but
    /// the error is not spread over it. <strong>Its two cardinal records are exact:
    /// forward and backward carry twelve points each and every one of them holds</strong>,
    /// reproducing the shipped numbers to five figures through the ordinary ladder.
    /// </para>
    /// <para>
    /// The other seventeen records are a different kind of data. They carry up to
    /// 121 points where twelve did for forward, eleven of them fall somewhere
    /// instead of rising, and the values wander -- the sideways record reads 381.13
    /// at a goal speed of 257 and 312.57 at 324.5. No ladder produces that, and
    /// none of the three compasses the riekling owns comes close: 138 points for
    /// its bare-handed one, 99 each for the spear and the crossbow.
    /// </para>
    /// <para>
    /// <strong>And the damage has a side.</strong> The riekling is the only project
    /// whose locomotion rungs are clips played backwards, and every one of them is
    /// in <c>Blend_MT_Right</c> -- its right strafe is its left one reversed. The
    /// nine records between forward and backward the short way, which are the ones
    /// that mix that arm, hold 17 of their 546 points and seven of them fall, with
    /// the drop growing steadily across them: 2.7, 15.1, 68.6, 105.9, 120.3, 132.2,
    /// 181.5. The seven going round the other way hold 97 of 467 and four fall, by
    /// 20 at worst. The two cardinals, which use no reversed clip at all, are exact.
    /// </para>
    /// <para>
    /// That the broken half is the reversed half is suggestive, and it is as far as
    /// the evidence goes. Reading the reversed arm wrongly on purpose does not
    /// reproduce the shipped numbers -- travel left un-negated, the speed negated,
    /// the duration taken as <c>D/ps</c> and so signed, and the arm zeroed outright
    /// were each measured, and none of them beats reading it correctly (138 points,
    /// and 17 on the affected headings). So the riekling's 899 missing points are
    /// recorded rather than chased.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheRieklingsCardinalRecordsAreExactAndTheRestAreNot()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var actor = (ActorProject)cache.OpenActor("RieklingProject")!;
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile("RieklingProject")!);
        SpeedEntry entry = cache.SpeedData!.Block("RieklingProject")!.Entries.First(e => e.Records.Count > 0);

        var arms = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
            .Select(st => Compass.ArmsOf(walk, st, actor))
            .First(a => a.Count == 4);

        int cardinalHeld = 0, cardinalAll = 0, widest = 0;
        int rightHeld = 0, rightAll = 0, rightFalling = 0;
        int leftHeld = 0, leftAll = 0, leftFalling = 0;

        foreach (SpeedRecord record in entry.Records)
        {
            bool cardinal = record.Direction < 1e-3f || MathF.Abs(record.Direction - 0.5f) < 1e-3f;
            bool right = !cardinal && record.Direction < 0.5f;   // the half holding the reversed arm

            foreach (SpeedPoint point in record.Points)
            {
                if (point.Y <= 0f) continue;
                double y = SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset);
                bool ok = y > 0 && Math.Abs(y - point.Y) / point.Y <= 0.02;

                if (cardinal) { cardinalAll++; if (ok) cardinalHeld++; }
                else if (right) { rightAll++; if (ok) rightHeld++; }
                else { leftAll++; if (ok) leftHeld++; }
            }

            if (cardinal) continue;
            widest = Math.Max(widest, record.Points.Count);
            if (!Falls(record)) continue;
            if (right) rightFalling++; else leftFalling++;
        }

        // forward and backward use no reversed clip, and every point is right
        Assert.Equal(24, cardinalAll);
        Assert.Equal(24, cardinalHeld);

        // the half that mixes Blend_MT_Right, where every reversed clip lives
        Assert.Equal(546, rightAll);
        Assert.Equal(17, rightHeld);
        Assert.Equal(7, rightFalling);

        // and the half that does not
        Assert.Equal(467, leftAll);
        Assert.Equal(97, leftHeld);
        Assert.Equal(4, leftFalling);

        Assert.Equal(121, widest);
    }

    private static bool Falls(SpeedRecord record)
    {
        float worst = 0f;
        for (int i = 1; i < record.Points.Count; i++)
        {
            if (record.Points[i].X <= record.Points[i - 1].X) continue;
            worst = MathF.Max(worst, record.Points[i - 1].Y - record.Points[i].Y);
        }

        return worst > 0.01f * MathF.Max(record.Points[^1].Y, 1f);
    }
}
