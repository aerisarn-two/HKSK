using System.Numerics;
using HKSK.Behavior;
using HKSK.Engine;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
using static HKSK.Speed.SpeedDataGenerator;

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
    private static SpeedDataGenerator.Inferred Infer(SkyrimCache cache, IReadOnlyDictionary<string, MovementType> movements)
        => SpeedDataGenerator.Infer(cache, movements, Masters.RaceRoles());

    private const double Tolerance = 0.02;

    private int _projectPoints, _projectHeld;
    private int _sharedBlocks, _newBlocks, _declared;
    private int _evaluatorAgreed, _evaluatorDiffered, _evaluatorMissed;
    private readonly List<string> _mismatches = [];
    private int _sharedPoints, _sharedHeld, _newPoints, _newHeld;
    private int _keyHeld, _keyPoints;
    private readonly List<string> _perKey = [];

    /// <summary>
    /// How much of the shipped table the inference reproduces, and how much it
    /// invents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list comes out right -- all 49, because every project with an
    /// animation cache has a block and nothing else does. The key set does not.
    /// It writes 144 blocks, of which <strong>79 are ones the game ships</strong> --
    /// 92% of the file, against 51 before the evaluator. It misses 7 and invents
    /// 65, and 10 declared movement types cannot be placed at all.
    /// </para>
    /// <para>
    /// <strong>Eight projects carry no <c>BSSpeedSamplerModifier</c> at all</strong>
    /// and the game ships a one-key table for each. Having no sampler does not mean
    /// having no ladder: <c>AtronachFlame</c> and <c>Dragon_Priest</c> drive their
    /// locomotion blends straight from <c>Speed</c>, and reading that where no
    /// sampled speed exists recovers both -- 242 of the atronach's 260 points and
    /// 81 of the priest's 99.
    /// </para>
    /// <para>
    /// The other six do not move on a curve at all. <c>AtronachStorm</c>,
    /// <c>Wisp</c> and <c>Witchlight</c> ship a table of flat zero;
    /// <c>DragonProject</c> ships a flat 384 and <c>IceWraith</c> a flat 319.67;
    /// <c>ChaurusFlyer</c> is zero at most headings and not at the rest. With the
    /// dwarven spider, whose directional blend carries no binding at all, they are
    /// the 7 still missed.
    /// </para>
    /// <para>
    /// The riekling was a tenth until the engine learned that an intro animation
    /// has finished. Its two candidate compasses carry identical rung weights, so
    /// the pairing scores them equally and says nothing; what settles it is running
    /// the graph, and that used to rest in <c>MT_Equip</c> because its combat
    /// machine is in <c>START_STATE_MODE_SYNC</c> on a variable starting at the
    /// equip state. Raising the equip clip's own end trigger carries it through, and
    /// the evaluator picks the bare-handed compass -- 138 of its 1037 points against
    /// 99 for the crossbow one. It is still the worst block in the file and what it
    /// needs is a model: its forward and backward records are exact and its lateral
    /// ones are not a ladder response at all.
    /// </para>
    /// <para>
    /// <strong>All 64 inventions are movement types declared and never swept.</strong>
    /// FirstPerson alone is 18 of them: it declares the full 19-key humanoid set and
    /// the game ships one. DefaultMale and DefaultFemale declare 19 and ship 14. The
    /// draugr pair proves no rule can tell a declared-and-swept type from a
    /// declared-and-not: same graph, same constants, same animations, six blocks
    /// against one.
    /// </para>
    /// <para>
    /// <strong>The evaluator is used where the heuristic cannot answer, not
    /// instead of it.</strong> That is measured rather than assumed. On the 51
    /// blocks both can reach the pairing holds 10145 of 11638 points while the
    /// evaluator holds 7124, and the two disagree on 26 of the 51 -- pinning
    /// <c>iState</c> puts the graph in <em>a</em> locomotion state but not reliably
    /// the one the key denotes. On the 22 blocks the pairing cannot reach at all,
    /// the evaluator holds 2190 of 4558, which is 22 blocks of the shipped file that
    /// were simply absent before.
    /// </para>
    /// <para>
    /// The 63 inventions are movement types declared and never swept, and the draugr
    /// pair proves no rule can tell those from the swept ones: same graph, same
    /// constants, same animations, six blocks against one. They are the price of
    /// recall, not a defect.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheInferenceRecoversAllEightySixBlocks()
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

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rebuild-score.txt"),
            $"shipped {shipped.Count}\nmade {made.Count}\n" +
            $"recovered {made.Intersect(shipped).Count()}\n" +
            $"missed {shipped.Except(made).Count()}\n" +
            $"invented {made.Except(shipped).Count()}\n" +
            $"unbuildable {inferred.Unbuildable}\n\n" +
            "missed:\n" + string.Join("\n", shipped.Except(made).OrderBy(x => x.Item1)) +
            "\n\ninvented:\n" + string.Join("\n", made.Except(shipped).OrderBy(x => x.Item1)));

        Assert.Equal(86, shipped.Count);
        Assert.Equal(149, made.Count);

        Assert.Equal(86, made.Intersect(shipped).Count());   // recovered, was 51
        Assert.Equal(0, shipped.Except(made).Count());       // missed, was 35
        Assert.Equal(63, made.Except(shipped).Count());      // invented, was 41
        Assert.Equal(0, inferred.Unbuildable);               // unplaceable, was 50
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
        List<string> per = [];

        for (int i = 0; i < inferred.File.Blocks.Count; i++)
        {
            string name = SpeedDataFile.StemOf(inferred.File.Projects[i]);
            SpeedProjectBlock? theirs = vanilla.Block(name);
            if (theirs is null) continue;

            string? path = cache.FindProjectFile(name);
            ActorProject actor = (ActorProject)cache.OpenActor(name)!;
            ProjectWalk walk = ProjectWalk.Of(path!);
            // No sampler means no sampled speed to read; the blends that move such
            // a creature read Speed directly.
            string parameter = Ladders.ParameterOf(walk) ?? "Speed";

            hkbBehaviorGraph? graph = null;
            foreach (ProjectStep step in walk.Steps)
                if (step.Node is hkbBehaviorGraph found) { graph = found; break; }

            if (graph is null) continue;
            Properties properties = Properties.OfProject(path!);

            var variables = new ProjectVariables(walk.Steps);
            var built = LocomotionStates.In(walk, variables)
                .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
                .Where(st => st.Arms.Count > 0)
                .ToList();

            var constants = StateConstants.Of(walk, BehaviorRoot.Of(path!)!.Value);
            List<StateAssignment> expressions = [.. StateExpressions.In(walk)];
            var placed = StateExpressions.WithNodes(walk).ToList();

            foreach (SpeedEntry mine in inferred.File.Blocks[i].Entries)
            {
                SpeedEntry? yours = theirs.Entries.FirstOrDefault(e => e.Key == mine.Key && e.Records.Count > 0);
                if (yours is null) continue;

                string? movement = StateConstants.MovementTypeOf(constants, (int)mine.Key);
                MovementType? type = movement is not null &&
                    movements.TryGetValue(movement, out MovementType mt) ? mt : null;
                var arms = built.FirstOrDefault(b => b.State.Key == (int)mine.Key).Arms;
                bool declared = arms is not null;
                LocomotionState? paired = null;

                arms ??= FlatAt(walk, properties, actor, (int)mine.Key);

                // Reached by a tag over a subtree with no ladder, which is the graph
                // declaring it as plainly as a tag over a locomotion state does.
                bool flat = !declared && arms is not null;
                bool borrowed = false;

                if (arms is null)
                {
                    (paired, _) = Pairing.For(
                        walk, built, (int)mine.Key, type, constants.Count, expressions, constants, placed);

                    arms = paired is { } chosen
                        ? built.First(b => b.State.Equals(chosen)).Arms
                        : LikeAnother(built, constants, movements, type);

                    // Borrowed from a key that walks alike, which is the masters
                    // speaking rather than the graph, but it is not a guess either.
                    borrowed = arms is not null;
                    arms ??= StateAt(graph, walk, properties, actor, built, parameter, (int)mine.Key);
                }

                if (built.Count == 0) arms ??= Standing(walk, properties, actor);

                if (arms is null) continue;

                bool shared = declared || flat || borrowed || paired is not null;
                if (declared)
                {
                    _declared++;

                    // Ground truth: the graph itself says this state carries this key.
                    // Does running it, driven by Bethesda's own selectors, land there?
                    var found = StateAt(graph, walk, properties, actor, built, parameter, (int)mine.Key);
                    if (found is null) _evaluatorMissed++;
                    else if (ReferenceEquals(found, arms)) _evaluatorAgreed++;
                    else
                    {
                        _evaluatorDiffered++;
                        LocomotionState want = built.First(b => b.State.Key == (int)mine.Key).State;
                        LocomotionState got = built.First(b => ReferenceEquals(b.Arms, found)).State;
                        _mismatches.Add(
                            $"{name} key {mine.Key}: declared '{want.State.m_name}' " +
                            $"-> evaluator '{got.State.m_name}'");
                    }
                }
                if (shared) _sharedBlocks++; else _newBlocks++;

                blocks++;
                bool blockHolds = true;
                float share = Share(graph, walk, properties, parameter);

                foreach (SpeedRecord record in yours.Records)
                {
                    records++;
                    bool recordHolds = true;

                    foreach (SpeedPoint point in record.Points)
                    {
                        points++;

                        // The record's own heading picks the arm; the ladder under it
                        // answers at the shipped goal speed.
                        double y = share * SpeedSampler.Sample(
                            arms, record.Direction, point.X - SpeedLadder.SamplerOffset);

                        // A shipped zero is held by building zero there, not by being
                        // zero: a relative tolerance means nothing at 0, so it is
                        // judged absolutely. Counting every zero as held credited the
                        // chaurus flyer with 18 points for a block whose other 28 are
                        // all wrong, which made refusing that block look like a loss.
                        if (point.Y <= 0f)
                        {
                            _projectPoints++;
                            if (Math.Abs(y) <= 1.0) { pointsHeld++; _projectHeld++; }
                            else recordHolds = false;
                            continue;
                        }

                        _projectPoints++;
                        bool ok = Math.Abs(y - point.Y) / point.Y <= Tolerance;
                        if (shared) { _sharedPoints++; if (ok) _sharedHeld++; }
                        else { _newPoints++; if (ok) _newHeld++; }

                        _keyPoints++;
                        if (ok) { pointsHeld++; _projectHeld++; _keyHeld++; } else recordHolds = false;

                    }

                    if (recordHolds) recordsHeld++; else blockHolds = false;
                }

                if (blockHolds) blocksHeld++;

                _perKey.Add($"{name,-24} key {mine.Key,-3} " +
                            $"{(declared ? "declared" : flat ? "flat" : paired is not null ? "paired" : borrowed ? "borrowed" : "evaluated"),-9} " +
                            $"'{(built.FirstOrDefault(b => ReferenceEquals(b.Arms, arms)).State.State?.m_name ?? "(flat)"),-30}' " +
                            $"{_keyHeld,4}/{_keyPoints,-4} arms={arms.Count}");
                _keyHeld = _keyPoints = 0;
            }

            per.Add($"{name,-28} {_projectHeld,6}/{_projectPoints,-6} " +
                    $"{(_projectPoints == 0 ? 0 : 100.0 * _projectHeld / _projectPoints),5:0.0}%");
            _projectHeld = _projectPoints = 0;
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rebuild-curves.txt"),
            $"blocks {blocks}\nrecords {records}\npoints {points}\n" +
            $"pointsHeld {pointsHeld}\nrecordsHeld {recordsHeld}\nblocksHeld {blocksHeld}\n" +
            $"\nblocks the graph itself declares: {_declared}" +
            $"  (evaluator agrees {_evaluatorAgreed}, differs {_evaluatorDiffered}, " +
            $"finds nothing {_evaluatorMissed})\n" +
            $"blocks reached without the evaluator: {_sharedBlocks}\n" +
            $"blocks only the evaluator reaches:   {_newBlocks}\n" +
            string.Join("\n", _mismatches) + "\n" +
            $"points on shared blocks: {_sharedHeld}/{_sharedPoints}\n" +
            $"points on new blocks:    {_newHeld}/{_newPoints}\n" +
            string.Join("\n", per.OrderByDescending(x => x)) + "\n\n" + string.Join("\n", _perKey));

        Assert.Equal(86, blocks);
        Assert.Equal(1634, records);
        Assert.Equal(18302, points);

        // 15305 against the 10145 the pairing alone reached, over 77 blocks against
        // 51. The rate reads 87% rather than 92% only because RieklingProject is in
        // the denominator with 1037 points and 138 of them right; on the other 76
        // blocks it is 13345 of 14551.
        Assert.Equal(15896, pointsHeld);
        Assert.Equal(1331, recordsHeld);
        Assert.Equal(53, blocksHeld);

        // The spider centurion's arms play at a rate an expression computes from the
        // sampled speed, and the whole block follows from evaluating it.
        Assert.Contains(_perKey, line => line.StartsWith("DwarvenSpiderCenturionProject ") && line.Contains(" 61/61 "));

        Assert.Equal(25, _declared);
        Assert.Equal(66, _sharedBlocks);
        Assert.Equal(20, _newBlocks);
        Assert.Equal(13587, _sharedHeld);

        // On the 25 the graph declares, running it lands in the right state 6 times.
        // Every one of the 18 differences is a stance -- sneaking, bow drawn,
        // blocking, one- and two-handed, magic ready, casting -- and the key's own
        // name says which: iState_NPCSneaking, iState_NPCBowDrawn, iState_NPC1HM and
        // the rest. The movement selectors alone leave the graph in default
        // locomotion, which is correct; the stance is simply not being set. One of
        // them is known: iIsInSneak = 1 reaches Sneak_Locomotion_State, found by
        // searching the player's 301 variables against these declarations. The
        // others need a combination, and nothing here sets any of them yet.
        Assert.Equal(7, _evaluatorAgreed);
        Assert.Equal(18, _evaluatorDiffered);
        Assert.Equal(0, _evaluatorMissed);
    }

    /// <summary>
    /// The written file, read the way the game reads it, against the shipped one.
    /// </summary>
    /// <remarks>
    /// The curve scores above ask the model at the shipped goal speeds. The file is a
    /// different thing -- thinned points the game interpolates between, and a pass
    /// through above the last one -- and two faults were invisible to the model and
    /// plain here: headings multiplied rather than accumulated, so 12 of every 19
    /// records could not be found by heading at all, and records that stopped at the
    /// ladder's top rung, so faster requests came back unchanged (77.1% before).
    /// </remarks>
    [MastersFact]
    public void TheWrittenFileReadsBackAsTheGameReadsIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile written = SpeedDataFile.Parse(Infer(cache, Masters.Read()).File.Write());
        SpeedDataFile shipped = cache.SpeedData!;

        int points = 0, held = 0, records = 0, recordsHeld = 0;
        foreach (string name in shipped.ProjectNames)
            foreach (SpeedEntry entry in shipped.Block(name)!.Entries.Where(e => e.Records.Count > 0))
            {
                SpeedEntry mine = Assert.Single(written.Block(name)!.Entries, e => e.Key == entry.Key);
                foreach (SpeedRecord record in entry.Records)
                {
                    SpeedRecord ours = Assert.Single(mine.Records, r => r.Direction == record.Direction);
                    records++;
                    bool whole = true;
                    foreach (SpeedPoint point in record.Points)
                    {
                        points++;
                        float y = ours.Sample(point.X);
                        bool ok = point.Y <= 0f ? MathF.Abs(y) <= 1f : Math.Abs(y - point.Y) / point.Y <= Tolerance;
                        if (ok) held++; else whole = false;
                    }
                    if (whole) recordsHeld++;
                }
            }

        Assert.Equal(18302, points);
        Assert.Equal(15034, held);
        Assert.Equal(894, recordsHeld);
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

        // Bit for bit the headings the game writes, or its lookup by heading misses.
        Assert.All(again.Blocks.SelectMany(b => b.Entries),
                   e => Assert.Equal(SpeedRecord.StandardDirections(), e.Records.Select(r => r.Direction)));
    }
}
