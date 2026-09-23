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

        Assert.Equal(
            [@"animations\Idle.hkx", @"animations\Walk.hkx", @"animations\Run.hkx"],
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
        Assert.Equal(2, actor.Animations.Count);
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

    [Fact]
    public void AnimationsThatDoNotMakeACreatureAreRefusedBeforeAnythingIsWritten()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            Assemble(Animation("Idle", new AnimationRole(RoleKind.Idle))));

        Assert.Contains("forward walk", thrown.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_folder, "out")));
    }
}
