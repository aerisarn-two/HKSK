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
        => SpeedDataGenerator.Infer(cache, movements);

    private const double Tolerance = 0.02;

    private int _projectPoints, _projectHeld;
    private int _sharedBlocks, _newBlocks, _declared;
    private int _evaluatorAgreed, _evaluatorDiffered, _evaluatorMissed;
    private readonly List<string> _mismatches = [];
    private int _sharedPoints, _sharedHeld, _newPoints, _newHeld;
    private int _keyHeld, _keyPoints;
    private readonly List<string> _perKey = [];

    /// <summary>
    /// The table holds a block for every key a graph can put <c>iState</c> at, in
    /// every project that reads the table, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine reads <c>iState</c> back from the graph and applies the movement
    /// type its <c>iState_</c> constant names, and the sampler keys the table on the
    /// same value (<c>docs/speed-data.md</c> §4.5). So which blocks exist is decided
    /// by the graph after all -- not by which constants it declares, which is where
    /// the draugr pair looked underivable, but by which values it can write: the
    /// initial value, the tagging generators, the state manager's rows and the
    /// expressions (<see cref="StateKeys"/>). That is 128 blocks over the 41 projects
    /// with a <c>BSSpeedSamplerModifier</c>.
    /// </para>
    /// <para>
    /// 76 of vanilla's 86 are among them. The 10 that are not, the engine never asks
    /// for: eight are the projects without a sampler, whose tables nothing reads, and
    /// two -- the spriggan's and the lurker's key 1 -- are constants no writer in the
    /// graph ever assigns, so <c>iState</c> cannot reach them. The 52 vanilla does not
    /// ship are keys the graph writes and the sweep skipped: FirstPerson's stances,
    /// the draugr skeleton's weapons, the player's and the horse's mounted states,
    /// the netch's sprint, the sphere's ranged stance, the horker's swim.
    /// </para>
    /// <para>
    /// Six writable keys get no block on purpose. Driven into their state, nothing
    /// sampler-fed is live beside them and the pose there carries no root motion --
    /// the rider on its saddle offset, the first-person camera -- or, for the horse's
    /// swim, a clip whose motion the cache records as no travel at all. Nothing the
    /// animation does there can be measured, and an absent block is the game's own
    /// answer: the request passes through unchanged.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheTableHoldsEveryKeyTheGraphCanWrite()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Inferred inferred = Infer(cache, Masters.Read());

        SpeedDataFile vanilla = cache.SpeedData!;

        Assert.Equal(41, inferred.Projects);
        Assert.Equal(49, vanilla.Projects.Count);
        Assert.True(inferred.File.Projects.All(vanilla.Projects.Contains));

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
            "\n\ninvented:\n" + string.Join("\n", made.Except(shipped).OrderBy(x => x.Item1)) +
            "\n\nhow:\n" + string.Join("\n", inferred.How.OrderBy(h => h.Key).Select(h => $"{h.Key.Project} {h.Key.Key} {h.Value}")));

        Assert.Equal(86, shipped.Count);
        Assert.Equal(128, made.Count);
        Assert.Equal(76, made.Intersect(shipped).Count());
        Assert.Equal(52, made.Except(shipped).Count());
        Assert.Equal(0, inferred.Unbuildable);

        (string, uint)[] unread =
            [
                ("AtronachFlame", 1), ("AtronachStormProject", 0), ("ChaurusFlyer", 0), ("Dragon_Priest", 0),
                ("DragonProject", 0), ("IceWraithProject", 0), ("WispProject", 0), ("WitchlightProject", 0),
            ];
        (string, uint)[] unwritable = [("BenthicLurkerProject", 1), ("Spriggan", 1)];
        Assert.Equal(unread.Concat(unwritable).Order(), shipped.Except(made).Order());

        // How each block was placed: the graph declares it under a tag or a row, an
        // expression pairs it, the graph driven into a tagged state shows the ladder
        // live beside it (the attacks over the weapon locomotion, the perk states
        // over the bow and block locomotion) or, with none live, the flat pose it
        // plays (the sprints, the bleedout, the power attacks, the falls), a tag
        // over a compass of clips makes it flat, or the graph is run.
        var routes = inferred.How.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(37, routes["declared"]);
        Assert.Equal(50, routes["paired"]);
        Assert.Equal(27, routes["tagged"]);
        Assert.False(routes.ContainsKey("flat"));
        Assert.False(routes.ContainsKey("alike"));
        Assert.Equal(13, routes["evaluated"]);
        Assert.Equal(1, routes["standing"]);
        Assert.Equal(6, routes["unread"]);
        Assert.Equal(
            [("DefaultFemale", 63), ("DefaultMale", 63),
             ("FirstPerson", 1), ("FirstPerson", 61), ("FirstPerson", 63), ("HorseProject", 63)],
            inferred.How.Where(h => h.Value == "unread").Select(h => h.Key).Order());
    }

    /// <summary>
    /// Where a block was recovered, the curve is close: 90% of the shipped points.
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

            BehaviorRoot root = BehaviorRoot.Of(path!)!.Value;
            var constants = StateConstants.Of(walk, root);
            IReadOnlyList<StateKeys.Writer> writers = StateKeys.Writers(walk, root);
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
                    // Does driving the graph into that state -- by the events that
                    // enter it, the choosers on the way and what the transitions ask
                    // -- show that state's own ladder live?
                    // A key the graph rests at -- its initial value -- is read at rest.
                    var found = TaggedAt(graph, walk, properties, actor, built, parameter, (int)mine.Key, writers)
                                ?? (writers.Any(w => w.Key == (int)mine.Key && w.By == StateKeys.By.Initial)
                                    ? StateAt(graph, walk, properties, actor, built, parameter, (int)mine.Key)
                                    : null);
                    if (found is null)
                    {
                        _evaluatorMissed++;
                        _mismatches.Add($"{name} key {mine.Key}: declared '{built.First(b => b.State.Key == (int)mine.Key).State.State.m_name}' -> evaluator nothing");
                    }
                    else if (built.Any(b => b.State.Key == (int)mine.Key && ReferenceEquals(b.Arms, found))) _evaluatorAgreed++;
                    else
                    {
                        _evaluatorDiffered++;
                        LocomotionState want = built.First(b => b.State.Key == (int)mine.Key).State;
                        string got = built.FirstOrDefault(b => ReferenceEquals(b.Arms, found)).State.State?.m_name ?? "(a compass off the graph)";
                        _mismatches.Add(
                            $"{name} key {mine.Key}: declared '{want.State.m_name}' " +
                            $"-> evaluator '{got}'");
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

        // The 76 shipped blocks the engine asks for (TheTableHoldsEveryKeyTheGraphCanWrite).
        Assert.Equal(76, blocks);
        Assert.Equal(1444, records);
        Assert.Equal(16930, points);

        // 90%. The rate is held down by RieklingProject, in the denominator with
        // 1037 points and 138 of them right.
        Assert.Equal(15296, pointsHeld);
        Assert.Equal(1252, recordsHeld);
        Assert.Equal(49, blocksHeld);

        // The spider centurion's arms play at a rate an expression computes from the
        // sampled speed, and the whole block follows from evaluating it.
        Assert.Contains(_perKey, line => line.StartsWith("DwarvenSpiderCenturionProject ") && line.Contains(" 61/61 "));

        Assert.Equal(24, _declared);
        Assert.Equal(63, _sharedBlocks);
        Assert.Equal(13, _newBlocks);
        Assert.Equal(13275, _sharedHeld);

        // On the 24 the graph declares, driving it into the declared state -- by
        // the events that enter it, the choosers on the way, what the transitions ask
        // and the end triggers held -- shows that state's own ladder live every time.
        // Pinning iState and running with the movement selectors alone landed 7 and
        // left 17 in default locomotion, every one a stance the right state 7 times.
        // Every one of the 17 differences is a stance -- sneaking, bow drawn,
        // blocking, one- and two-handed, magic ready, casting -- and the key's own
        // name says which: iState_NPCSneaking, iState_NPCBowDrawn, iState_NPC1HM and
        // the rest. The movement selectors alone leave the graph in default
        // locomotion, which is correct; the stance is simply not being set. One of
        // them is known: iIsInSneak = 1 reaches Sneak_Locomotion_State, found by
        // searching the player's 301 variables against these declarations. The
        // others need a combination, and nothing here sets any of them yet.
        Assert.Equal(24, _evaluatorAgreed);
        Assert.Equal(0, _evaluatorDiffered);
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
    /// Blocks the engine never asks for are not written and not scored.
    /// </remarks>
    [MastersFact]
    public void TheWrittenFileReadsBackAsTheGameReadsIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile written = SpeedDataFile.Parse(Infer(cache, Masters.Read()).File.Write());
        SpeedDataFile shipped = cache.SpeedData!;

        int points = 0, held = 0, records = 0, recordsHeld = 0, unasked = 0;
        List<string> perBlock = [];
        foreach (string name in shipped.ProjectNames)
            foreach (SpeedEntry entry in shipped.Block(name)!.Entries.Where(e => e.Records.Count > 0))
            {
                int blockPoints = 0, blockHeld = 0;
                // A block the engine never asks for -- a project with no sampler, a key
                // no graph writes -- is not in the file, and is not scored.
                SpeedEntry? mine = written.Block(name)?.Entries.FirstOrDefault(e => e.Key == entry.Key);
                if (mine is null) { unasked++; continue; }
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
                        if (ok) { held++; blockHeld++; } else whole = false;
                        blockPoints++;
                    }
                    if (whole) recordsHeld++;
                }
                perBlock.Add($"{name} {entry.Key} {blockHeld}/{blockPoints}");
            }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rebuild-readback.txt"),
            $"unasked {unasked}\nrecords {records}\nrecordsHeld {recordsHeld}\npoints {points}\nheld {held}\n\n" + string.Join("\n", perBlock));

        // 84.0% of the shipped points on the 76 blocks, read through the game's own
        // lookup. 79.5% before the horse's and the werewolf's motion was read at their
        // caches' own numbers (ActorProject.MotionOf), which took them from 0 and 26 of
        // their points to 289 and 217, and 82.3% before every sweep reached 324.5
        // (SweepFloor), which a slow creature's doubled speed fell short of. Three choices made for the
        // engine cost against the shipped file and are kept, measured before that fix:
        // no sampler offset (13,622 with it, against 13,451), every heading swept from
        // zero where the shipped sweeps settle in from 0.5 (13,550 starting there), and
        // a tolerance of 0.5 (13,288 at the game's 2).
        Assert.Equal(10, unasked);
        Assert.Equal(16930, points);
        Assert.Equal(14215, held);
        Assert.Equal(587, recordsHeld);
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
