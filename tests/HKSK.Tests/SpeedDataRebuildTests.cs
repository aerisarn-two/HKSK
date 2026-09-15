using HKSK.Behavior;
using HKSK.Engine;
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

    private int _projectPoints, _projectHeld;
    private int _sharedBlocks, _newBlocks, _declared;
    private int _evaluatorAgreed, _evaluatorDiffered, _evaluatorMissed;
    private readonly List<string> _mismatches = [];
    private int _sharedPoints, _sharedHeld, _newPoints, _newHeld;

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
    /// <summary>The events that put a creature into locomotion.</summary>
    private static readonly Events Moving = Events.Of("moveStart", "moveForward");

    /// <summary>
    /// Builds a speed table by running each graph, one locomotion state at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Which animations belong to a state is no longer guessed.</strong>
    /// The state is held -- <c>iState</c> is pinned to the key, so the modifier
    /// pass reads it instead of recomputing it -- the graph is driven into
    /// locomotion, and whatever speed ladder comes out live is the one that state
    /// uses. Earlier this was a pairing heuristic over movement types, locomotion
    /// states and expression sites.
    /// </para>
    /// <para>
    /// <strong>Each record is built at its own heading.</strong> Record n is
    /// evaluated with <c>Direction</c> set to that record's direction, so where a
    /// project's compass is driven by it the graph picks the arm itself. Earlier a
    /// compass was assembled by hand and an arm chosen by index.
    /// </para>
    /// <para>
    /// The goal-speed grid is still a grid over the ladder's own range: the rule by
    /// which the sampler kept about a dozen of some 650 swept positions is not
    /// known, and nothing here has changed that.
    /// </para>
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
            string? parameter = Ladders.ParameterOf(walk);
            if (parameter is null) continue;

            // The project is read once and evaluated many times -- one run per state
            // per heading -- so the walk and the character properties are hoisted.
            hkbBehaviorGraph? graph = null;
            foreach (ProjectStep step in walk.Steps)
                if (step.Node is hkbBehaviorGraph found) { graph = found; break; }

            if (graph is null) continue;
            Properties properties = Properties.OfProject(path);

            var constants = StateConstants.Of(walk, root);
            var variables = new ProjectVariables(walk.Steps);
            List<StateAssignment> expressions = [.. StateExpressions.In(walk)];
            var placed = StateExpressions.WithNodes(walk).ToList();

            var built = LocomotionStates.In(walk, variables)
                .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
                .Where(st => st.Arms.Count > 0)
                .ToList();

            foreach ((string constant, int key) in constants.OrderBy(c => c.Value))
            {
                string movement = constant["iState_".Length..];
                if (!movements.TryGetValue(movement, out MovementType type)) continue;

                // What the graph declares, first: a BSiStateTaggingGenerator tags the
                // subtree it guards and a BSIStateManagerModifier declares a table of
                // (machine, state) pairs, and ProjectWalk.KeyOf reads both. Only where
                // the graph declares nothing does the heuristic, then the evaluator,
                // get a turn.
                var arms = built.FirstOrDefault(b => b.State.Key == key).Arms;

                arms ??= FlatAt(walk, properties, actor, key);

                if (arms is null)
                {
                    (LocomotionState? paired, Pairing.By _) = Pairing.For(
                        walk, built, key, type, constants.Count, expressions, constants, placed);

                    arms = paired is { } chosen
                        ? built.First(b => b.State.Equals(chosen)).Arms
                        : StateAt(graph, walk, properties, actor, built, parameter, key);
                }

                if (arms is null) { unbuildable++; continue; }

                float top = arms.Max(a => a.Ladder.Rungs.Count == 0 ? 0f : a.Ladder.Rungs[^1].Weight);
                if (top <= 0f) { unbuildable++; continue; }

                var entry = new SpeedEntry { Key = (uint)key };
                block.Entries.Add(entry);
                blocks++;

                float share = Share(graph, walk, properties, parameter);

                foreach (float heading in Headings())
                {
                    var record = new SpeedRecord { Direction = heading };
                    entry.Records.Add(record);

                    for (int i = 0; i <= 16; i++)
                    {
                        float x = top * i / 16f;
                        record.Points.Add(new SpeedPoint(
                            x, share * SpeedSampler.Sample(arms, heading, x - SpeedLadder.SamplerOffset)));
                    }
                }
            }
        }

        return new Inferred(file, projects, blocks, unbuildable);
    }

    /// <summary>
    /// The compass a state uses, found by running the graph rather than by pairing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>iState</c> is pinned to the key -- in play it is an output, computed by an
    /// expression, so holding it is what asks the graph "what do you do in this
    /// state" -- and the graph is driven into locomotion. Whichever parametric blend
    /// on the sampler's output comes out live is that state's speed ladder, and the
    /// locomotion state that owns it is the one to build from.
    /// </para>
    /// <para>
    /// This replaces a pairing heuristic over movement types, expression sites and
    /// constant counts. It leaves only 7 declared states unaccounted for, against 50.
    /// </para>
    /// </remarks>
    private static List<(float Direction, SpeedLadder Ladder)>? StateAt(
        hkbBehaviorGraph graph, ProjectWalk walk, Properties properties, ActorProject actor,
        List<(LocomotionState State, List<(float Direction, SpeedLadder Ladder)> Arms)> built,
        string parameter, int key)
    {
        Evaluation run = ActiveGenerators.Evaluate(graph, walk, tables =>
        {
            foreach (Variables variables in tables.Values)
            {
                // Bethesda's own selectors: moving, forward, not turning in place.
                // iState is not among them -- it is the label the engine reads to
                // know which movement type applies, which is why nothing in any of
                // the 49 graphs binds a generator to it.
                variables.Set("iSyncIdleLocomotion", 1);
                variables.Set("iSyncForwardState", 0);
                variables.Set("iSyncTurnState", 1);
                variables.Set("Direction", 0f);
                variables.Set("TurnDelta", 0f);
                variables.Set("TurnDeltaDamped", 0f);
                variables.Set("Speed", 100f);
            }
        }, properties, Moving);

        var live = Ladders.ActiveIn(run, walk, parameter);

        foreach ((hkbBlenderGenerator blend, float _, float _) in live)
            foreach ((LocomotionState state, var arms) in built)
                foreach (SpeedConsumer consumer in state.Blends)
                    if (ReferenceEquals(consumer.Node, blend))
                        return arms;

        // Nothing matched because nothing reads the sampler: the netch and the
        // slaughterfish run their ladders on SpeedDamped and raw Speed, so they have
        // no locomotion state in the ConsumersIn sense and no arms were built for
        // them. Take the compass straight off the graph instead.
        return live.Count == 0 ? null : ArmsAround(live[0].Blend, run.Walk ?? walk, actor);
    }

    /// <summary>
    /// The flat curve a key gets when the graph tags it but gives it no ladder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not every locomotion is a blend of gaits.</strong> The player's
    /// <c>MT_Drunk_iStateGen</c> guards a state machine whose moving state is one
    /// clip, <c>IdleDrunk_Walk</c>, and <c>Sprint_iStateGen</c> guards a blend of
    /// two manual selectors. Neither holds a blend the sampler drives, so neither
    /// becomes a locomotion state and neither key could be answered -- both were
    /// paired to an attack state and held 0 of their 83 points.
    /// </para>
    /// <para>
    /// A creature playing one clip travels at that clip's own speed whatever speed
    /// it is asked for, which is a ladder of one rung and therefore flat. The
    /// shipped file agrees exactly: every point of the player's key 15 reads 29.47
    /// and <c>IdleDrunk_Walk</c> delivers 29.469, and every point of key 1 reads
    /// 370.37 against <c>MT_SprintForward</c>'s 370.365.
    /// </para>
    /// <para>
    /// <strong>Which clip is not guessed.</strong> The subtree is evaluated, so the
    /// machine settles into its moving state and the selectors resolve their own
    /// bindings: the sprint's is driven by <c>iRightHandEquipped</c> and picks the
    /// unarmed arm because that is what the variable starts at, which is the case
    /// the shipped block records. Only a subtree with exactly one travelling clip
    /// is answered -- several means a blend this does not model, and guessing one
    /// would be worse than leaving the key to the heuristic.
    /// </para>
    /// </remarks>
    private static List<(float Direction, SpeedLadder Ladder)>? FlatAt(
        ProjectWalk walk, Properties properties, ActorProject actor, int key)
    {
        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not BSiStateTaggingGenerator tag) continue;
            if (tag.m_iStateToSetAs != key || tag.m_pDefaultGenerator is not { } under) continue;

            Evaluation run = ActiveGenerators.Evaluate(under, walk, tables =>
            {
                foreach (Variables variables in tables.Values)
                {
                    variables.Set("iSyncIdleLocomotion", 1);
                    variables.Set("iSyncForwardState", 0);
                    variables.Set("iSyncTurnState", 1);
                    variables.Set("Direction", 0f);
                    variables.Set("Speed", 100f);
                }
            }, properties, Moving);

            // Only where the tag guards no speed-driven blend at all. Where it
            // guards one, that ladder is the creature's locomotion and a single
            // clip taken out of it is a moment, not a curve. The live set is what
            // is asked, not the walk's parent chain: a node the walk reached by
            // another route first records that route's parent, and the falmer's bow
            // ladders do, so asking the ancestry says there is no ladder there when
            // eight of them are running.
            HashSet<IHavokObject> ladders = [];
            foreach (SpeedConsumer consumer in Locomotion.ConsumersIn(walk.Steps))
                ladders.Add(consumer.Node);

            SpeedRung? only = null;
            bool several = false;

            foreach (ActiveNode node in run.Active)
            {
                if (ladders.Contains(node.Generator)) { several = true; break; }
                if (node.Generator is not hkbClipGenerator clip) continue;
                if (clip.m_animationName is not { Length: > 0 } animation) continue;
                if (clip.m_playbackSpeed <= 0f) continue;

                string stem = Path.GetFileNameWithoutExtension(animation.Replace('\\', '/'));
                if (actor.Animation(stem)?.Motion is not { Duration: > 0f } motion) continue;
                if (motion.Translations.Count == 0 || motion.Translations[^1].Value.Length() <= 0f) continue;

                if (only is not null) { several = true; break; }

                    float duration = motion.Duration / clip.m_playbackSpeed;

                // The rung sits on the speed axis at the speed it delivers, which is
                // the only position a single clip has.
                only = new SpeedRung(
                    motion.Translations[^1].Value.Length() / duration,
                    motion.Translations[^1].Value, duration, stem);
            }

            if (several || only is not { } rung) continue;

            return [(0f, new SpeedLadder([rung]) { Name = tag.m_name })];
        }

        return null;
    }

    /// <summary>
    /// The share of the character's movement the locomotion ladder accounts for.
    /// </summary>
    /// <remarks>
    /// A blend mixes root motion over <c>weight * worldFromModelWeight</c>, so a
    /// ladder mixed against an animation that is in the world-from-model blend and
    /// stands still delivers only its share of the travel. The daedra's locomotion
    /// sits under a blend that mixes it half and half with an idle, and its table is
    /// exactly half what the ladder alone gives.
    /// </remarks>
    private static float Share(
        hkbBehaviorGraph graph, ProjectWalk walk, Properties properties, string parameter)
    {
        Evaluation run = ActiveGenerators.Evaluate(graph, walk, tables =>
        {
            foreach (Variables variables in tables.Values)
            {
                variables.Set("iSyncIdleLocomotion", 1);
                variables.Set("iSyncForwardState", 0);
                variables.Set("iSyncTurnState", 1);
                variables.Set("Direction", 0f);
                variables.Set("Speed", 0f);
            }
        }, properties, Moving);

        foreach ((hkbBlenderGenerator _, float _, float motion) in
                 Ladders.ActiveIn(run, walk, parameter))
            return motion;

        return 1f;
    }

    /// <summary>
    /// A compass built from the graph: the nearest parametric blend above a ladder
    /// whose children lead to ladders, each child's weight being its heading.
    /// </summary>
    private static List<(float Direction, SpeedLadder Ladder)>? ArmsAround(
        hkbBlenderGenerator ladder, ProjectWalk walk, ActorProject actor)
    {
        hkbBlenderGenerator? compass = null;
        foreach (ProjectStep above in walk.Ancestors(ladder))
            if (above.Node is hkbBlenderGenerator blend && (blend.m_flags & 16) != 0)
            { compass = blend; break; }

        if (compass is null)
        {
            SpeedLadder only = SpeedLadder.FromBlender(ladder, actor);
            return only.Rungs.Count == 0 ? null : [(0f, only)];
        }

        List<(float, SpeedLadder)> arms = [];

        foreach (hkbBlenderGeneratorChild child in compass.m_children ?? [])
        {
            if (child.m_generator is null) continue;

            hkbBlenderGenerator? under = child.m_generator as hkbBlenderGenerator
                ?? Descendants(child.m_generator, walk).FirstOrDefault(b => (b.m_flags & 16) != 0);

            if (under is null) continue;

            SpeedLadder built = SpeedLadder.FromBlender(under, actor);
            if (built.Rungs.Count > 0) arms.Add((child.m_weight, built));
        }

        return arms.Count == 0 ? null : arms;
    }

    private static IEnumerable<hkbBlenderGenerator> Descendants(IHavokObject from, ProjectWalk walk)
    {
        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBlenderGenerator blend)
                foreach (ProjectStep above in walk.Ancestors(blend))
                    if (ReferenceEquals(above.Node, from)) { yield return blend; break; }
    }

    /// <summary>
    /// How much of the shipped table the inference reproduces, and how much it
    /// invents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list comes out right -- all 49, because every project with an
    /// animation cache has a block and nothing else does. The key set does not.
    /// It writes 141 blocks, of which <strong>77 are ones the game ships</strong> --
    /// 90% of the file, against 51 before the evaluator. It misses 9 and invents
    /// 64, and only 1 declared movement type cannot be placed at all, against 50.
    /// </para>
    /// <para>
    /// <strong>Of the 9 it misses, 8 are impossible from the graph.</strong>
    /// AtronachFlame, AtronachStorm, ChaurusFlyer, Dragon_Priest, DragonProject,
    /// IceWraith, Wisp and Witchlight have no <c>BSSpeedSamplerModifier</c> at all,
    /// and the game ships a one-key table for each -- nothing in those graphs reads
    /// a speed table, so there is no ladder to rebuild from. The ninth is the
    /// dwarven spider, whose directional blend carries no binding at all.
    /// </para>
    /// <para>
    /// The riekling used to be a tenth. Its combat state holds two compasses, and
    /// read as one they cancelled: no arm carried a top weight, so nothing could be
    /// built. Kept apart, the block builds -- but it holds only 99 of its 1037
    /// points, and 149 with its other locomotion state, so what it needs now is a
    /// model rather than a key.
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
    public void TheInferenceRecoversSeventySevenOfTheEightySixBlocks()
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
        Assert.Equal(141, made.Count);

        Assert.Equal(77, made.Intersect(shipped).Count());   // recovered, was 51
        Assert.Equal(9, shipped.Except(made).Count());       // missed, was 35
        Assert.Equal(64, made.Except(shipped).Count());      // invented, was 41
        Assert.Equal(1, inferred.Unbuildable);               // unplaceable, was 50
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
            string? parameter = Ladders.ParameterOf(walk);
            if (parameter is null) continue;

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

                if (arms is null)
                {
                    (paired, _) = Pairing.For(
                        walk, built, (int)mine.Key, type, constants.Count, expressions, constants, placed);

                    arms = paired is { } chosen
                        ? built.First(b => b.State.Equals(chosen)).Arms
                        : StateAt(graph, walk, properties, actor, built, parameter, (int)mine.Key);
                }

                if (arms is null) continue;

                bool shared = declared || flat || paired is not null;
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
                        if (point.Y <= 0f) { pointsHeld++; _projectHeld++; _projectPoints++; continue; }

                        // The record's own heading picks the arm; the ladder under it
                        // answers at the shipped goal speed.
                        double y = share * SpeedSampler.Sample(
                            arms, record.Direction, point.X - SpeedLadder.SamplerOffset);

                        _projectPoints++;
                        bool ok = Math.Abs(y - point.Y) / point.Y <= Tolerance;
                        if (shared) { _sharedPoints++; if (ok) _sharedHeld++; }
                        else { _newPoints++; if (ok) _newHeld++; }

                        if (ok) { pointsHeld++; _projectHeld++; } else recordHolds = false;
                    }

                    if (recordHolds) recordsHeld++; else blockHolds = false;
                }

                if (blockHolds) blocksHeld++;
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
            string.Join("\n", per.OrderByDescending(x => x)));

        Assert.Equal(77, blocks);
        Assert.Equal(1463, records);
        Assert.Equal(17629, points);

        // 14324 against the 10145 the pairing alone reached, over 77 blocks against
        // 51. The rate falls from 87% to 81% because the blocks reached late are
        // the harder ones -- the 15 the evaluator supplies hold 1914 of 2909, and
        // RieklingProject, the newest of them, holds 99 of 1037 whichever of its two
        // locomotion states is chosen -- but every absolute count is up.
        Assert.Equal(14324, pointsHeld);
        Assert.Equal(1096, recordsHeld);
        Assert.Equal(44, blocksHeld);

        Assert.Equal(25, _declared);
        Assert.Equal(62, _sharedBlocks);
        Assert.Equal(15, _newBlocks);
        Assert.Equal(12410, _sharedHeld);

        // On the 25 the graph declares, running it lands in the right state 6 times.
        // Every one of the 18 differences is a stance -- sneaking, bow drawn,
        // blocking, one- and two-handed, magic ready, casting -- and the key's own
        // name says which: iState_NPCSneaking, iState_NPCBowDrawn, iState_NPC1HM and
        // the rest. The movement selectors alone leave the graph in default
        // locomotion, which is correct; the stance is simply not being set. One of
        // them is known: iIsInSneak = 1 reaches Sneak_Locomotion_State, found by
        // searching the player's 301 variables against these declarations. The
        // others need a combination, and nothing here sets any of them yet.
        Assert.Equal(6, _evaluatorAgreed);
        Assert.Equal(18, _evaluatorDiffered);
        Assert.Equal(1, _evaluatorMissed);
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
