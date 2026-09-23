using HKSK.Assembly;
using HKSK.Behavior;
using HKSK.Engine;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// A creature assembled from nothing but animations and a skeleton: the three
/// packfiles a race names, and a graph the engine can be driven through.
/// </summary>
public sealed class CreatureAssemblerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hksk-assemble-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A packfile with nothing in it, which is all the assembler copies.</summary>
    private string Placeholder(string name)
    {
        string path = Path.Combine(_folder, "in", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        HavokFile.Of(new hkRootLevelContainer { m_namedVariants = [] }).Save(path);
        return path;
    }

    private AssemblyResult Assemble(params RoledAnimation[] animations) =>
        CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"), animations),
            Path.Combine(_folder, "out"));

    private RoledAnimation Animation(string name, params AnimationRole[] roles) =>
        new(Placeholder(name + ".hkx"), roles);

    private AssemblyResult Walker() => Assemble(
        Animation("Idle", new AnimationRole(RoleKind.Idle)),
        Animation("Walk", new AnimationRole(RoleKind.Walk, Heading.Forward)),
        Animation("Run", new AnimationRole(RoleKind.Run, Heading.Forward)));

    [Fact]
    public void TheThreePackfilesAreWrittenAndNameEachOther()
    {
        AssemblyResult made = Walker();

        Assert.True(File.Exists(made.ProjectPath));
        ProjectFile project = ProjectFile.Load(made.ProjectPath);
        string character = Assert.Single(project.CharacterFiles);
        Assert.Equal(@"characters\Bonewalker.hkx", character, ignoreCase: true);

        CharacterFile read = CharacterFile.Load(
            Path.Combine(Path.GetDirectoryName(made.ProjectPath)!, character.Replace('\\', Path.DirectorySeparatorChar)));

        Assert.Equal("Bonewalker", read.Name);
        Assert.Equal(@"behaviors\BonewalkerBehavior.hkx", read.BehaviorFilename, ignoreCase: true);
        Assert.Equal(@"character assets\skeleton.hkx", read.RigName, ignoreCase: true);

        // No ragdoll was given, so the skeleton answers for both, and the note says so.
        Assert.Equal(read.RigName, read.RagdollName);
        Assert.Contains(made.Notes, n => n.Contains("no ragdoll", StringComparison.Ordinal));
    }

    /// <summary>
    /// The animation list is the cache's numbering, so it is in the order the caller
    /// gave and in no other.
    /// </summary>
    [Fact]
    public void TheAnimationsAreNumberedInTheOrderTheyWereGiven()
    {
        AssemblyResult made = Walker();
        CharacterFile character = CharacterFile.Load(
            Path.Combine(Path.GetDirectoryName(made.ProjectPath)!, "characters", "Bonewalker.hkx"));

        // The ones given come first and in order, because that order is their numbering.
        // The two after them are the turn nobody animated, made from the idle.
        Assert.Equal(
            [@"animations\Idle.hkx", @"animations\Walk.hkx", @"animations\Run.hkx",
             @"animations\TurnLeft.hkx", @"animations\TurnRight.hkx"],
            character.AnimationNames, StringComparer.OrdinalIgnoreCase);

        foreach (string animation in character.AnimationNames)
            Assert.True(File.Exists(Path.Combine(
                Path.GetDirectoryName(made.ProjectPath)!, animation.Replace('\\', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// The graph declares the names the engine speaks through. Five of them are
    /// declared by all 46 of the game's creatures, and a name the engine writes that
    /// the graph has not declared is a value the graph never sees.
    /// </summary>
    [Fact]
    public void TheGraphDeclaresWhatTheEngineWritesAndReads()
    {
        AssemblyResult made = Walker();
        var editor = new GraphEditor(HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx")));

        foreach (string name in new[] { "bAnimationDriven", "iState", "IsAttackReady", "iSyncIdleLocomotion", "Direction" })
            Assert.True(editor.VariableIndex(name) >= 0, $"{name} should be declared");

        Assert.True(editor.VariableIndex("iState_Default") >= 0, "the movement type's constant should be declared");
        Assert.True(editor.EventIndex("moveStart") >= 0);
        Assert.True(editor.EventIndex("DeathAnimation") >= 0);
    }

    /// <summary>
    /// And the thing that matters: the engine, driven through the graph, reaches the
    /// idle standing still and the locomotion once it is told to move.
    /// </summary>
    /// <remarks>
    /// Which rung of the ladder it lands on is not asserted here, and cannot be yet: the
    /// rungs blend on what the speed sampler writes, and the sampler reads the request
    /// through the creature's speed table, which the assembler does not write until the
    /// cache's third file is built. Until then it writes nothing and the ladder sits on
    /// its floor, which is the right answer to the question actually being asked -- does
    /// the graph move at all.
    /// </remarks>
    [Fact]
    public void TheEngineReachesTheIdleAtRestAndTheLocomotionOnMoveStart()
    {
        AssemblyResult made = Walker();

        string[] Clips(Events events) =>
            [.. ActiveGenerators
                .Of(made.ProjectPath, tables => { foreach (Variables v in tables.Values) v.Set("Speed", 100f); }, events)
                .Active.Where(a => a.Generator is hkbClipGenerator)
                .Select(a => a.Name)];

        Assert.Equal(["Idle"], Clips(Events.None));

        string[] moving = Clips(Events.Of("moveStart"));
        Assert.DoesNotContain("Idle", moving);
        Assert.All(moving, name => Assert.Contains("Forward", name, StringComparison.Ordinal));
    }

    /// <summary>
    /// The cache row restates the graph, and holds the one thing that is nowhere else:
    /// where each animation carries the creature.
    /// </summary>
    [Fact]
    public void TheCacheRowNumbersTheClipsAndHoldsTheMotion()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec(
                "Bonewalker",
                Placeholder("skeleton.hkx"),
                [
                    new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                    new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 90f),
                    new RoledAnimation(Placeholder("Back.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Back)], Speed: 40f),
                ],
                ClipDurations: new Dictionary<string, float> { ["Walk"] = 1f, ["Back"] = 2f }),
            Path.Combine(_folder, "out"));

        Assert.Equal("BonewalkerProject.txt", made.Cache.Name);
        Assert.True(made.Cache.Block.HasAnimationCache);
        Assert.Contains(@"behaviors\BonewalkerBehavior.hkx", made.Cache.Block.Files, StringComparer.OrdinalIgnoreCase);

        // Every clip the graph plays is restated against the position of its animation.
        var byName = made.Cache.Block.Clips.ToDictionary(c => c.Name, c => c.CacheIndex);
        Assert.Equal(0, byName["Idle"]);
        Assert.Equal(1, byName["WalkForward"]);

        // The walk goes 90 units in its one second, forward, which is +Y.
        HKSK.Cache.ClipMovement walk = made.Cache.Movements!.Movements.Single(m => m.CacheIndex == 1);
        Assert.Equal(90f, walk.Travel, 1);
        Assert.True(walk.Translations[^1].Value.Y > 89f, "forward is +Y");

        // The back walk goes the other way, and 40 a second for two seconds is 80.
        HKSK.Cache.ClipMovement back = made.Cache.Movements.Movements.Single(m => m.CacheIndex == 2);
        Assert.Equal(80f, back.Travel, 1);
        Assert.True(back.Translations[^1].Value.Y < -79f, "back is -Y");

        // The idle was given no speed, so it carries the creature nowhere and has no block.
        Assert.DoesNotContain(made.Cache.Movements.Movements, m => m.CacheIndex == 0);
        Assert.Contains(made.Notes, n => n.Contains("the motion they are to carry", StringComparison.Ordinal));
    }

    /// <summary>
    /// A speed ladder blends on a variable that nothing but the speed sampler writes,
    /// so a graph without one never leaves its first rung.
    /// </summary>
    [Fact]
    public void TheSpeedSamplerIsWiredToTheFourThingsItReadsAndWrites()
    {
        AssemblyResult made = Walker();
        var file = HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx"));

        var editor = new GraphEditor(file);
        BSSpeedSamplerModifier sampler = Assert.Single(file.All<BSSpeedSamplerModifier>());

        var bound = sampler.m_variableBindingSet!.m_bindings
            .ToDictionary(b => b.m_memberPath, b => editor.Strings.m_variableNames[b.m_variableIndex]);

        Assert.Equal("iState", bound["state"]);
        Assert.Equal("Direction", bound["direction"]);
        Assert.Equal("Speed", bound["goalSpeed"]);
        Assert.Equal("SpeedSampled", bound["speedOut"]);

        // A field a node also binds is a slot, not a value, and the shipped ones say -1.
        Assert.Equal(-1, sampler.m_state);
    }

    /// <summary>
    /// A creature that walks in four directions gets a compass of ladders, which is how
    /// the game's bipeds are built.
    /// </summary>
    [Fact]
    public void FourHeadingsMakeACompassOfLadders()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)]),
                new RoledAnimation(Placeholder("Run.hkx"), [new AnimationRole(RoleKind.Run, Heading.Forward)]),
                new RoledAnimation(Placeholder("Back.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Back)]),
                new RoledAnimation(Placeholder("Left.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Left)]),
                new RoledAnimation(Placeholder("Right.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Right)]),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Equal(LocomotionPlan.FourArm, made.Plan.Locomotion);

        var file = HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx"));
        var editor = new GraphEditor(file);

        hkbBlenderGenerator compass = editor.Require<hkbBlenderGenerator>("BonewalkerDirectionBlend");
        Assert.Equal(4, compass.m_children.Count);

        // Forward at nothing, round through the right, the back and the left.
        Assert.Equal([0f, 0.2f, 0.5f, 0.8f], compass.m_children.Select(c => c.m_weight));
        Assert.Equal("Direction",
            editor.Strings.m_variableNames[compass.m_variableBindingSet!.m_bindings.Single().m_variableIndex]);

        // The forward arm is a ladder of its two gaits; the others are a clip each.
        Assert.IsType<hkbBlenderGenerator>(compass.m_children[0].m_generator);
        Assert.IsType<hkbClipGenerator>(compass.m_children[1].m_generator);
    }

    /// <summary>
    /// The movement type is authored from the clips and not the other way round: a
    /// heading's walk speed is that heading's walk clip's, and the ladder's rungs are
    /// the record, written down.
    /// </summary>
    [Fact]
    public void TheMovementTypeIsTheSpeedsTheClipsDeliver()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 100f),
                new RoledAnimation(Placeholder("Run.hkx"), [new AnimationRole(RoleKind.Run, Heading.Forward)], Speed: 175f),
                new RoledAnimation(Placeholder("Back.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Back)], Speed: 40f),
            ],
            MovementTypeName: "BonewalkerDefault"),
            Path.Combine(_folder, "out"));

        HKSK.Speed.MovementType movement = made.Movement;
        Assert.Equal("BonewalkerDefault", movement.Name);
        Assert.Equal(100f, movement.ForwardWalk);
        Assert.Equal(175f, movement.ForwardRun);

        // The back walk is its own; its run has no clip, so it walks at the speed it walks.
        Assert.Equal(40f, movement.BackWalk);
        Assert.Equal(40f, movement.BackRun);

        // Nothing steps sideways, so the sides take the forward speeds.
        Assert.Equal(100f, movement.LeftWalk);
        Assert.Equal(175f, movement.RightRun);
        Assert.False(movement.OneGait, "it walks at 100 and runs at 175");
    }

    /// <summary>
    /// A creature's packfiles are not enough on their own: the game finds a project by
    /// name in the animation cache and takes the first behaviour in its file list, so a
    /// creature whose loose files are perfect and whose row is missing reports that its
    /// root behaviour cannot be found.
    /// </summary>
    [Fact]
    public void InstallingPutsTheCreatureInTheCacheTheGameReadsItFrom()
    {
        string meshes = Path.Combine(_folder, "Meshes");
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 100f),
            ],
            MovementTypeName: "BonewalkerDefault",
            ClipDurations: new Dictionary<string, float> { ["Walk"] = 1f }),
            Path.Combine(meshes, "actors", "bonewalker"));

        InstallReport report = CreatureInstaller.Install(made, meshes);

        Assert.True(report.Added);
        Assert.True(File.Exists(Path.Combine(meshes, "animationdatasinglefile.txt")));

        // And the cache opens it as an actor, which is what the game does with it.
        SkyrimCache cache = SkyrimCache.Load(meshes);
        Assert.Contains("BonewalkerProject", cache.ProjectNames);

        ActorProject actor = Assert.IsType<ActorProject>(cache.OpenActor("BonewalkerProject"));

        // The two given, and the turn each way that was made from the idle.
        Assert.Equal(4, actor.Animations.Count);
        Assert.Contains(actor.Clips, c => c.Name == "Idle");
        Assert.Contains(actor.Clips, c => c.Name == "WalkForward");

        // The walk carries the creature 100 units in its second, which is in no packfile.
        Assert.Equal(100f, actor.Animations[1].Motion!.Travel, 1);
        Assert.Contains(report.Notes, n => n.Contains("was added to the cache", StringComparison.Ordinal));

        // The speed table is the last thing to be built and the only one that is
        // measured: the creature's own ladder sampled at every speed the engine may ask
        // for. Until it exists the sampler has nothing to look a request up in.
        Assert.True(report.SpeedTable, string.Join("; ", report.Notes));
        Assert.NotNull(cache.SpeedData?.Block("BonewalkerProject"));
    }

    /// <summary>A creature that fights, flinches, stumbles and dies.</summary>
    private AssemblyResult Fighter() => CreatureAssembler.Assemble(
        new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
        [
            new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
            new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)]),
            new RoledAnimation(Placeholder("Swing.hkx"), [new AnimationRole(RoleKind.Attack, Name: "attackStart_Attack1")]),
            new RoledAnimation(Placeholder("Heavy.hkx"), [new AnimationRole(RoleKind.PowerAttack, Name: "attackStart_ForwardPower")]),
            new RoledAnimation(Placeholder("Hit.hkx"), [new AnimationRole(RoleKind.Recoil)]),
            new RoledAnimation(Placeholder("Stumble.hkx"), [new AnimationRole(RoleKind.Stagger)]),
            new RoledAnimation(Placeholder("Die.hkx"), [new AnimationRole(RoleKind.Death)]),
        ]),
        Path.Combine(_folder, "out"));

    /// <summary>
    /// The engine sends an attack by the name the race's attack data gives it, and the
    /// graph is what answers. Each situation is left when its own clip says so.
    /// </summary>
    [Fact]
    public void TheEngineReachesEachSituationByTheEventThatStartsIt()
    {
        AssemblyResult made = Fighter();

        string[] Clips(params string[] events) =>
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of(events))
                .Active.Where(a => a.Generator is hkbClipGenerator)
                .Select(a => a.Name)];

        Assert.Contains("Attack_Attack1", Clips("attackStart_Attack1"));
        Assert.Contains("Attack_ForwardPower", Clips("attackStart_ForwardPower"));
        Assert.Contains("Recoil", Clips("recoilStart"));
        Assert.Contains("Stagger", Clips("staggerStart"));
        Assert.Contains("Death", Clips("DeathAnimation"));

        // And none of them is where the creature stands when nothing has happened.
        Assert.Equal(["Idle"], Clips());
    }

    /// <summary>
    /// A one-shot clip raises its own ending, so a creature is never left mid-attack
    /// waiting to be told to stop, and the death clip hands it to its ragdoll.
    /// </summary>
    [Fact]
    public void EachSituationsClipRaisesTheEventThatEndsIt()
    {
        AssemblyResult made = Fighter();
        var editor = new GraphEditor(HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx")));

        string Raised(string clip) =>
            editor.EventName(editor.Require<hkbClipGenerator>(clip).m_triggers!.m_triggers[0].m_event.m_id)!;

        Assert.Equal("attackStop", Raised("Attack_Attack1"));
        Assert.Equal("recoilStop", Raised("Recoil"));
        Assert.Equal("staggerStop", Raised("Stagger"));
        Assert.Equal("Ragdoll", Raised("Death"));

        // Played once, never looped: a creature that loops its death never finishes it.
        foreach (string once in new[] { "Attack_Attack1", "Recoil", "Stagger", "Death" })
            Assert.Equal((sbyte)ClipMode.SinglePlay, editor.Require<hkbClipGenerator>(once).m_mode);
    }

    /// <summary>
    /// Without an attack block the engine does not know which attacks the project can
    /// play, whatever its graph says. The block is derived from the states the graph
    /// has, entered by the names the race's attack data gives them.
    /// </summary>
    [Fact]
    public void TheSetDataNamesTheAttacksTheGraphCanPlay()
    {
        string meshes = Path.Combine(_folder, "Meshes");
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 100f),
                new RoledAnimation(Placeholder("Swing.hkx"), [new AnimationRole(RoleKind.Attack, Name: "attackStart_Attack1")]),
                new RoledAnimation(Placeholder("Heavy.hkx"), [new AnimationRole(RoleKind.PowerAttack, Name: "attackStart_ForwardPower")]),
            ],
            ClipDurations: new Dictionary<string, float> { ["Walk"] = 1f }),
            Path.Combine(meshes, "actors", "bonewalker"));

        InstallReport report = CreatureInstaller.Install(made, meshes);
        Assert.True(report.SetData, string.Join("; ", report.Notes));

        SkyrimCache cache = SkyrimCache.Load(meshes);
        var sets = cache.SetData.Project("BonewalkerProject");
        Assert.NotNull(sets);

        var named = sets.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Select(a => a.EventName).ToList();
        Assert.Contains("attackStart_Attack1", named);
        Assert.Contains("attackStart_ForwardPower", named);
    }

    /// <summary>
    /// A knocked-down creature lies in whatever pose its ragdoll settled in, so which
    /// get-up plays is not a choice a variable can make: a pose matcher compares the
    /// ragdoll against each clip's first frame and plays the nearest.
    /// </summary>
    [Fact]
    public void GettingUpMatchesThePoseTheCreatureIsLyingIn()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)]),
                new RoledAnimation(Placeholder("Up1.hkx"), [new AnimationRole(RoleKind.GetUp)]),
                new RoledAnimation(Placeholder("Up2.hkx"), [new AnimationRole(RoleKind.GetUp)]),
                new RoledAnimation(Placeholder("Raise.hkx"), [new AnimationRole(RoleKind.Reanimate)]),
            ],
            PoseMatchBones: (3, 4)),
            Path.Combine(_folder, "out"));

        var file = HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx"));
        var editor = new GraphEditor(file);

        hkbPoseMatchingGenerator up = editor.Require<hkbPoseMatchingGenerator>("GetUp");
        Assert.Equal(2, up.m_children.Count);

        // It starts playing when told to get up and starts matching on the event that
        // put the creature on the floor.
        Assert.Equal("GetUpStart", editor.EventName(up.m_startPlayingEventId));
        Assert.Equal("Ragdoll", editor.EventName(up.m_startMatchingEventId));

        // The root and the pelvis are bone 0 in all 97 of the shipped ones; the other
        // two are a choice, and the caller's choice is kept.
        Assert.Equal(0, up.m_rootBoneIndex);
        Assert.Equal(0, up.m_pelvisIndex);
        Assert.Equal(3, up.m_otherBoneIndex);
        Assert.Equal(4, up.m_anotherBoneIndex);

        // Two ways up, and the engine says which by iGetUpType.
        var selector = editor.Require<hkbManualSelectorGenerator>("GetUpSelector");
        Assert.Equal(2, selector.m_generators.Count);
        Assert.Equal("iGetUpType",
            editor.Strings.m_variableNames[selector.m_variableBindingSet!.m_bindings.Single().m_variableIndex]);

        // A creature that gets up has to be given back to the engine to walk about with.
        var raised = editor.Require<hkbClipGenerator>("GetUp1").m_triggers!.m_triggers
            .Select(t => editor.EventName(t.m_event.m_id)).ToList();
        Assert.Contains("GetUpEnd", raised);
        Assert.Contains("AddCharacterControllerToWorld", raised);
    }

    /// <summary>
    /// A creature nobody animated a turn for gets one: the idle in a slot of its own
    /// with a turn on it, since root motion belongs to a slot and not to an animation.
    /// </summary>
    [Fact]
    public void ACreatureWithNoTurnClipIsGivenOneEachWay()
    {
        string meshes = Path.Combine(_folder, "Meshes");
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 100f),
            ],
            ClipDurations: new Dictionary<string, float> { ["Walk"] = 1f },
            TurnRate: 90f),
            Path.Combine(meshes, "actors", "bonewalker"));

        Assert.Contains(made.Plan.Synthesised, t => t.Contains("turn in place", StringComparison.Ordinal));
        Assert.Contains(made.Notes, n => n.Contains("90 degrees a second", StringComparison.Ordinal));

        // The engine asks for a turn by name and gets one.
        string[] Clips(params string[] events) =>
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of(events))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];

        Assert.Contains("TurnLeft", Clips("turnLeft"));
        Assert.Contains("TurnRight", Clips("turnRight"));

        // Each turn is the idle's animation in a slot of its own, going nowhere and
        // turning, one way each.
        CreatureInstaller.Install(made, meshes);
        ActorProject actor = SkyrimCache.Load(meshes).OpenActor("BonewalkerProject")!;

        HKSK.Cache.ClipMovement left = actor.Animations.Single(a => a.StoredName.Contains("TurnLeft")).Motion!;
        HKSK.Cache.ClipMovement right = actor.Animations.Single(a => a.StoredName.Contains("TurnRight")).Motion!;

        Assert.Equal(0f, left.Travel, 3);
        Assert.Equal(90f, left.Turn * 180f / MathF.PI, 1);
        Assert.Equal(left.Turn, right.Turn, 3);
        Assert.True(left.Rotations[^1].Value.Z * right.Rotations[^1].Value.Z < 0, "they turn opposite ways");
    }

    /// <summary>
    /// A stance is the standing and moving parts over again with the weapon out, and a
    /// creature is let into it when the engine says the weapon is drawn.
    /// </summary>
    [Fact]
    public void ACombatStanceIsTheWholeCreatureOverAgainWithItsWeaponOut()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)]),
                new RoledAnimation(Placeholder("Ready.hkx"), [new AnimationRole(RoleKind.CombatIdle, Stance: Stance.Combat)]),
                new RoledAnimation(Placeholder("WalkArmed.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward, Stance: Stance.Combat)]),
                new RoledAnimation(Placeholder("Draw.hkx"), [new AnimationRole(RoleKind.Equip)]),
                new RoledAnimation(Placeholder("Sheathe.hkx"), [new AnimationRole(RoleKind.Unequip)]),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Contains(Module.CombatStance, made.Plan.Modules);
        Assert.Contains(Module.EquipTransitions, made.Plan.Modules);

        string[] Clips(params string[] events) =>
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of(events))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];

        // At ease it stands as itself; ready, it stands as the armed one.
        Assert.Equal(["Idle"], Clips());
        Assert.Equal(["CombatIdle"], Clips("combatStanceStart"));

        // And moving while ready plays the armed walk, not the other one.
        Assert.Contains("CombatWalkForward", Clips("combatStanceStart", "moveStart"));

        // Drawing and putting away are one-shots that say when they are done, which is
        // what the engine waits for before it lets the creature attack.
        Assert.Contains("Equip", Clips("weaponDraw"));
        Assert.Contains("Unequip", Clips("weaponSheathe"));

        var editor = new GraphEditor(HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx")));
        Assert.Equal("weapEquipOut",
            editor.EventName(editor.Require<hkbClipGenerator>("Equip").m_triggers!.m_triggers[0].m_event.m_id));
    }

    /// <summary>
    /// A canned turn is a manoeuvre the AI asks for by name -- turn ninety left, turn
    /// about -- rather than a rate it asks for. One clip with a mirror serves both sides.
    /// </summary>
    [Fact]
    public void OneCannedTurnClipCanServeBothSides()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)]),
                new RoledAnimation(Placeholder("Turn90.hkx"),
                    [new AnimationRole(RoleKind.CannedTurn, Side: Side.Left, Angle: 90, Mirror: true)]),
                new RoledAnimation(Placeholder("About.hkx"),
                    [new AnimationRole(RoleKind.CannedTurn, Side: Side.Left, Angle: 180)]),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Contains(Module.CannedTurn, made.Plan.Modules);

        string[] Clips(params string[] events) =>
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of(events))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];

        Assert.Contains("CannedTurnLeft90", Clips("cannedTurnLeft90"));
        Assert.Contains("CannedTurnRight90", Clips("cannedTurnRight90"));
        Assert.Contains("CannedTurnLeft180", Clips("cannedTurnLeft180"));

        // The right turn is the left one played mirrored, which is one clip serving two
        // manoeuvres rather than two animations.
        var editor = new GraphEditor(HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonewalkerBehavior.hkx")));

        hkbClipGenerator left = editor.Require<hkbClipGenerator>("CannedTurnLeft90");
        hkbClipGenerator right = editor.Require<hkbClipGenerator>("CannedTurnRight90");
        Assert.Equal(left.m_animationName, right.m_animationName);
        Assert.Equal(0, left.m_flags & 4);
        Assert.Equal(4, right.m_flags & 4);

        // The turn about was given for one side only, so only that side has one.
        Assert.Empty(Clips("cannedTurnRight180").Where(c => c.StartsWith("CannedTurn", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A biped strafes and a quadruped steers, so a quadruped's ladder has a rung per
    /// gait and each rung is three clips blended on how hard it is being asked to turn.
    /// </summary>
    [Fact]
    public void AQuadrupedSteersInsteadOfStrafing()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonehound", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 60f),
                new RoledAnimation(Placeholder("WalkL.hkx"),
                    [new AnimationRole(RoleKind.Walk, Heading.Forward, Side: Side.Left)], Speed: 60f, TurnDegrees: 75f),
                new RoledAnimation(Placeholder("WalkR.hkx"),
                    [new AnimationRole(RoleKind.Walk, Heading.Forward, Side: Side.Right)], Speed: 60f, TurnDegrees: -75f),
                new RoledAnimation(Placeholder("Run.hkx"), [new AnimationRole(RoleKind.Run, Heading.Forward)], Speed: 300f),
                new RoledAnimation(Placeholder("Back.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Back)], Speed: 30f),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Equal(LocomotionPlan.Quadruped, made.Plan.Locomotion);

        var editor = new GraphEditor(HavokFile.Load(Path.Combine(
            Path.GetDirectoryName(made.ProjectPath)!, "behaviors", "BonehoundBehavior.hkx")));

        // Each rung steers: bearing left, straight on, bearing right, on the damped turn.
        hkbBlenderGenerator walk = editor.Require<hkbBlenderGenerator>("BonehoundWalkBlend");
        Assert.Equal([75f, 0f, -75f], walk.m_children.Select(c => c.m_weight));
        Assert.Equal("TurnDeltaDamped",
            editor.Strings.m_variableNames[walk.m_variableBindingSet!.m_bindings.Single().m_variableIndex]);

        // And the ladder is the gaits, at the speeds their clips deliver.
        hkbBlenderGenerator ladder = editor.Require<hkbBlenderGenerator>("BonehoundForwardBlend");
        Assert.Equal([60f, 300f], ladder.m_children.Select(c => c.m_weight));

        // Backing up is a state of its own, not an arm of anything.
        string[] backing =
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of("moveBackward"))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];
        Assert.Contains("WalkBackward", backing);
    }

    /// <summary>
    /// A creature with no walk swims for a living, and its swim is its locomotion
    /// rather than a state beside it, which is what the slaughterfish is.
    /// </summary>
    [Fact]
    public void ACreatureWithNoWalkSwimsForALiving()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonefish", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Float.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Swim.hkx"), [new AnimationRole(RoleKind.Swim, Heading.Forward)], Speed: 200f),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Equal(LocomotionPlan.Swimmer, made.Plan.Locomotion);

        string[] moving =
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of("moveStart"))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];

        Assert.Contains("SwimForward", moving);
        Assert.Equal(200f, made.Movement.ForwardWalk);
    }

    /// <summary>
    /// A creature that walks and also swims gets a swim beside its walk. The graph
    /// hears about water through one event and nothing else -- no depth, no surface,
    /// no submersion -- so a swim is a posture with locomotion in it.
    /// </summary>
    [Fact]
    public void AWalkerThatSwimsGetsAPostureForIt()
    {
        AssemblyResult made = CreatureAssembler.Assemble(
            new CreatureSpec("Bonewalker", Placeholder("skeleton.hkx"),
            [
                new RoledAnimation(Placeholder("Idle.hkx"), [new AnimationRole(RoleKind.Idle)]),
                new RoledAnimation(Placeholder("Walk.hkx"), [new AnimationRole(RoleKind.Walk, Heading.Forward)], Speed: 90f),
                new RoledAnimation(Placeholder("Paddle.hkx"), [new AnimationRole(RoleKind.Swim, Heading.Forward)], Speed: 60f),
            ]),
            Path.Combine(_folder, "out"));

        Assert.Contains(Module.Swimming, made.Plan.Modules);

        string[] Clips(params string[] events) =>
            [.. ActiveGenerators.Of(made.ProjectPath, _ => { }, Events.Of(events))
                .Active.Where(a => a.Generator is hkbClipGenerator).Select(a => a.Name)];

        // On dry land it walks; in the water it paddles. Leaving the water is not
        // asserted here: the evaluator is given a set of events rather than a sequence,
        // so swimStart and swimStop together are not one then the other.
        Assert.Contains("WalkForward", Clips("moveStart"));
        Assert.Contains("SwimSwimForward", Clips("swimStart", "moveStart"));
        Assert.DoesNotContain("WalkForward", Clips("swimStart", "moveStart"));
    }

    [Fact]
    public void AnimationsThatDoNotMakeACreatureAreRefusedBeforeAnythingIsWritten()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            Assemble(Animation("Idle", new AnimationRole(RoleKind.Idle))));

        Assert.Contains("forward walk", thrown.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_folder, "out")));
    }
}
