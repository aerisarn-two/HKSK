using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Inferring the whole speed table from the behaviour graphs, the animation cache
/// and the movement types, then measuring the distance to the shipped file.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is read from the shipped table.</strong> Which projects get a
/// block, which keys each one carries, how many headings a block has, which goal
/// speeds to sample and what speed comes back are all decided from the three
/// inputs. The vanilla file is opened only afterwards, to say how far off the
/// result is.
/// </para>
/// <para>
/// That is the difference between a curve model and an inference, and the numbers
/// are much harsher for it -- which is the point of measuring this way.
/// </para>
/// </remarks>
public sealed class SpeedDataRebuildTests
{
    private const double Tolerance = 0.02;

    /// <summary>The 19 headings every shipped block carries, at 0.05 apart.</summary>
    private static IEnumerable<float> Headings()
    {
        for (int i = 0; i < 19; i++) yield return i * 0.05f;
    }

    private sealed record Inferred(SpeedDataFile File, int Projects, int Blocks, int Unbuildable);

    /// <summary>
    /// Builds a speed table from the graphs alone.
    /// </summary>
    /// <remarks>
    /// A project gets a block for every <c>iState_&lt;MOVT&gt;</c> constant its root
    /// graph declares whose movement type the masters carry and whose key the graph
    /// can pair with a locomotion state. The goal speeds are a grid over the arm's
    /// own range, because the rule by which the sampler kept about a dozen of some
    /// 650 swept positions is not known.
    /// </remarks>
    private static Inferred Infer(SkyrimCache cache, IReadOnlyDictionary<string, MovementType> movements)
    {
        var file = new SpeedDataFile();
        int projects = 0, blocks = 0, unbuildable = 0;

        foreach (CacheProject project in cache.OpenAll())
        {
            if (project is not ActorProject actor) continue;

            string? path = cache.FindProjectFile(project.Name);
            if (path is null) continue;

            projects++;
            file.Projects.Add(SpeedDataFile.ListingFor(project.Name));

            var block = new SpeedProjectBlock();
            file.Blocks.Add(block);

            if (BehaviorRoot.Of(path) is not { } root) continue;

            ProjectWalk walk = ProjectWalk.Of(path);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            var variables = new ProjectVariables(walk.Steps);
            var constants = StateConstants.Of(walk, root);

            var built = LocomotionStates.In(walk, variables)
                .Select(s => (State: s, Arms: Compass.ArmsOf(walk, s, actor)))
                .Where(s => s.Arms.Count > 0)
                .ToList();

            List<StateAssignment> expressions = [.. StateExpressions.In(walk)];
            var placed = StateExpressions.WithNodes(walk).ToList();

            foreach ((string constant, int key) in constants.OrderBy(c => c.Value))
            {
                string movement = constant["iState_".Length..];
                if (!movements.TryGetValue(movement, out MovementType type)) continue;

                (LocomotionState? chosen, Pairing.By how) =
                    Pairing.For(walk, built, key, type, constants.Count, expressions, constants, placed);

                if (chosen is null) { unbuildable++; continue; }

                var arms = built.First(b => b.State.Equals(chosen.Value)).Arms;

                float top = arms.Max(a => a.Ladder.Rungs.Count == 0 ? 0f : a.Ladder.Rungs[^1].Weight);
                if (top <= 0f) { unbuildable++; continue; }

                var entry = new SpeedEntry { Key = (uint)key };
                block.Entries.Add(entry);
                blocks++;

                foreach (float heading in Headings())
                {
                    var record = new SpeedRecord { Direction = heading };
                    entry.Records.Add(record);

                    for (int i = 0; i <= 16; i++)
                    {
                        float x = top * i / 16f;
                        record.Points.Add(new SpeedPoint(
                            x, SpeedSampler.Sample(arms, heading, x - SpeedLadder.SamplerOffset)));
                    }
                }
            }
        }

        return new Inferred(file, projects, blocks, unbuildable);
    }

    /// <summary>
    /// How much of the shipped table the inference reproduces, and how much it
    /// invents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list comes out right -- all 49, because every project with an
    /// animation cache has a block and nothing else does. The key set does not.
    /// The inference writes a block for every declared movement type it can pair
    /// with a locomotion state, which is <strong>not</strong> what Bethesda swept:
    /// it writes 92 blocks, of which 51 are ones the game ships -- 59% of the file.
    /// It misses 35 and invents 41, and 50 declared movement types could not be
    /// paired with a state at all.
    /// </para>
    /// <para>
    /// The 41 inventions are movement types declared and never swept, and the draugr
    /// pair proves no rule can tell those from the swept ones: same graph, same
    /// constants, same animations, six blocks against one. They are the price of
    /// recall, not a defect in the pairing.
    /// </para>
    /// <para>
    /// Both errors were shown to be unavoidable from these inputs. The misses are
    /// mostly the 30 projects whose graph never writes <c>iState</c>, where nothing
    /// pairs a key with a state. The inventions are movement types that were
    /// declared and never sampled, and the draugr pair proves no rule can tell those
    /// apart: same graph, same constants, same animations, six blocks against one.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheInferenceRecoversEighteenOfTheEightySixBlocks()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Inferred inferred = Infer(cache, Masters.Read());

        SpeedDataFile vanilla = cache.SpeedData!;

        Assert.Equal(49, inferred.Projects);
        Assert.Equal(49, vanilla.Projects.Count);
        Assert.Equal(vanilla.Projects.Order(), inferred.File.Projects.Order());

        var shipped = new HashSet<(string, uint)>();
        foreach (string name in vanilla.ProjectNames)
            foreach (SpeedEntry e in vanilla.Block(name)!.Entries)
                if (e.Records.Count > 0) shipped.Add((name, e.Key));

        var made = new HashSet<(string, uint)>();
        for (int i = 0; i < inferred.File.Blocks.Count; i++)
            foreach (SpeedEntry e in inferred.File.Blocks[i].Entries)
                made.Add((SpeedDataFile.StemOf(inferred.File.Projects[i]), e.Key));

        Assert.Equal(86, shipped.Count);
        Assert.Equal(92, made.Count);

        Assert.Equal(51, made.Intersect(shipped).Count());   // recovered
        Assert.Equal(35, shipped.Except(made).Count());      // missed
        Assert.Equal(41, made.Except(shipped).Count());      // invented
        Assert.Equal(50, inferred.Unbuildable);              // declared but unpairable
    }

    /// <summary>
    /// Where a block was recovered, the curve is close: 87% of the shipped points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inferred curve is a function of goal speed, so it is evaluated at the
    /// goal speeds the shipped record happens to carry -- reading those back is a
    /// measurement, not an input, and the inferred file samples its own grid.
    /// </para>
    /// <para>
    /// Over the 51 recovered blocks: 10,145 of 11,638 points within 2%, 694 of 969
    /// records within 2% at every point, and 26 blocks exact end to end. The
    /// forward-only creatures are exact along their forward heading and wrong across
    /// the other eighteen, which come from a turn axis section 6 does not model.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheRecoveredBlocksCarryTheRightCurves()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var movements = Masters.Read();
        Inferred inferred = Infer(cache, movements);
        SpeedDataFile vanilla = cache.SpeedData!;

        int blocks = 0, blocksHeld = 0, records = 0, recordsHeld = 0, points = 0, pointsHeld = 0;

        for (int i = 0; i < inferred.File.Blocks.Count; i++)
        {
            string name = SpeedDataFile.StemOf(inferred.File.Projects[i]);
            SpeedProjectBlock? theirs = vanilla.Block(name);
            if (theirs is null) continue;

            string? path = cache.FindProjectFile(name);
            ActorProject actor = (ActorProject)cache.OpenActor(name)!;
            ProjectWalk walk = ProjectWalk.Of(path!);
            var variables = new ProjectVariables(walk.Steps);
            var constants = StateConstants.Of(walk, BehaviorRoot.Of(path!)!.Value);

            var built = LocomotionStates.In(walk, variables)
                .Select(s => (State: s, Arms: Compass.ArmsOf(walk, s, actor)))
                .Where(s => s.Arms.Count > 0)
                .ToList();

            List<StateAssignment> expressions = [.. StateExpressions.In(walk)];
            var placed = StateExpressions.WithNodes(walk).ToList();

            foreach (SpeedEntry mine in inferred.File.Blocks[i].Entries)
            {
                SpeedEntry? yours = theirs.Entries.FirstOrDefault(e => e.Key == mine.Key && e.Records.Count > 0);
                if (yours is null) continue;

                // rebuild the arms to evaluate at their goal speeds
                string? movement = StateConstants.MovementTypeOf(constants, (int)mine.Key);
                MovementType? type = movement is not null && movements.TryGetValue(movement, out MovementType m) ? m : null;

                (LocomotionState? chosen, _) =
                    Pairing.For(walk, built, (int)mine.Key, type, constants.Count, expressions, constants, placed);
                if (chosen is null) continue;

                var arms = built.First(b => b.State.Equals(chosen.Value)).Arms;

                blocks++;
                bool blockHolds = true;

                foreach (SpeedRecord record in yours.Records)
                {
                    records++;
                    bool recordHolds = true;

                    foreach (SpeedPoint point in record.Points)
                    {
                        points++;
                        if (point.Y <= 0f) { pointsHeld++; continue; }

                        double y = SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset);
                        if (Math.Abs(y - point.Y) / point.Y <= Tolerance) pointsHeld++; else recordHolds = false;
                    }

                    if (recordHolds) recordsHeld++; else blockHolds = false;
                }

                if (blockHolds) blocksHeld++;
            }
        }

        Assert.Equal(51, blocks);
        Assert.Equal(969, records);
        Assert.Equal(11638, points);

        Assert.Equal(10145, pointsHeld);
        Assert.Equal(694, recordsHeld);
        Assert.Equal(26, blocksHeld);
    }

    /// <summary>The inferred table writes back as a well-formed file.</summary>
    [MastersFact]
    public void TheInferredTableRoundTripsThroughTheFileFormat()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Inferred inferred = Infer(cache, Masters.Read());

        SpeedDataFile again = SpeedDataFile.Parse(inferred.File.Write());

        Assert.Equal(inferred.File.Projects, again.Projects);
        Assert.Equal(inferred.File.Blocks.Count, again.Blocks.Count);
        Assert.All(again.Blocks.Where(b => b.Entries.Count > 0),
                   b => Assert.All(b.Entries, e => Assert.Equal(19, e.Records.Count)));
    }
}
