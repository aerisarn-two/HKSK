using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The whole path from a project to its speed table: the states it declares, the
/// node that reads the table, the blends that node drives, and the animations
/// underneath them -- over all 49 projects in the shipped cache.
/// </summary>
/// <remarks>
/// Every number here is measured against the game, and they are asserted exactly
/// rather than as bounds, so a change in how the graph is walked shows up as a
/// failure rather than as a quietly different answer.
/// </remarks>
public sealed class SpeedSamplerTests
{
    /// <summary>The eight projects with no <c>BSSpeedSamplerModifier</c> anywhere.</summary>
    /// <remarks>
    /// Established by reading all 654 behaviour files in the game, not by walking
    /// projects: exactly 42 contain the node, and they serve the other 41 projects.
    /// </remarks>
    private static readonly string[] WithoutSampler =
    [
        "AtronachFlame", "AtronachStormProject", "ChaurusFlyer", "DragonProject",
        "Dragon_Priest", "IceWraithProject", "WispProject", "WitchlightProject",
    ];

    private static (SkyrimCache Cache, List<string> Projects) Load()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        return (cache, [.. cache.SpeedData!.ProjectNames]);
    }

    /// <summary>Step one: every project the table names is a project we can open.</summary>
    [CorpusFact]
    public void EveryProjectInTheTableOpensAsAnActor()
    {
        (SkyrimCache cache, List<string> projects) = Load();
        Assert.Equal(49, projects.Count);

        foreach (string name in projects)
        {
            ActorProject actor = Assert.IsType<ActorProject>(cache.OpenActor(name));
            Assert.NotEmpty(actor.Behaviors);
            Assert.NotNull(cache.SpeedData!.Block(name));
        }
    }

    /// <summary>
    /// Step two: every state a table is keyed by names the movement type that
    /// declares it.
    /// </summary>
    /// <remarks>
    /// The key is an <c>iState</c> value, and a project declares its own in
    /// variables called <c>iState_&lt;MOVT&gt;</c> whose initial value is the key.
    /// That covers every key in the file except two in <c>FalmerProject</c>, which
    /// are the malformed entries of §10 -- 1651406194 and 2147483648 are not state
    /// numbers, they are a corrupted record header read as one.
    /// </remarks>
    [CorpusFact]
    public void EveryStateKeyNamesItsMovementType()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        var gaps = new List<(string Project, uint Key)>();
        int declared = 0;

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;

            declared += sampler.States.Count;
            var known = sampler.States.Select(s => (long)s.Key).ToHashSet();

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
                if (!known.Contains(entry.Key))
                    gaps.Add((name, entry.Key));

            // a state always names at least one movement type, never an empty list
            Assert.All(sampler.States, s => Assert.NotEmpty(s.MovementTypes));
        }

        Assert.Equal([("FalmerProject", 1651406194u), ("FalmerProject", 2147483648u)],
                     gaps.OrderBy(g => g.Key));
        Assert.True(declared > 250, $"only {declared} states declared across the corpus");
    }

    /// <summary>
    /// Step three, first half: the sampler is present exactly where the table is
    /// used, and the eight projects without one do not use it.
    /// </summary>
    /// <remarks>
    /// <c>BSSpeedSamplerModifier</c> is the only reader of a speed table, so a
    /// project without one cannot consult it however its block is filled in. Four
    /// of the eight carry a block that is flat zero; the other four carry a real
    /// curve that nothing reads, their blends being driven straight from
    /// <c>Speed</c>. No project that has a sampler has a flat block.
    /// </remarks>
    [CorpusFact]
    public void TheSamplerIsPresentExactlyWhereTheTableIsUsed()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        var without = new List<string>();
        var flatWithSampler = new List<string>();
        int flatWithout = 0;

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            SpeedProjectBlock block = cache.SpeedData!.Block(name)!;

            bool flat = block.Entries.SelectMany(e => e.Records).All(r =>
                r.Points.Count == 0 || r.Points.Max(p => p.Y) - r.Points.Min(p => p.Y) <= 1e-6f);

            if (sampler is null) { without.Add(name); if (flat) flatWithout++; }
            else if (flat) flatWithSampler.Add(name);
        }

        Assert.Equal(WithoutSampler, without.ToArray());
        Assert.Equal(4, flatWithout);
        Assert.Empty(flatWithSampler);
    }

    /// <summary>
    /// Step three, second half: walking the tree from the sampler reaches the
    /// blended animations, and every one of them has root motion.
    /// </summary>
    /// <remarks>
    /// The sampler names the variable it writes; the blends that read that
    /// variable are the speed ladders; each rung resolves through the animation
    /// cache to a clip and its travel. Nothing here reads a node name.
    /// </remarks>
    [CorpusFact]
    public void EveryRungReachesAnAnimationWithRootMotion()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        int ladders = 0, compasses = 0, arms = 0, rungs = 0, stationary = 0;

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;

            ladders += sampler.Ladders.Count;
            compasses += sampler.Compasses.Count;
            arms += sampler.Compasses.Sum(c => c.Arms.Count);

            foreach ((string _, SpeedLadder ladder) in sampler.Ladders)
                foreach (SpeedRung rung in ladder.Rungs)
                {
                    rungs++;
                    Assert.False(string.IsNullOrEmpty(rung.Animation));
                    Assert.True(rung.Duration > 0f, $"{rung.Animation} has no duration");
                    if (rung.Travel.Length() <= 0f) stationary++;
                }
        }

        Assert.Equal(1038, ladders);
        Assert.Equal(142, compasses);
        Assert.Equal(996, arms);
        Assert.Equal(3416, rungs);

        // A rung is allowed to stand still, and 101 of them do: a first-person
        // body clip does not translate because the camera does, and an in-place
        // attack like H2H_AttackLeft is a rung of the blend it interrupts. They
        // are resolved, not skipped -- a rung with no entry in the cache at all
        // is what SpeedLadder drops, and there are none of those here.
        Assert.Equal(101, stationary);
    }

    /// <summary>The tolerance a rebuilt curve has to hold, at every point.</summary>
    /// <remarks>
    /// A curve fits or it does not. Averaging the error over the points of one
    /// record hides a bad end, and averaging over records hides whole records being
    /// wrong, so neither is done here: a record passes only when every point of it
    /// is inside this, and the counts below are counts of records.
    /// </remarks>
    private const double Tolerance = 0.02;

    /// <summary>
    /// Step four: rebuild the table, curve by curve, and count what holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things have to line up. The state picks the compass -- from the tagging
    /// generator that sets <c>iState</c> to the key where the graph has one, and
    /// from the movement type's name where it does not (<see cref="FamilyGuess"/>);
    /// the heading picks the arm, and where it falls between two arms it is a blend
    /// of both (<see cref="SpeedSampler.Sample"/>).
    /// </para>
    /// <para>
    /// Of the 1482 curves in the 41 projects that read the table, 660 hold at 2%
    /// end to end, 214 are rebuilt and do not hold, and 608 have no compass to
    /// rebuild from -- mostly quadrupeds, which turn rather than strafe, so their
    /// side and back records come from a turn axis §6 does not model.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheTableRebuildsCurveByCurve()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        int curves = 0, pass = 0, fail = 0, unresolved = 0;

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;                 // never reads the table

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                var arms = sampler.CompassFor((int)entry.Key);

                foreach (SpeedRecord record in entry.Records)
                {
                    curves++;
                    if (arms is null) { unresolved++; continue; }
                    if (Holds(arms, record)) pass++; else fail++;
                }
            }
        }

        Assert.Equal(1482, curves);
        Assert.Equal(660, pass);
        Assert.Equal(214, fail);
        Assert.Equal(608, unresolved);
    }

    /// <summary>
    /// Seven projects have every curve they own rebuilt, end to end.
    /// </summary>
    /// <remarks>
    /// These are the ones whose families the graph separates cleanly, and they are
    /// what the model is worth once identification is out of the way: 19 headings
    /// each, of which only four land on a compass arm, so most are two-arm blends.
    /// The worst point of the worst of these curves is half a percent.
    /// </remarks>
    [CorpusFact]
    public void SevenProjectsRebuildEveryCurveTheyHave()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        var whole = new List<string>();
        double worst = 0;

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;

            bool all = true;
            double here = 0;

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                var arms = sampler.CompassFor((int)entry.Key);
                foreach (SpeedRecord record in entry.Records)
                {
                    if (arms is null || !Holds(arms, record)) { all = false; break; }
                    here = Math.Max(here, Worst(arms, record));
                }

                if (!all) break;
            }

            if (all) { whole.Add(name); worst = Math.Max(worst, here); }
        }

        Assert.Equal(
        [
            "BallistaCenturion", "ChaurusProject", "DraugrSkeletonProject",
            "SphereCenturion", "SteamProject", "TrollProject", "VampireLord",
        ], whole.OrderBy(n => n, StringComparer.Ordinal).ToArray());

        Assert.InRange(worst, 0d, 0.006d);
    }

    /// <summary>
    /// The player's own curves, which no route reached before the graph was asked
    /// which family a state belongs to.
    /// </summary>
    /// <remarks>
    /// Six of its fourteen states are tagged in the graph and two more are found by
    /// name, <c>NPCDefault</c> among them -- the plain movement type, which lives in
    /// <c>mt_behavior</c> and is the one the whole hunt was for.
    /// </remarks>
    [CorpusFact]
    public void ThePlayerRebuildsTheCurvesItsStatesIdentify()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedSampler player = Assert.IsType<SpeedSampler>(
            SpeedSampler.FromProject((ActorProject)cache.OpenActor("DefaultFemale")!));

        int pass = 0, fail = 0, unresolved = 0;
        foreach (SpeedEntry entry in cache.SpeedData!.Block("DefaultFemale")!.Entries)
        {
            var arms = player.CompassFor((int)entry.Key);
            foreach (SpeedRecord record in entry.Records)
            {
                if (arms is null) unresolved++;
                else if (Holds(arms, record)) pass++;
                else fail++;
            }
        }

        Assert.Equal(141, pass);
        Assert.Equal(11, fail);
        Assert.Equal(114, unresolved);

        // the movement type itself, and the file it lives in
        SpeedCompass mt = player.Compasses.Single(c => ReferenceEquals(c.Arms, player.CompassFor(0)));
        Assert.Equal("MT_Direction_Blend", mt.Name);
        Assert.Equal("mt_behavior", mt.File);
    }

    /// <summary>Whether every point of a record is inside <see cref="Tolerance"/>.</summary>
    private static bool Holds(IReadOnlyList<(float Direction, SpeedLadder Ladder)> arms, SpeedRecord record) =>
        Worst(arms, record) <= Tolerance;

    /// <summary>The worst point of a record, relative.</summary>
    private static double Worst(IReadOnlyList<(float Direction, SpeedLadder Ladder)> arms, SpeedRecord record)
    {
        double worst = 0;
        foreach (SpeedPoint point in record.Points)
            if (point.Y > 0f)
                worst = Math.Max(worst, Math.Abs(
                    SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset)
                    - point.Y) / point.Y);

        return worst;
    }

    /// <summary>
    /// A heading between two compass arms is a blend of both, not the nearer one.
    /// </summary>
    /// <remarks>
    /// The file samples 19 headings at steps of 0.05 and a compass has arms at
    /// steps of 0.125, so only 0, 0.25, 0.5 and 0.75 ever land on one: 238 of the
    /// 1634 records. Everything else is a two-arm blend, and treating it as the
    /// nearest arm instead is measurably worse -- which is the check here, because
    /// a model that is right for the wrong reason would pass the test above.
    /// </remarks>
    [CorpusFact]
    public void AHeadingBetweenArmsIsBlendedRatherThanRounded()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedSampler sampler = Assert.IsType<SpeedSampler>(
            SpeedSampler.FromProject((ActorProject)cache.OpenActor("SphereCenturion")!));

        SpeedEntry entry = cache.SpeedData!.Block("SphereCenturion")!.Entries[0];
        var arms = Assert.IsAssignableFrom<IReadOnlyList<(float Direction, SpeedLadder Ladder)>>(
            sampler.CompassFor((int)entry.Key));

        int between = 0, blended = 0, rounded = 0;

        foreach (SpeedRecord record in entry.Records)
        {
            // a heading the compass has no arm for
            if (arms.Any(a => MathF.Abs(a.Direction - record.Direction) <= 1e-4f)) continue;
            between++;

            SpeedLadder nearest = arms.MinBy(a => MathF.Abs(a.Direction - record.Direction)).Ladder;

            double worstRounded = 0;
            foreach (SpeedPoint point in record.Points)
                if (point.Y > 0f)
                    worstRounded = Math.Max(worstRounded,
                        Math.Abs(nearest.Evaluate(point.X - SpeedLadder.SamplerOffset) - point.Y) / point.Y);

            if (Holds(arms, record)) blended++;
            if (worstRounded <= Tolerance) rounded++;
        }

        // every one of them holds when blended, and none of them when rounded
        Assert.Equal(15, between);
        Assert.Equal(15, blended);
        Assert.Equal(0, rounded);
    }

    /// <summary>
    /// The same rebuild with the compass named, on the record §6 is verified
    /// against.
    /// </summary>
    /// <remarks>
    /// <c>SphereCenturion</c>'s plain locomotion hangs under
    /// <c>MT_Direction_Blend</c>, and its forward arm is the three-rung ladder §6
    /// works through: one clip at 0.026 playback and again at 1, then a second
    /// clip. Naming the compass removes the family ambiguity and nothing else, so
    /// this is what the rebuild is worth once §9 is closed.
    /// </remarks>
    [CorpusFact]
    public void ANamedCompassRebuildsItsRecordToBetterThanATenthOfAPercent()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedSampler sampler = Assert.IsType<SpeedSampler>(
            SpeedSampler.FromProject((ActorProject)cache.OpenActor("SphereCenturion")!));

        Assert.Equal("SampledSpeed", sampler.SamplerOutput);
        Assert.True(sampler.ReadsSampledSpeed);

        SpeedLadder forward = Assert.IsType<SpeedLadder>(sampler.Cardinal(0f, "MT_Direction_Blend"));
        Assert.Equal([5f, 192f, 384f], forward.Rungs.Select(r => r.Weight));
        Assert.Equal(["MT_Forward", "MT_Forward", "MT_FastForward"], forward.Rungs.Select(r => r.Animation));

        SpeedRecord record = cache.SpeedData!.Block("SphereCenturion")!
            .Entries[0].Records.First(r => r.Direction == 0f);

        double worst = 0;
        int points = 0;
        foreach (SpeedPoint point in record.Points)
        {
            if (point.Y <= 0f) continue;
            points++;
            worst = Math.Max(worst, Math.Abs(forward.Tabulate(point.X) - point.Y) / point.Y);
        }

        Assert.Equal(12, points);
        Assert.InRange(worst, 0d, 0.001d);                       // every point within 0.1%
    }

}
