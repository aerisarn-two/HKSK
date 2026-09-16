using HKSK.Behavior;
using HKX2;
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

    /// <summary>
    /// A negative playback speed read as a negative contribution makes a record
    /// fall, and only that does -- but it does not make the riekling's numbers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The riekling's eleven falling records are the file's own anomaly, and the
    /// obvious suspect is the thing that makes it unique: its right strafe is its
    /// left one played backwards. If a sampler took that minus sign as a negative
    /// <em>speed</em> rather than as a reversed direction, then a heading between
    /// forward and right would mix a positive contribution with a negative one, and
    /// the answer would drop away as the reversed arm took more of the weight.
    /// </para>
    /// <para>
    /// <strong>It does exactly that, and nothing else tried does.</strong> Blending
    /// the arms as signed speeds instead of as travel vectors produces six falling
    /// records where reading them correctly produces none. That is the only
    /// mechanism found that makes a speed curve fall at all: the correct reading
    /// cannot, because it mixes travel as a vector and takes a length at the end,
    /// and a flipped vector is still a positive speed.
    /// </para>
    /// <para>
    /// <strong>It is still not what Bethesda wrote.</strong> It holds 82 of the
    /// 1037 points against the correct reading's 138, and the six falls are not the
    /// eleven the file has. On the sideways record itself it is further out than
    /// ever -- the file climbs 20.5, 23.2, 25.1, 46.9 over the first four goal
    /// speeds while the signed blend sits at about -3. So the sign explains the
    /// shape and not the numbers, and the block stays recorded rather than modelled.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void ASignedBlendMakesTheFallAndNotTheNumbers()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var actor = (ActorProject)cache.OpenActor("RieklingProject")!;
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile("RieklingProject")!);
        SpeedEntry entry = cache.SpeedData!.Block("RieklingProject")!.Entries.First(e => e.Records.Count > 0);

        LocomotionState state = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
            .First(st => Compass.ArmsOf(walk, st, actor).Count == 4);

        var arms = Compass.ArmsOf(walk, state, actor).OrderBy(a => a.Direction).ToList();
        List<float> sign = [.. arms.Select(a => Reversed(walk, a.Ladder) ? -1f : 1f)];

        // one arm of the four, and it is the right strafe
        Assert.Equal([1f, -1f, 1f, 1f], sign);
        Assert.Equal("Blend_MT_Right", arms[1].Ladder.Name);

        (int plain, int plainFalls) = Measure(arms, sign, entry, signed: false);
        (int signed, int signedFalls) = Measure(arms, sign, entry, signed: true);

        // reading it correctly cannot make a record fall; reading the sign as a
        // speed makes six, and the file has eleven
        Assert.Equal(0, plainFalls);
        Assert.Equal(6, signedFalls);

        // and it is further from the shipped numbers, not nearer
        Assert.Equal(138, plain);
        Assert.Equal(82, signed);
    }

    private static bool Reversed(ProjectWalk walk, SpeedLadder ladder)
    {
        foreach (SpeedConsumer consumer in Locomotion.ConsumersIn(walk.Steps))
        {
            if (consumer.Node is not hkbBlenderGenerator blend || blend.m_name != ladder.Name) continue;
            foreach (hkbBlenderGeneratorChild? child in blend.m_children ?? [])
                if (child?.m_generator is hkbClipGenerator clip && clip.m_playbackSpeed < 0f) return true;
        }

        return false;
    }

    private static (int Held, int Falling) Measure(
        List<(float Direction, SpeedLadder Ladder)> arms, List<float> sign, SpeedEntry entry, bool signed)
    {
        int held = 0, falling = 0;

        foreach (SpeedRecord record in entry.Records)
        {
            double previous = double.NaN;
            bool fell = false;

            foreach (SpeedPoint point in record.Points)
            {
                if (point.Y <= 0f) continue;
                double y = signed
                    ? Scalar(arms, sign, record.Direction, point.X - SpeedLadder.SamplerOffset)
                    : SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset);

                if (Math.Abs(y - point.Y) / point.Y <= 0.02) held++;
                if (!double.IsNaN(previous) && y < previous - 0.01 * Math.Abs(previous)) fell = true;
                previous = y;
            }

            if (fell) falling++;
        }

        return (held, falling);
    }

    /// <summary>The arms blended as signed speeds rather than as travel vectors.</summary>
    private static double Scalar(
        List<(float Direction, SpeedLadder Ladder)> arms, List<float> sign, float direction, float x)
    {
        int upper = arms.FindIndex(a => a.Direction >= direction - 1e-4f);
        if (upper < 0) upper = 0;

        int lower = upper == 0 ? arms.Count - 1 : upper - 1;
        float from = arms[lower].Direction, to = arms[upper].Direction;
        if (to <= from) to += 1f;

        float here = direction < from ? direction + 1f : direction;
        float span = to - from;
        float u = span > 0f ? (here - from) / span : 0f;

        double a = arms[lower].Ladder.Evaluate(x) * sign[lower];
        double b = arms[upper].Ladder.Evaluate(x) * sign[upper];

        return a + u * (b - a);
    }

    /// <summary>
    /// The riekling's values are speeds its own compass can make, at headings that
    /// are not the ones they are filed under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking each shipped point whether <em>any</em> heading of the riekling's
    /// compass produces it, rather than the heading its record names, answers yes
    /// for <strong>813 of its 1037 points</strong> where the record's own heading
    /// answers yes for 138. A creature that turned to face its travel while the
    /// sweep was running would look exactly like that.
    /// </para>
    /// <para>
    /// <strong>The free heading has to be paid for, and it is.</strong> A spare
    /// parameter and a 2% window will fit a lot by chance, so the same question is
    /// asked of four other creatures' shipped values against the riekling's
    /// compass: they match 79 of 250, 70 of 192, 152 of 323 and 28 of 141, which is
    /// 20% to 47%. The riekling's own is 78%, well clear of all of them.
    /// </para>
    /// <para>
    /// <strong>It is still not a model.</strong> No single heading works: answering
    /// every record at the forward arm holds 76 points, and sweeping the answer
    /// heading from 0 to 0.1 peaks at 144 -- against the 138 the correct reading
    /// already gives. So the block is described and not rebuilt, and what is
    /// recorded is that its values belong to its own compass and its headings do
    /// not.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheRieklingsValuesBelongToItsCompassButNotToItsHeadings()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var actor = (ActorProject)cache.OpenActor("RieklingProject")!;
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile("RieklingProject")!);

        LocomotionState state = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
            .First(st => Compass.ArmsOf(walk, st, actor).Count == 4);
        var arms = Compass.ArmsOf(walk, state, actor).OrderBy(a => a.Direction).ToList();

        (int own, int any, int all) = Fit(cache, arms, "RieklingProject", filed: true);

        Assert.Equal(1037, all);
        Assert.Equal(138, own);
        Assert.Equal(813, any);

        // the same question of values that have nothing to do with this creature
        foreach ((string other, int expected, int points) in new[]
        {
            ("BearProject", 79, 250), ("DraugrProject", 70, 192),
            ("WolfProject", 152, 323), ("GiantProject", 28, 141),
        })
        {
            (_, int matched, int n) = Fit(cache, arms, other, filed: false);
            Assert.Equal(points, n);
            Assert.Equal(expected, matched);

            // and every one of them is far below the riekling's own 78%
            Assert.True(matched / (double)n < 0.5, $"{other} fitted {matched} of {n}");
        }
    }

    /// <summary>
    /// How many of a block's points the compass makes at their own heading, and how
    /// many it makes at any heading at all.
    /// </summary>
    private static (int Own, int Any, int All) Fit(
        SkyrimCache cache, List<(float Direction, SpeedLadder Ladder)> arms, string project, bool filed)
    {
        SpeedEntry entry = cache.SpeedData!.Block(project)!.Entries.First(e => e.Records.Count > 0);
        int own = 0, any = 0, all = 0;

        foreach (SpeedRecord record in entry.Records)
            foreach (SpeedPoint point in record.Points)
            {
                if (point.Y <= 0f) continue;
                all++;

                float x = point.X - SpeedLadder.SamplerOffset;
                if (filed && Holds(SpeedSampler.Sample(arms, record.Direction, x), point.Y)) own++;

                for (int i = 0; i <= 200; i++)
                    if (Holds(SpeedSampler.Sample(arms, i / 200f, x), point.Y)) { any++; break; }
            }

        return (own, any, all);
    }

    private static bool Holds(double got, double want) =>
        got > 0 && Math.Abs(got - want) / want <= 0.02;

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
