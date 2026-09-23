using HKSK.Assembly;
using HKSK.Behavior;
using HKSK.Engine;
using HKSK.Havok;
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
    [Fact]
    public void TheEngineReachesTheIdleAtRestAndTheWalkOnMoveStart()
    {
        AssemblyResult made = Walker();

        string[] Clips(Events events, float speed) =>
            [.. ActiveGenerators
                .Of(made.ProjectPath, tables => { foreach (Variables v in tables.Values) v.Set("SpeedSampled", speed); }, events)
                .Active.Where(a => a.Generator is hkbClipGenerator)
                .Select(a => a.Name)];

        Assert.Equal(["Idle"], Clips(Events.None, 0f));
        Assert.Contains("WalkForward", Clips(Events.Of("moveStart"), 1f));
        Assert.Contains("RunForward", Clips(Events.Of("moveStart"), 3f));
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
