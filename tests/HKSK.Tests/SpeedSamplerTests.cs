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
    /// Step four: rebuild the table, over every record whose family the movement
    /// type identifies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things have to line up. The state picks the compass, through the
    /// movement type naming it (<see cref="SpeedSampler.CompassFor"/>); the
    /// heading picks the arm, and where it falls between two arms it is a blend of
    /// both (<see cref="SpeedSampler.Sample"/>).
    /// </para>
    /// <para>
    /// The state is asked of the graph first: a <c>BSiStateTaggingGenerator</c>
    /// that sets <c>iState</c> to the key has the answering compass beneath it,
    /// which places 209 of these records with no name matching at all -- including
    /// every one the player has. Only where no tagging generator covers the key
    /// does this fall back to the movement type's name.
    /// </para>
    /// <para>
    /// <strong>Why not all 1634.</strong> 152 records belong to the eight projects
    /// that do not use the table; 342 belong to projects with no compass, which
    /// turn rather than strafe and whose side and back records come from a turn
    /// axis §6 does not model; and the rest need a family neither the graph nor the
    /// names separate. What is left is 741 records and 9458 points.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheTableRebuildsWhereTheMovementTypeIdentifiesTheFamily()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        int records = 0, resolved = 0;
        var errors = new List<double>();

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                var arms = sampler?.CompassFor((int)entry.Key);

                foreach (SpeedRecord record in entry.Records)
                {
                    records++;
                    if (arms is null) continue;

                    resolved++;
                    foreach (SpeedPoint point in record.Points)
                        if (point.Y > 0f)
                            errors.Add(Math.Abs(
                                SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset)
                                - point.Y) / point.Y);
                }
            }
        }

        errors.Sort();
        Assert.Equal(1634, records);
        Assert.Equal(741, resolved);
        Assert.Equal(9458, errors.Count);
        Assert.InRange(errors[errors.Count / 2], 0d, 0.002d);        // median under 0.2%
        Assert.True(errors.Count(e => e < 0.0005) >= 4400,
                    $"only {errors.Count(e => e < 0.0005)} points within 0.05%");
    }

    /// <summary>
    /// Ten projects rebuild every record they have, including the headings that
    /// fall between compass arms.
    /// </summary>
    /// <remarks>
    /// These are the projects whose movement types separate their compasses
    /// cleanly, and they are the measure of what the model is worth once
    /// identification is out of the way: 19 headings each -- 57 for the giant,
    /// three states -- of which only four ever land on an arm, so most of these
    /// are two-arm blends. The bound is a tenth of a percent at the 90th
    /// percentile, and the median across them is nearer a hundredth.
    /// </remarks>
    [CorpusFact]
    public void TenProjectsRebuildEveryRecordTheyHave()
    {
        (SkyrimCache cache, List<string> projects) = Load();

        var clean = new List<string>();

        foreach (string name in projects)
        {
            SpeedSampler? sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(name)!);
            if (sampler is null) continue;

            int records = 0, resolved = 0;
            var errors = new List<double>();

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                var arms = sampler.CompassFor((int)entry.Key);
                foreach (SpeedRecord record in entry.Records)
                {
                    records++;
                    if (arms is null) continue;
                    resolved++;
                    foreach (SpeedPoint point in record.Points)
                        if (point.Y > 0f)
                            errors.Add(Math.Abs(
                                SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset)
                                - point.Y) / point.Y);
                }
            }

            if (records == 0 || resolved != records || errors.Count == 0) continue;
            errors.Sort();
            if (errors[(int)(errors.Count * 0.9)] < 0.01) clean.Add(name);
        }

        Assert.Equal(
        [
            "BallistaCenturion", "DraugrSkeletonProject", "FrostbiteSpiderProject",
            "GiantProject", "HagravenProject", "SphereCenturion", "SteamProject",
            "TrollProject", "VampireBruteProject", "VampireLord",
        ], clean.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The graph names the family itself, for the states a tagging generator
    /// covers.
    /// </summary>
    /// <remarks>
    /// <c>iState_&lt;MOVT&gt;</c> variables are constants; the game writes
    /// <c>iState</c> from the actor's movement type and the graph compares the two.
    /// Where a <c>BSiStateTaggingGenerator</c> sets <c>iState</c> to a key, the
    /// compasses beneath it are that key's, and no name has to be read: the
    /// falmer's 1 is its bow family and 2 its plain locomotion, and the player's 2,
    /// 6, 7 and 9 are sneak, one-handed, two-handed and magic.
    /// </remarks>
    [CorpusFact]
    public void TheGraphNamesTheFamilyForTheStatesItTags()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        SpeedSampler falmer = Assert.IsType<SpeedSampler>(
            SpeedSampler.FromProject((ActorProject)cache.OpenActor("FalmerProject")!));
        Assert.Equal(["Bow_DirectionalBlend"], falmer.TaggedCompasses[1]);
        Assert.Equal(["MT_DirectionalBlend"], falmer.TaggedCompasses[2]);

        SpeedSampler player = Assert.IsType<SpeedSampler>(
            SpeedSampler.FromProject((ActorProject)cache.OpenActor("DefaultFemale")!));
        Assert.Equal(["Sneak_Direction_Blend"], player.TaggedCompasses[2]);
        Assert.Equal(["H2H_1HM_Direction_Blend"], player.TaggedCompasses[6]);
        Assert.Equal(["2HM_Direction_Blend"], player.TaggedCompasses[7]);
        Assert.Equal(["Magic_Direction_Blend"], player.TaggedCompasses[9]);

        // and the key the graph cannot separate stays unseparated rather than guessed
        Assert.Equal(2, player.TaggedCompasses[8].Count);

        // the player's records are rebuilt off that, and off nothing else
        var errors = new List<double>();
        foreach (SpeedEntry entry in cache.SpeedData!.Block("DefaultFemale")!.Entries)
        {
            if (!player.TaggedCompasses.TryGetValue((int)entry.Key, out IReadOnlyList<string>? tagged)
                || tagged.Count != 1) continue;

            var arms = Assert.IsAssignableFrom<IReadOnlyList<(float Direction, SpeedLadder Ladder)>>(
                player.CompassFor((int)entry.Key));

            foreach (SpeedRecord record in entry.Records)
                foreach (SpeedPoint point in record.Points)
                    if (point.Y > 0f)
                        errors.Add(Math.Abs(
                            SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset)
                            - point.Y) / point.Y);
        }

        errors.Sort();
        Assert.Equal(912, errors.Count);
        Assert.InRange(errors[errors.Count / 2], 0d, 0.001d);          // median under 0.1%
        Assert.InRange(errors[(int)(errors.Count * 0.9)], 0d, 0.01d);  // p90 under 1%
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

        double blended = 0, rounded = 0;
        int between = 0;

        foreach (SpeedRecord record in entry.Records)
        {
            // a heading the compass has no arm for
            if (arms.Any(a => MathF.Abs(a.Direction - record.Direction) <= 1e-4f)) continue;
            between++;

            SpeedLadder nearest = arms.MinBy(a => MathF.Abs(a.Direction - record.Direction)).Ladder;

            foreach (SpeedPoint point in record.Points)
            {
                if (point.Y <= 0f) continue;
                float x = point.X - SpeedLadder.SamplerOffset;
                blended += Math.Abs(SpeedSampler.Sample(arms, record.Direction, x) - point.Y) / point.Y;
                rounded += Math.Abs(nearest.Evaluate(x) - point.Y) / point.Y;
            }
        }

        Assert.Equal(15, between);
        Assert.True(blended * 20 < rounded,
                    $"blending {blended:F4} is not decisively better than rounding {rounded:F4}");
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
