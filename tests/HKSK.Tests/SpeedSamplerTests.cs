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

    /// <summary>
    /// Step four: where the tree says which ladder answers a record, the rebuilt
    /// curve is the shipped one.
    /// </summary>
    /// <remarks>
    /// A heading is answered by the compass child sitting on it, and a project
    /// without a compass turns rather than strafes, so only its forward record is
    /// answered by its gait ladder. Where that leaves exactly one candidate there
    /// is no choice to make and no fitting is possible.
    ///
    /// <strong>It leaves 44 records of 1634.</strong> Most projects carry several
    /// families -- walk, run, bow, magic -- and a heading alone does not say which
    /// one a record was sampled for; that join is §9's open problem and this test
    /// does not paper over it. What it does assert is that the arithmetic is right
    /// wherever the identification is not in doubt.
    /// </remarks>
    [CorpusFact]
    public void TheTableRebuildsWhereverTheTreeDeterminesTheLadder()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        int resolved = 0;
        var errors = new List<double>();

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
                foreach (SpeedRecord record in entry.Records)
                {
                    SpeedLadder? ladder = Sole(sampler, record.Direction);
                    if (ladder is null) continue;

                    resolved++;
                    foreach (SpeedPoint point in record.Points)
                        if (point.Y > 0f)
                            errors.Add(Math.Abs(ladder.Tabulate(point.X) - point.Y) / point.Y);
                }
        }

        errors.Sort();
        Assert.Equal(44, resolved);
        Assert.Equal(450, errors.Count);
        Assert.InRange(errors[errors.Count / 2], 0d, 0.0005d);          // median under 0.05%
        Assert.True(errors.Count(e => e < 0.01) >= errors.Count * 0.8,
                    $"only {errors.Count(e => e < 0.01)} of {errors.Count} within 1%");
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

        var errors = record.Points.Where(p => p.Y > 0f)
            .Select(p => (double)Math.Abs(forward.Tabulate(p.X) - p.Y) / p.Y)
            .OrderBy(e => e).ToList();

        Assert.Equal(12, errors.Count);
        Assert.InRange(errors[errors.Count / 2], 0d, 0.0002d);   // median under 0.02%
        Assert.InRange(errors[^1], 0d, 0.001d);                  // worst under 0.1%
    }

    /// <summary>The ladder for a heading, when the tree leaves exactly one.</summary>
    private static SpeedLadder? Sole(SpeedSampler sampler, float direction)
    {
        var candidates = new List<SpeedLadder>();

        if (sampler.Compasses.Count > 0)
        {
            foreach ((string _, var arms) in sampler.Compasses)
                foreach ((float heading, SpeedLadder ladder) in arms)
                    if (MathF.Abs(heading - direction) <= 1e-4f) candidates.Add(ladder);
        }
        else if (direction == 0f)
        {
            candidates.AddRange(sampler.Ladders.Select(l => l.Ladder));
        }

        var distinct = candidates
            .Where(c => c.Rungs.Count > 0)
            .GroupBy(c => string.Join("|", c.Rungs.Select(r => $"{r.Weight}:{r.Animation}")))
            .Select(g => g.First())
            .ToList();

        return distinct.Count == 1 ? distinct[0] : null;
    }
}
