using System.Numerics;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;

namespace HKSK.Speed;

/// <summary>
/// Writes <c>speeddatasinglefile.txt</c> from the game's other assets.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is read from a shipped speed table.</strong> Which projects get a
/// block, which keys each one carries, which goal speeds are swept and kept, and what
/// speed comes back are decided from three inputs: the behaviour graphs and the
/// animation cache (a <see cref="SkyrimCache"/>), and the movement types and the races
/// that wear them, which live in the masters and are handed in because this library
/// does not open plugins.
/// </para>
/// <para>
/// It is written for the engine that reads it rather than to reproduce the shipped
/// file: a block for every state the graph can put <c>iState</c> at, since that is the
/// key the sampler asks for (<see cref="StateKeys"/>); the blend law at the goal speed
/// itself, with no offset, since the query applies none; a sweep from zero, since below
/// its first point a record is read from the origin; and past every speed the movement
/// type asks for, since above its last point the request passes through unchanged.
/// Where that differs from the shipped table, <c>docs/speed-data.md</c> §8 says why.
/// </para>
/// </remarks>
public static class SpeedDataGenerator
{
    /// <summary>Builds the whole table.</summary>
    /// <param name="cache">The game's animation cache, with the meshes folder its behaviours live in.</param>
    /// <param name="movements">Every movement type the masters define, by name.</param>
    /// <param name="tolerance">
    /// How far a dropped point may sit from the line the game draws between its
    /// neighbours. The game's own files were thinned at
    /// <see cref="SpeedRecord.RetentionTolerance"/>; the default here keeps four times
    /// as much of the curve, in a file a few times the size.
    /// </param>
    public static SpeedDataFile Generate(
        SkyrimCache cache,
        IReadOnlyDictionary<string, MovementType> movements,
        float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(movements);
        if (!(tolerance > 0f)) throw new ArgumentOutOfRangeException(nameof(tolerance));

        return Infer(cache, movements, tolerance).File;
    }

    /// <summary>
    /// How far a dropped point may sit from the line that replaces it, in units per
    /// second: a quarter of the game's own 2, which is coarse against the half-unit grid
    /// the sweep runs on.
    /// </summary>
    public const float DefaultTolerance = 0.5f;

    /// <summary>
    /// How far past the fastest speed a movement type asks for the sweep runs. Above a
    /// record's last point the query hands the request back unchanged, so a request the
    /// sweep does not reach is answered as if there were no table; <c>SpeedMult</c>
    /// scales what the game asks for, and a doubled speed is the ordinary reach of it.
    /// The response is flat there, so the extra costs a record one point.
    /// </summary>
    public const float SweepBeyond = 2f;

    /// <summary>The 19 headings every shipped block carries, at 0.05 apart.</summary>
    // Accumulated, not multiplied: the game's headings are 0.05f added nineteen times,
    // and 0.05f * i differs from that in 12 of the 19 (SpeedRecord.StandardDirections).
    internal static IEnumerable<float> Headings() => SpeedRecord.StandardDirections();

    internal sealed record Inferred(
        SpeedDataFile File, int Projects, int Blocks, int Unbuildable,
        IReadOnlyDictionary<(string Project, int Key), string> How);

    /// <summary>
    /// Builds a speed table from the graphs alone.
    /// </summary>
    /// <summary>The events that put a creature into locomotion.</summary>
    internal static readonly Events Moving = Events.Of("moveStart", "moveForward");

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
    /// A project is in the table when it carries a <c>BSSpeedSamplerModifier</c>, the
    /// one reader the game has, and gets a block for every value its graph can put
    /// <c>iState</c> at (<see cref="StateKeys"/>), whether or not the value names a
    /// movement type: the sampler asks for the key either way.
    /// </para>
    /// </remarks>
    internal static Inferred Infer(
        SkyrimCache cache, IReadOnlyDictionary<string, MovementType> movements,
        float tolerance = DefaultTolerance)
    {
        var file = new SpeedDataFile();
        int projects = 0, blocks = 0, unbuildable = 0;
        var how = new Dictionary<(string, int), string>();

        foreach (CacheProject project in cache.OpenAll())
        {
            if (project is not ActorProject actor) continue;

            string? path = cache.FindProjectFile(project.Name);
            if (path is null) continue;

            if (BehaviorRoot.Of(path) is not { } root) continue;

            ProjectWalk walk = ProjectWalk.Of(path);

            // The sampler is the only thing that reads the table. A project without
            // one -- the eight flyers and hoverers -- is left out, and the game
            // answers a request for it as it answers any absent project: unchanged.
            if (Ladders.ParameterOf(walk) is not { } parameter) continue;

            projects++;
            file.Projects.Add(SpeedDataFile.ListingFor(project.Name));

            var block = new SpeedProjectBlock();
            file.Blocks.Add(block);

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

            foreach ((int key, IReadOnlySet<StateKeys.By> writtenBy) in StateKeys.Writable(walk, root))
            {
                // The movement type, where the key names one: it bounds the sweep and
                // helps pair an undeclared key. A key that names none -- the player's
                // mounted states, declared in the horse's file -- is asked for all
                // the same.
                MovementType? type = StateConstants.MovementTypeOf(constants, key) is { } movement
                                     && movements.TryGetValue(movement, out MovementType found) ? found : null;

                // What the graph declares, first: a BSiStateTaggingGenerator tags the
                // subtree it guards and a BSIStateManagerModifier declares a table of
                // (machine, state) pairs, and ProjectWalk.KeyOf reads both. Only where
                // the graph declares nothing does the heuristic, then the evaluator,
                // get a turn.
                string route = "declared";
                var arms = built.FirstOrDefault(b => b.State.Key == key).Arms;

                if (arms is null && FlatAt(walk, properties, actor, key) is { } flat) { arms = flat; route = "flat"; }

                if (arms is null)
                {
                    (LocomotionState? paired, Pairing.By _) = type is { } known
                        ? Pairing.For(walk, built, key, known, constants.Count, expressions, constants, placed)
                        : (null, default);

                    if (paired is { } chosen) { arms = built.First(b => b.State.Equals(chosen)).Arms; route = "paired"; }
                    else if (LikeAnother(built, constants, movements, type) is { } alike) { arms = alike; route = "alike"; }
                    // A tag or a manager row says exactly which subtree holds the key.
                    // When nothing under it reads the sampler and no other reading
                    // applies -- the player's mounted states, whose blends run on the
                    // horse's speed -- the honest block is none: running the graph with
                    // iState pinned would hand the key another state's curve, since
                    // nothing in any graph selects on iState.
                    else if (writtenBy.All(by => by is StateKeys.By.Tag or StateKeys.By.Manager)) { how[(project.Name, key)] = "unread"; continue; }
                    else if (StateAt(graph, walk, properties, actor, built, parameter, key) is { } run) { arms = run; route = "evaluated"; }
                }

                // Nothing in this graph reads a speed into a blend, so the creature
                // has no curve: whatever it plays, it plays at one speed.
                if (built.Count == 0 && arms is null && Standing(walk, properties, actor) is { } standing) { arms = standing; route = "standing"; }

                if (arms is null) { unbuildable++; how[(project.Name, key)] = "unbuildable"; continue; }
                how[(project.Name, key)] = route;

                // A creature that cannot move by root motion has a curve, and it is
                // zero. The grid it is written on is degenerate because the ladder
                // gives no extent, and the shipped one spans 0 to 324.5 like 74 of
                // the 86 blocks do -- where that extent comes from is not derived
                // here, and the curve is zero at every x either way.
                bool still = arms.All(a => a.Ladder.Rungs.All(r => r.Travel.Length() <= 0f));

                float top = arms.Max(a => a.Ladder.Rungs.Count == 0 ? 0f : a.Ladder.Rungs[^1].Weight);
                if (top <= 0f && !still) { unbuildable++; continue; }
                if (arms.Any(a => a.Ladder.Rungs.Count == 0)) { unbuildable++; continue; }

                var entry = new SpeedEntry { Key = (uint)key };
                block.Entries.Add(entry);
                blocks++;

                float share = Share(graph, walk, properties, parameter);

                // Where the sweep stops. Above a record's last point the game does not
                // clamp -- it hands the request back unchanged (SpeedRecord.Sample) --
                // so the record has to reach every speed the game can ask for: the fastest
                // the movement type names, scaled by SweepBeyond for SpeedMult, and the
                // whole ladder, whose top rung may lie far above that -- the humanoids'
                // is the run at ten times speed, 3,510. Beyond the top rung the curve is
                // flat, so reaching past it costs one point.
                float fastest = type is { } t
                    ? new[] { t.ForwardWalk, t.ForwardRun, t.BackWalk, t.BackRun, t.LeftWalk, t.LeftRun, t.RightWalk, t.RightRun }.Max()
                    : 0f;
                float end = MathF.Max(top, SweepBeyond * fastest);

                foreach (float heading in Headings())
                {
                    // Swept on the file's own half-unit grid from zero: below a record's
                    // first point the query interpolates from the origin, so the first
                    // point has to be the response at zero itself. The curve is the blend
                    // law at the goal speed and nothing else -- the query applies no
                    // offset (docs/speed-data.md §4.2); the 0.0404 the shipped sweeps
                    // read early is the tool's, and SpeedLadder.Tabulate keeps it for
                    // reading that file.
                    List<SpeedPoint> sweep = [];
                    for (float x = 0f; x <= end; x += 0.5f)
                        sweep.Add(new SpeedPoint(x, share * SpeedSampler.Sample(arms, heading, x)));

                    entry.Records.Add(new SpeedRecord { Direction = heading, Points = SpeedRecord.Retain(sweep, tolerance) });
                }
            }
        }

        return new Inferred(file, projects, blocks, unbuildable, how);
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
    internal static List<(float Direction, SpeedLadder Ladder)>? StateAt(
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
    /// The state a key borrows because its movement type walks at another key's
    /// speeds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the player's movement types are perks rather than stances.
    /// <c>NPCBowDrawnQuickShot</c> and <c>NPCBlockingShieldCharge</c> carry the walk
    /// speeds of <c>NPCBowDrawn</c> and <c>NPCBlocking</c> exactly -- 120, 65.11,
    /// 74.89, 76.81 and 81, 71, 81, 81 -- and replace only the run speeds, with
    /// <c>NPCDefault</c>'s 370 and 205.25. So the perk changes how fast the player
    /// may run, not what animation plays, and the shipped tables say the same: key
    /// 16's records are key 3's and key 17's are key 4's, point for point.
    /// </para>
    /// <para>
    /// The graph does not declare either, and the speed pairing cannot help. It
    /// scores a state by how many of a movement type's eight speeds are rungs, so
    /// for key 16 the bow ladder matches the four walks and the default ladder
    /// matches the four runs, and it ties and says nothing; for key 17 nothing
    /// matches at all, because the block ladder's rungs are not the movement type's
    /// numbers.
    /// </para>
    /// <para>
    /// <strong>The masters settle it without the shipped file.</strong> Where an
    /// undeclared key's movement type walks at exactly the same four speeds as a
    /// declared key's, it is that key's locomotion -- and the walk speeds are the
    /// ones that matter, because a speed table is read at a goal speed and the
    /// ladder it indexes is the same ladder either way.
    /// </para>
    /// </remarks>
    internal static List<(float Direction, SpeedLadder Ladder)>? LikeAnother(
        List<(LocomotionState State, List<(float Direction, SpeedLadder Ladder)> Arms)> built,
        IReadOnlyDictionary<string, int> constants,
        IReadOnlyDictionary<string, MovementType> movements,
        MovementType? mine)
    {
        if (mine is not { } type) return null;

        List<(float Direction, SpeedLadder Ladder)>? found = null;

        foreach ((string constant, int key) in constants)
        {
            if (!movements.TryGetValue(constant["iState_".Length..], out MovementType other)) continue;
            if (!WalksAlike(type, other)) continue;

            var arms = built.FirstOrDefault(b => b.State.Key == key).Arms;
            if (arms is null) continue;

            // Two declared keys walking alike would make this ambiguous, so it only
            // answers where exactly one does.
            if (found is not null) return null;
            found = arms;
        }

        return found;
    }

    /// <summary>Whether two movement types walk at the same four speeds.</summary>
    internal static bool WalksAlike(MovementType a, MovementType b)
    {
        for (int quarter = 0; quarter < 4; quarter++)
        {
            (float mine, float _) = a.At(quarter * 0.25f);
            (float theirs, float _) = b.At(quarter * 0.25f);
            if (mine <= 0f || MathF.Abs(mine - theirs) > 0.005f) return false;
        }

        return true;
    }

    /// <summary>
    /// A compass whose arms are single clips rather than ladders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BleedOut_iStateGen</c> guards a machine whose moving state is
    /// <c>BleedOut_Moving_Blend</c>: a cyclic parametric blend of four clips at
    /// 0.25, 0.5, 0.75 and 1, and no speed ladder anywhere under it. A creature
    /// bleeding out has one animation per direction and no gait to choose, so the
    /// curve is flat in the goal speed and varies only with the heading -- and the
    /// shipped table says exactly that, 20.5 at every goal speed forward against
    /// <c>BleedOut_Forward</c>'s 20.500, and 17.96 sideways against
    /// <c>BleedOut_Right</c>'s 17.957.
    /// </para>
    /// <para>
    /// The dip between the arms is the same vector blend as anywhere else: at 0.15
    /// the file reads 13.51, below both of its neighbours, because the two clips
    /// point in different directions and their travel is mixed as a vector before
    /// it is divided.
    /// </para>
    /// <para>
    /// A blend is read as a compass rather than a ladder when every child weight is
    /// at most 1, which is <c>SpeedSampler</c>'s own test -- a rung is a speed in
    /// units per second and a heading is a fraction of a turn.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A clip whose playback speed an expression computes from the sampled speed,
    /// as a ladder over the goal speed.
    /// </summary>
    internal static SpeedLadder? RateLadder(
        Evaluation run, hkbClipGenerator clip, Vector3 travel, float duration, string stem, string? name)
    {
        if (run.Walk?.StepOf(clip) is not { } step) return null;
        if (!run.Variables.TryGetValue(step.File, out Variables? table)) return null;

        var binding = clip.m_variableBindingSet?.m_bindings?
            .FirstOrDefault(b => b.m_memberPath == "playbackSpeed" && b.m_bindingType == 0);
        if (binding is null || binding.m_variableIndex < 0 || binding.m_variableIndex >= table.Count) return null;
        string variable = table.NameOf(binding.m_variableIndex);

        Expression? writer = null;
        foreach (ProjectStep other in run.Walk.Steps)
        {
            if (other.File != step.File || other.Node is not hkbEvaluateExpressionModifier modifier) continue;
            foreach (var data in modifier.m_expressions?.m_expressionsData ?? [])
                if (Expression.Parse(data.m_expression) is { } parsed &&
                    string.Equals(parsed.Effect.Target, variable, StringComparison.OrdinalIgnoreCase))
                    writer = parsed;
        }
        if (writer is null || table.IndexOf("SpeedSampled") < 0) return null;

        float held = table.AsReal(table.IndexOf("SpeedSampled"));
        List<SpeedRung> rungs = [];
        for (float x = 0f; x <= 1000f; x += 0.5f)
        {
            table.Set("SpeedSampled", x);
            if (!writer.TryEvaluate(table, out float rate) || rate <= 0f) { rungs.Clear(); break; }
            rungs.Add(new SpeedRung(x, travel, duration / rate, stem));
        }
        table.Set("SpeedSampled", held);

        return rungs.Count == 0 ? null : new SpeedLadder(rungs) { Name = name };
    }

    internal static List<(float Direction, SpeedLadder Ladder)>? CompassOfClips(
        Evaluation run, ActorProject actor)
    {
        // A node reached by two routes appears once per route, and only the routes
        // that carry motion say anything about it.
        var share = new Dictionary<hkbGenerator, float>(ReferenceEqualityComparer.Instance);
        foreach (ActiveNode node in run.Active)
            share[node.Generator] = MathF.Max(share.GetValueOrDefault(node.Generator), node.Motion);

        foreach (ActiveNode node in run.Active)
        {
            if (node.Generator is not hkbBlenderGenerator blend) continue;
            if ((blend.m_flags & 16) == 0) continue;                       // FLAG_PARAMETRIC_BLEND

            // An arm's speed is read from the clip's own travel, and a compass that
            // is only part of the pose delivers only its share of the root motion:
            // Havok mixes root motion over weight times worldFromModelWeight and
            // renormalises (tools/hkmeasure, WFM=), and per-bone weights do not
            // enter it (BONES=). The chaurus flyer's locomotion sits at a third,
            // beside two partial-body idles that do not travel.
            float carried = share[blend];
            if (carried <= 0.001f || node.Motion < carried) continue;

            // A heading compass has to cover the circle, and Bethesda's do it with
            // four arms or eight. Three clips under a parametric blend are a
            // fragment of something else -- the dragon's ground locomotion is one,
            // and read as a compass it scores 3 of 51 where flat scores 51 of 51.
            IList<hkbBlenderGeneratorChild> children = blend.m_children ?? [];
            if (children.Count < 4) continue;

            List<(float, SpeedLadder)> arms = [];
            bool headings = true;

            foreach (hkbBlenderGeneratorChild child in children)
            {
                if (child.m_weight > 1.001f) { headings = false; break; }
                if (child.m_generator is not hkbClipGenerator clip) { headings = false; break; }
                if (clip.m_animationName is not { Length: > 0 } animation || clip.m_playbackSpeed == 0f)
                { headings = false; break; }

                string stem = Path.GetFileNameWithoutExtension(animation.Replace('\\', '/'));
                if (actor.Animation(stem)?.Motion is not { Duration: > 0f } motion ||
                    motion.Translations.Count == 0)
                { headings = false; break; }

                float rate = MathF.Abs(clip.m_playbackSpeed);
                Vector3 travel = motion.Translations[^1].Value * carried;
                float duration = motion.Duration / rate;

                // A clip whose playback speed is bound plays at whatever the graph
                // computes, and where an expression computes it from the sampled
                // speed the arm is a ladder in the goal speed: the spider
                // centurion's four arms each play at max(5, SpeedSampled) over a
                // declared speed of their own.
                if (RateLadder(run, clip, travel, motion.Duration, stem, blend.m_name) is { } bound)
                {
                    arms.Add((child.m_weight, bound));
                    continue;
                }

                var rung = new SpeedRung(
                    travel.Length() / duration,
                    clip.m_playbackSpeed < 0f ? -travel : travel,
                    duration, stem);

                arms.Add((child.m_weight, new SpeedLadder([rung]) { Name = blend.m_name }));
            }

            if (headings && arms.Count == children.Count) return arms;
        }

        return null;
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
    /// <summary>
    /// The curve a creature has when nothing in its graph reads a speed into a
    /// blend, which is no curve at all: one constant, or one per heading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six of the eight projects with no sampler have no ladder either, and their
    /// tables say so. `AtronachStorm`, `Wisp` and `Witchlight` ship flat zero and
    /// their live clips travel zero. `DragonProject` ships a flat 384 and the clip
    /// it rests in, `MTForwardGround`, travels at 384.001. `IceWraith` ships
    /// 319.67, 263.6, 230.52 repeating around the compass, and its `RunF`, `RunB`
    /// and `RunL` all travel at 319.667, the smaller numbers being the vector blend
    /// between them.
    /// </para>
    /// <para>
    /// So the same two readings the player's drunk and bleedout need answer these:
    /// run the graph and take what it plays. Rooting them at the graph rather than
    /// at a tag is the whole difference.
    /// </para>
    /// </remarks>
    internal static List<(float Direction, SpeedLadder Ladder)>? Standing(
        ProjectWalk walk, Properties properties, ActorProject actor)
    {
        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBehaviorGraph graph && graph.m_rootGenerator is { } root)
                return Flat(walk, properties, actor, root);

        return null;
    }

    internal static List<(float Direction, SpeedLadder Ladder)>? FlatAt(
        ProjectWalk walk, Properties properties, ActorProject actor, int key)
    {
        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not BSiStateTaggingGenerator tag) continue;
            if (tag.m_iStateToSetAs != key || tag.m_pDefaultGenerator is not { } under) continue;

            if (Flat(walk, properties, actor, under) is { } found) return found;
        }

        return null;
    }

    internal static List<(float Direction, SpeedLadder Ladder)>? Flat(
        ProjectWalk walk, Properties properties, ActorProject actor, hkbGenerator under)
    {
        {
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

            // A compass of flat clips, where the subtree has one: the bleedout is
            // four clips on a cyclic parametric blend and nothing else.
            if (CompassOfClips(run, actor) is { } compass) return compass;

            // Every travelling clip the subtree plays. They may be several -- the
            // sprint blends a right-side and a left-side variant of the same stride
            // -- and the curve is flat so long as they agree on a speed.
            SpeedRung? only = null;
            bool several = false;

            var carries = new Dictionary<hkbGenerator, float>(ReferenceEqualityComparer.Instance);
            foreach (ActiveNode node in run.Active)
                carries[node.Generator] = MathF.Max(carries.GetValueOrDefault(node.Generator), node.Motion);

            foreach (ActiveNode node in run.Active)
            {
                if (ladders.Contains(node.Generator)) { several = true; break; }
                if (node.Generator is not hkbClipGenerator clip) continue;
                if (clip.m_animationName is not { Length: > 0 } animation) continue;
                if (clip.m_playbackSpeed <= 0f) continue;

                // Same as the compass: the speed comes from the clip's own travel,
                // and a clip that is part of the pose delivers its share of it. The
                // routes that reach it carrying nothing say nothing about it.
                float carried = carries[clip];
                if (carried <= 0.001f || node.Motion < carried) continue;

                string stem = Path.GetFileNameWithoutExtension(animation.Replace('\\', '/'));
                if (actor.Animation(stem)?.Motion is not { Duration: > 0f } motion) continue;
                // A clip that does not travel makes a curve too, and it is zero.
                // AtronachStormProject, WispProject and WitchlightProject each ship
                // 38 points of exact zero while their movement types declare
                // 100/500, 100/300 and 500/500; all three rest on clips with no
                // root motion. Asking the project instead does not separate them --
                // all three own travelling animations they never reach from rest.
                if (motion.Translations.Count == 0) continue;

                float duration = motion.Duration / clip.m_playbackSpeed;
                float speed = carried * motion.Translations[^1].Value.Length() / duration;

                if (only is { } already)
                {
                    // Agreeing on a speed is what makes the curve flat. Differing on
                    // one is a ladder, and this does not model it.
                    if (MathF.Abs(already.Delivered - speed) > 0.005f * speed) { several = true; break; }
                    continue;
                }

                // The rung sits on the speed axis at the speed it delivers, which is
                // the only position a clip that is not part of a ladder has.
                only = new SpeedRung(
                    speed, carried * motion.Translations[^1].Value, duration, stem);
            }

            if (several || only is not { } rung) return null;

            // A zero curve is only the answer where the creature has no locomotion
            // to reach. AtronachStormProject, WispProject and WitchlightProject own
            // nothing that travels but staggers, recoils and a power attack, no two
            // of which agree on a speed -- and each ships 38 points of exact zero
            // against movement types declaring 100/500, 100/300 and 500/500. The ice
            // wraith rests on the same kind of non-travelling clip and is not one of
            // them: it owns RunF, RunB and RunL at 319.667, which is its shipped
            // forward speed to the digit.
            if (rung.Travel.Length() <= 0f && HeadingSet(actor) is not null) return null;

            return [(0f, new SpeedLadder([rung]) { Name = under.m_name })];
        }
    }

    /// <summary>
    /// The speed a project's own clips agree on, where three or more of them do.
    /// </summary>
    /// <remarks>
    /// Three clips travelling at one speed is a heading set -- forward, back and a
    /// side the compass mirrors -- and it says the creature moves whatever the graph
    /// was doing when it was asked. Reaction motion never looks like this: the storm
    /// atronach's four travelling clips are 150.21, 39.00, 44.50 and 23.49, and the
    /// witchlight's are 59.59, 176.98, 247.10 and 179.36.
    /// </remarks>
    internal static float? HeadingSet(ActorProject actor)
    {
        List<float> speeds = [];
        foreach (AnimationSlot slot in actor.Animations)
            if (slot.Motion is { Duration: > 0f, Translations.Count: > 0 } m &&
                m.Translations[^1].Value.Length() > 0f)
                speeds.Add(m.Translations[^1].Value.Length() / m.Duration);

        foreach (float speed in speeds)
            if (speeds.Count(other => MathF.Abs(other - speed) <= 0.005f * speed) >= 3)
                return speed;

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
    internal static float Share(
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

        // A ladder can be live under more than one parent, and its share of the
        // movement is then what each instance carries, added up: the netch's
        // locomotion hangs under both halves of a body blend at half the motion
        // each, and the creature travels at the whole of its speed.
        hkbBlenderGenerator? first = null;
        float share = 0f;

        foreach ((hkbBlenderGenerator blend, float _, float motion) in
                 Ladders.ActiveIn(run, walk, parameter))
        {
            first ??= blend;
            if (ReferenceEquals(blend, first)) share += motion;
        }

        return first is null ? 1f : share;
    }

    /// <summary>
    /// A compass built from the graph: the nearest parametric blend above a ladder
    /// whose children lead to ladders, each child's weight being its heading.
    /// </summary>
    internal static List<(float Direction, SpeedLadder Ladder)>? ArmsAround(
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

    internal static IEnumerable<hkbBlenderGenerator> Descendants(IHavokObject from, ProjectWalk walk)
    {
        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBlenderGenerator blend)
                foreach (ProjectStep above in walk.Ancestors(blend))
                    if (ReferenceEquals(above.Node, from)) { yield return blend; break; }
    }

}
