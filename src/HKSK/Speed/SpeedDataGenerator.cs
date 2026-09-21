using System.Text.RegularExpressions;
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

    /// <summary>Rebuilds one project's block, leaving every other project's as it is.</summary>
    /// <remarks>
    /// The project gets exactly the block <see cref="Generate(SkyrimCache, IReadOnlyDictionary{string, MovementType}, float)"/>
    /// would give it: a new creature is added at the end, an existing one is rebuilt where
    /// it stands, and one that no longer carries a sampler is taken out, since the game
    /// answers an absent project by passing the request through. A cache with no table
    /// gains one only when the project needs a block.
    /// </remarks>
    /// <exception cref="ArgumentException">The animation data lists no such project.</exception>
    /// <exception cref="InvalidOperationException">The project's Havok files cannot be found.</exception>
    public static Amendment Amend(
        SkyrimCache cache, string projectName,
        IReadOnlyDictionary<string, MovementType> movements,
        float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(movements);
        if (!(tolerance > 0f)) throw new ArgumentOutOfRangeException(nameof(tolerance));

        CacheProject project = Amendments.Open(cache, projectName);
        if (project is ActorProject && cache.FindProjectFile(project.Name) is null)
            throw new InvalidOperationException($"the Havok project file of '{project.Name}' cannot be found beside the cache");

        SpeedProjectBlock? block = BlockOf(cache, project, movements, tolerance, new Tally());
        if (block is null && cache.SpeedData is null) return Amendment.None;

        SpeedDataFile file = cache.SpeedData ??= new SpeedDataFile();

        // The listing and the block are parallel lists, so they move together.
        int at = file.Projects.FindIndex(p => string.Equals(SpeedDataFile.StemOf(p), project.Name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at < file.Blocks.Count)
        {
            if (block is null)
            {
                file.Projects.RemoveAt(at);
                file.Blocks.RemoveAt(at);
                return Amendment.Removed;
            }

            if (Bytes(file.Blocks[at]).AsSpan().SequenceEqual(Bytes(block))) return Amendment.Unchanged;

            file.Projects[at] = SpeedDataFile.ListingFor(project.Name);
            file.Blocks[at] = block;
            return Amendment.Replaced;
        }

        if (block is null) return Amendment.None;

        file.Projects.Add(SpeedDataFile.ListingFor(project.Name));
        file.Blocks.Add(block);
        return Amendment.Added;
    }

    private static byte[] Bytes(SpeedProjectBlock block) =>
        new SpeedDataFile { Projects = ["_"], Blocks = [block] }.Write();

    /// <summary>Rebuilds one project's block, with the movement types taken from the game's records.</summary>
    public static Amendment Amend(SkyrimCache cache, string projectName, Records.IGameRecords records, float tolerance = DefaultTolerance) =>
        Amend(cache, projectName, Records.GameRecordRules.MovementTypes(records), tolerance);

    /// <summary>Builds the whole table, with the movement types taken from the game's records.</summary>
    public static SpeedDataFile Generate(SkyrimCache cache, Records.IGameRecords records, float tolerance = DefaultTolerance) =>
        Generate(cache, Records.GameRecordRules.MovementTypes(records), tolerance);

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
        var tally = new Tally();

        foreach (CacheProject project in cache.OpenAll())
            if (BlockOf(cache, project, movements, tolerance, tally) is { } block)
            {
                file.Projects.Add(SpeedDataFile.ListingFor(project.Name));
                file.Blocks.Add(block);
            }

        return new Inferred(file, tally.Projects, tally.Blocks, tally.Unbuildable, tally.How);
    }

    /// <summary>What happened to each project and key while a table was built: for the tests, not the table.</summary>
    internal sealed class Tally
    {
        public int Projects, Blocks, Unbuildable;
        public Dictionary<(string, int), string> How { get; } = [];
    }

    /// <summary>
    /// One project's block, or null when the project has no place in the table.
    /// </summary>
    private static SpeedProjectBlock? BlockOf(
        SkyrimCache cache, CacheProject project, IReadOnlyDictionary<string, MovementType> movements,
        float tolerance, Tally tally)
    {
        if (project is not ActorProject actor) return null;

        string? path = cache.FindProjectFile(project.Name);
        if (path is null) return null;

        if (BehaviorRoot.Of(path) is not { } root) return null;

        ProjectWalk walk = ProjectWalk.Of(path);

        // The sampler is the only thing that reads the table. A project without
        // one -- the eight flyers and hoverers -- is left out, and the game
        // answers a request for it as it answers any absent project: unchanged.
        if (Ladders.ParameterOf(walk) is not { } parameter) return null;

        tally.Projects++;

        var block = new SpeedProjectBlock();

        // The project is read once and evaluated many times -- one run per state
        // per heading -- so the walk and the character properties are hoisted.
        hkbBehaviorGraph? graph = null;
        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBehaviorGraph found) { graph = found; break; }

        if (graph is null) return block;
        Properties properties = Properties.OfProject(path);

        var constants = StateConstants.Of(walk, root);
        var variables = new ProjectVariables(walk.Steps);
        List<StateAssignment> expressions = [.. StateExpressions.In(walk)];
        var placed = StateExpressions.WithNodes(walk).ToList();

        var built = LocomotionStates.In(walk, variables)
            .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
            .Where(st => st.Arms.Count > 0)
            .ToList();

        IReadOnlyList<StateKeys.Writer> writers = StateKeys.Writers(walk, root);

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

            // A tag or a manager row places the key in a subtree with no ladder of
            // its own. Drive the graph into that subtree by the events that enter
            // it and read which sampler-fed ladder is live beside it: the player's
            // attack states are layered over the locomotion that keeps reading the
            // sampler while the attack plays.
            if (arms is null && TaggedAt(graph, walk, properties, actor, built, parameter, key, writers) is { } beside)
            { arms = beside; route = "tagged"; }

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
                else if (writtenBy.All(by => by is StateKeys.By.Tag or StateKeys.By.Manager)) { tally.How[(project.Name, key)] = "unread"; continue; }
                else if (StateAt(graph, walk, properties, actor, built, parameter, key) is { } run) { arms = run; route = "evaluated"; }
            }

            // Nothing in this graph reads a speed into a blend, so the creature
            // has no curve: whatever it plays, it plays at one speed.
            if (built.Count == 0 && arms is null && Standing(walk, properties, actor) is { } standing) { arms = standing; route = "standing"; }

            if (arms is null) { tally.Unbuildable++; tally.How[(project.Name, key)] = "unbuildable"; continue; }
            tally.How[(project.Name, key)] = route;

            // A creature that cannot move by root motion has a curve, and it is
            // zero. The grid it is written on is degenerate because the ladder
            // gives no extent, and the shipped one spans 0 to 324.5 like 74 of
            // the 86 blocks do -- where that extent comes from is not derived
            // here, and the curve is zero at every x either way.
            bool still = arms.All(a => a.Ladder.Rungs.All(r => r.Travel.Length() <= 0f));

            float top = arms.Max(a => a.Ladder.Rungs.Count == 0 ? 0f : a.Ladder.Rungs[^1].Weight);
            if (top <= 0f && !still) { tally.Unbuildable++; continue; }
            if (arms.Any(a => a.Ladder.Rungs.Count == 0)) { tally.Unbuildable++; continue; }

            var entry = new SpeedEntry { Key = (uint)key };
            block.Entries.Add(entry);
            tally.Blocks++;

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

        return block;
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
    /// The ladder live beside a tagged subtree, found by driving the graph into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>BSiStateTaggingGenerator</c> or a <c>BSIStateManagerModifier</c> row says
    /// exactly which state holds the key, and <see cref="LocomotionStates"/> finds the
    /// ladder under it when there is one. When there is none, the sampler's answer is
    /// still read -- by whatever ladder is live in parallel, since Bethesda layers an
    /// attack, a block or a spell over the locomotion that keeps the feet moving. So
    /// the graph is put in that state, by raising the events of every transition into
    /// it and into each state above it, together with the locomotion events, and the
    /// live ladders are read off the result.
    /// </para>
    /// <para>
    /// The reading counts only when the writer itself came out live: the tagging
    /// generator among the active generators, the row's state's generator, or the
    /// generator an expression modifier hangs off. A run that settled elsewhere says
    /// nothing about this key, and the key falls to the readings that do not need
    /// the graph run.
    /// </para>
    /// </remarks>
    internal static List<(float Direction, SpeedLadder Ladder)>? TaggedAt(
        hkbBehaviorGraph graph, ProjectWalk walk, Properties properties, ActorProject actor,
        List<(LocomotionState State, List<(float Direction, SpeedLadder Ladder)> Arms)> built,
        string parameter, int key, IReadOnlyList<StateKeys.Writer> writers)
    {
        Dictionary<string, IList<string>>? eventNames = null;

        foreach (StateKeys.Writer writer in writers)
        {
            if (writer.Key != key || writer.Node is null) continue;
            // An expression's key is placed by where the expression sits (Pairing),
            // which is exact; driving the graph there is measured worse.
            if (writer.By is StateKeys.By.Expression) continue;

            // What has to come out active for the run to count: the tag, the row's
            // state's generator, or the generator an expression modifier hangs off.
            IHavokObject? live = writer.Node switch
            {
                hkbStateMachineStateInfo state => state.m_generator,
                hkbGenerator generator => generator,
                _ => GeneratorAbove(walk, writer.Node),
            };
            if (live is null) continue;

            eventNames ??= EventNamesByFile(walk);
            (IReadOnlyList<string> entry, var pins) = WayInto(walk, writer.Node, eventNames);
            Events events = Events.Of([.. Moving.Names, .. entry]);

            Evaluation run = ActiveGenerators.Evaluate(graph, walk, tables =>
            {
                foreach (Variables variables in tables.Values)
                {
                    variables.Set("iSyncIdleLocomotion", 1);
                    variables.Set("iSyncForwardState", 0);
                    variables.Set("iSyncTurnState", 1);
                    variables.Set("Direction", 0f);
                    variables.Set("TurnDelta", 0f);
                    variables.Set("TurnDeltaDamped", 0f);
                    variables.Set("Speed", 100f);
                }

                // A machine chooses its state by a variable: say it. A boolean cannot
                // hold a state id above 1, so such a pin is left to the event instead.
                foreach ((string file, string variable, int state) in pins)
                    if (tables.TryGetValue(file, out Variables? table) && table.IndexOf(variable) is var index && index >= 0
                        && !(state > 1 && table.TypeOf(index) == VariableType.VARIABLE_TYPE_BOOL))
                        table.Set(variable, state);
            // The state is read as it passes, before its clips end: an attack's own clip
            // raises attackStop, and read at rest the graph has already left.
            }, properties, events, finishClips: false);

            if (!run.Active.Any(n => ReferenceEquals(n.Generator, live))) continue;

            var ladders = Ladders.ActiveIn(run, walk, parameter);

            // Landed, and nothing sampler-fed live beside it: the state plays what
            // it plays at one speed -- the horse's sprint is a single clip -- and
            // the curve is flat at what its clips deliver.
            if (ladders.Count == 0)
            {
                if (live is hkbGenerator under && Flat(walk, properties, actor, under) is { } flat) return flat;
                continue;
            }

            foreach ((hkbBlenderGenerator blend, float _, float _) in ladders)
                foreach ((LocomotionState state, var arms) in built)
                    foreach (SpeedConsumer consumer in state.Blends)
                        if (ReferenceEquals(consumer.Node, blend))
                            return arms;

            if (ArmsAround(ladders[0].Blend, run.Walk ?? walk, actor) is { } around) return around;
        }

        return null;
    }

    private static hkbGenerator? GeneratorAbove(ProjectWalk walk, IHavokObject node)
    {
        for (IHavokObject? at = walk.StepOf(node)?.Parent; at is not null; at = walk.StepOf(at)?.Parent)
            if (at is hkbGenerator generator) return generator;

        return null;
    }

    // How to put the graph in the state holding the node: one event for each state
    // on the way up that is neither its machine's start nor chosen by a variable,
    // and the variable pinned to the state where one chooses -- a sync variable, a
    // bound startStateId (the bleedout's i1stPerson, the weapon selection's
    // iRightHandType, 1HM_Behavior's iWantBlock), or a manual selector's bound index.
    //
    // Raising every transition's event at once sends the graph anywhere -- the
    // player's root takes CartExit and GetUpExit as readily as attackStart -- so one
    // event is chosen per level: the one that also enters the most other states on
    // the way, since Bethesda routes attackStart into the attack state and into the
    // right-hand attack below it, then the shortest name. Self-transitions and
    // streaming queries enter nothing.
    internal static (IReadOnlyList<string> Events, IReadOnlyList<(string File, string Variable, int State)> Pins) WayInto(
        ProjectWalk walk, IHavokObject node, Dictionary<string, IList<string>> eventNames)
    {
        var levels = new List<(bool Needed, List<string> Events, List<(string Event, string Condition)> Conditions, string File)>();
        var pins = new List<(string, string, int)>();
        ProjectVariables? variables = null;

        for (IHavokObject? at = node; at is not null;)
        {
            if (walk.StepOf(at) is not { } step) break;

            if (at is hkbStateMachineStateInfo state && step.Parent is hkbStateMachine machine
                && eventNames.TryGetValue(step.File, out IList<string>? names))
            {
                var here = new List<string>();
                var conditions = new List<(string, string)>();
                void Add(hkbStateMachineTransitionInfo transition)
                {
                    int id = transition.m_eventId;
                    if (id < 0 || id >= names.Count) return;
                    string name = names[id];
                    if (name.StartsWith("selfTrans", StringComparison.Ordinal)) return;
                    if (name.StartsWith("streamingQuery", StringComparison.Ordinal)) return;
                    if (!here.Contains(name)) here.Add(name);
                    if (transition.m_condition is hkbExpressionCondition { m_expression: { } text })
                        conditions.Add((name, text));
                }

                foreach (hkbStateMachineTransitionInfo transition in machine.m_wildcardTransitions?.m_transitions ?? [])
                    if (transition.m_toStateId == state.m_stateId) Add(transition);

                foreach (hkbStateMachineStateInfo other in machine.m_states ?? [])
                    foreach (hkbStateMachineTransitionInfo transition in other.m_transitions?.m_transitions ?? [])
                        if (transition.m_toStateId == state.m_stateId) Add(transition);

                variables ??= new ProjectVariables(walk.Steps);
                bool picked = false;
                if ((StartStateMode)machine.m_startStateMode == StartStateMode.START_STATE_MODE_SYNC
                    && machine.m_syncVariableIndex >= 0
                    && variables.NameOf(step.File, machine.m_syncVariableIndex) is { } sync)
                { pins.Add((step.File, sync, state.m_stateId)); picked = true; }

                // A bound start state is a chooser. Where it can choose the state --
                // no event enters it, or the id is a flag's 0 or 1: the bleedout's
                // first- or third-person branch, the weapon selection, the block state
                // on iWantBlock -- it is pinned to the state. Where an event enters a
                // state it cannot name, the chooser is pinned to the machine's own
                // start and the event does the entering: pinned to AttackState,
                // iWantBlock started 1HM_Behavior inside it and the locomotion events
                // walked it out again, and left alone it starts the machine blocking.
                int bound = Bindings.VariableFor(machine, "startStateId");
                if (bound >= 0 && variables.NameOf(step.File, bound) is { } start)
                {
                    if (here.Count == 0 || state.m_stateId <= 1)
                    { pins.Add((step.File, start, state.m_stateId)); picked = true; }
                    else
                        pins.Add((step.File, start, machine.m_startStateId));
                }

                levels.Add((!picked && machine.m_startStateId != state.m_stateId, here, conditions, step.File));
            }

            if (step.Parent is hkbManualSelectorGenerator selector && step.Member == "generators")
            {
                int bound = Bindings.VariableFor(selector, "selectedGeneratorIndex");
                if (bound >= 0 && (variables ??= new ProjectVariables(walk.Steps)).NameOf(step.File, bound) is { } index)
                    pins.Add((step.File, index, step.Index));
            }

            at = step.Parent;
        }

        var chosen = new List<string>();
        var asked = new List<(string, string, int)>();
        foreach ((bool needed, List<string> here, var conditions, string file) in levels)
        {
            if (!needed || here.Count == 0) continue;

            // A return -- pairedStop, PairEnd, MountedSwimStop -- enters the resting
            // state of many machines and so is shared by many levels, which is the
            // opposite of what sharing is meant to find. It is taken only when
            // nothing else enters the state.
            List<string> entering = here.Where(e => !Returns.IsMatch(e)).ToList();
            if (entering.Count == 0) entering = here;

            string best = entering
                .OrderByDescending(e => levels.Count(l => l.Events.Contains(e)))
                .ThenBy(e => e.Length)
                .First();
            if (!chosen.Contains(best)) chosen.Add(best);

            // The transition asks for a situation -- a bow in the right hand, no
            // spell readied -- and the situation is set up for it: each conjunct of
            // the form `name == k`, `name >= k` or `name > k` pins the name.
            foreach ((string _, string condition) in conditions.Where(c => c.Event == best))
                foreach ((string name, int value) in Asked(condition))
                    asked.Add((file, name, value));
        }

        // What a transition asks for comes first, so that a chooser on the chain --
        // the weapon selection's own type -- says the last word.
        return (chosen, [.. asked.Distinct(), .. pins]);
    }

    private static readonly Regex Returns = new(@"(Stop|End|Exit|Out)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Conjunct = new(@"^\(?\s*(?<name>[A-Za-z_]\w*)\s*(?<op>==|>=|>)\s*(?<value>-?\d+)\s*\)?$", RegexOptions.Compiled);

    // The values a condition's conjuncts ask of named variables.
    private static IEnumerable<(string Name, int Value)> Asked(string condition)
    {
        foreach (string part in condition.Split("&&"))
        {
            Match match = Conjunct.Match(part.Trim());
            if (!match.Success || !int.TryParse(match.Groups["value"].Value, out int value)) continue;
            yield return (match.Groups["name"].Value, match.Groups["op"].Value == ">" ? value + 1 : value);
        }
    }

    private static Dictionary<string, IList<string>> EventNamesByFile(ProjectWalk walk)
    {
        var names = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectStep step in walk.Steps)
            if (step.Node is hkbBehaviorGraph graph && graph.m_data?.m_stringData?.m_eventNames is { } list)
                names.TryAdd(step.File, list);

        return names;
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
