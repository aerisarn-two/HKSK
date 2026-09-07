using HKSK.Fbx;
using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The whole pipeline, on a project built from nothing.
/// </summary>
/// <remarks>
/// These cover what the corpus tests cover -- the cache index rule, agreement
/// with the behaviour graph, editing, and the FBX round trip -- but need no game
/// data, so a runner that can execute mopper runs all of them. The corpus tests
/// still say something these cannot, which is that the rules match what Bethesda
/// actually shipped; these say the code implementing those rules has not
/// regressed, which is the part CI can check on every push.
/// </remarks>
public class SyntheticProjectTests
{
    [MopperFact]
    public void TheFixtureOpensAsAProjectWithItsHavokFilesResolved()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        Assert.True(project.HasHavok, "the project, character and behaviour files should resolve");
        Assert.True(project.HasCache);

        Assert.Equal(3, project.Animations.Count);
        Assert.Equal(3, project.Clips.Count);
        Assert.Single(project.Behaviors);
        Assert.Empty(project.MissingBehaviors);
        Assert.NotNull(project.SkeletonPath);

        // Every clip found its generator, so the two sides agree on what exists.
        Assert.All(project.Clips, c => Assert.NotNull(c.Generator));
    }

    /// <summary>
    /// The rule the library rests on, on data where the answer is known by
    /// construction rather than inferred from the game.
    /// </summary>
    [MopperFact]
    public void ClipsAreNumberedByTheirAnimationsPositionInTheCharacterFile()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        Assert.Equal(0, project.Clip("WalkForward")!.CacheIndex);
        Assert.Equal(0, project.Clip("WalkForwardSlow")!.CacheIndex);
        Assert.Equal(1, project.Clip("RunForward")!.CacheIndex);

        // Two clips over one animation share the slot, and so its root motion.
        Assert.Same(project.Clip("WalkForward")!.Slot, project.Clip("WalkForwardSlow")!.Slot);

        // Slot 2 is numbered but no clip plays it -- the chaurus case, where the
        // numbering is positional rather than a dense sequence of its own.
        Assert.Equal(@"Animations\Unused.hkx", project.Animations[2].StoredName);
        Assert.Empty(project.ClipsOf(project.Animations[2]));

        Assert.Empty(ConsistencyReport.Check(project));
    }

    [MopperFact]
    public void RootMotionBelongsToTheAnimationNotTheClip()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        AnimationSlot walk = project.Animation("Walk")!;
        AnimationSlot run = project.Animation("Run")!;

        Assert.Equal(40f, walk.Motion!.Travel, 2);
        Assert.Equal(MathF.PI / 2f, walk.Motion.Turn, 3);
        Assert.Equal(120f, run.Motion!.Travel, 2);
        Assert.Equal(0f, run.Motion.Turn, 3);

        // The animation no clip plays has no motion recorded either.
        Assert.Null(project.Animations[2].Motion);

        foreach (Clip clip in project.ClipsOf(walk))
            Assert.Same(walk.Motion, clip.Slot!.Motion);
    }

    /// <summary>
    /// Removing an animation moves everything above it, in the character file,
    /// the clips and the motion blocks together.
    /// </summary>
    [MopperFact]
    public void RemovingAnAnimationRenumbersTheCacheWithIt()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        IReadOnlyList<string> orphaned = project.RemoveAnimation(project.Animation("Walk")!);

        // Both clips over it went with it.
        Assert.Equal(["WalkForward", "WalkForwardSlow"], orphaned.OrderBy(n => n, StringComparer.Ordinal));

        // Run was slot 1 and is now slot 0, everywhere at once.
        AnimationSlot run = project.Animation("Run")!;
        Assert.Equal(0, run.Index);
        Assert.Equal(0, project.Clip("RunForward")!.CacheIndex);
        Assert.Equal(0, run.Motion!.CacheIndex);
        Assert.Equal(120f, run.Motion.Travel, 2);

        // And the unused slot moved down too rather than being left behind.
        Assert.Equal(@"Animations\Unused.hkx", project.Animations[1].StoredName);

        project.SaveCharacter();
        fixture.Cache().Save(fixture.Meshes);
    }

    /// <summary>
    /// Speed and crop are copied from the behaviour verbatim, so the validator
    /// notices when they stop matching.
    /// </summary>
    [MopperFact]
    public void ADriftedPlaybackSpeedIsReported()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        Assert.Empty(ConsistencyReport.Check(project));

        project.Clip("RunForward")!.Entry.PlaybackSpeed = 2.5f;

        Finding finding = Assert.Single(ConsistencyReport.Check(project));
        Assert.Equal("speed-mismatch", finding.Kind);
        Assert.Equal(Severity.Drift, finding.Severity);
    }

    /// <summary>
    /// A cache index pointing past the character file's list is reported, which
    /// is what a bad renumbering would look like.
    /// </summary>
    [MopperFact]
    public void AnIndexPastTheEndOfTheAnimationListIsReported()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        project.Clip("RunForward")!.Entry.CacheIndex = 99;

        var kinds = ConsistencyReport.Check(project).Select(f => f.Kind).ToList();
        Assert.Contains("index-out-of-range", kinds);
    }

    /// <summary>
    /// Out to FBX and back, carrying the cache's root motion and the animation's
    /// events -- the whole reason HKSK and HKFBX are paired.
    /// </summary>
    [MopperFact]
    public void AnAnimationSurvivesTheRoundTripThroughFbx()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        AnimationSlot walk = project.Animation("Walk")!;
        float travel = walk.Motion!.Travel;
        float turn = walk.Motion.Turn;

        var exchange = new AnimationExchange();
        string fbx = Path.Combine(fixture.Folder, "walk.fbx");

        ExchangeResult exported = exchange.Export(project, walk, fbx);
        Assert.True(exported.Succeeded, exported.Problem);

        ExchangeResult imported = exchange.Import(
            project, fbx, new ImportOptions { StoredName = walk.StoredName });
        Assert.True(imported.Succeeded, imported.Problem);

        // Replaced, so the slot and everything pointing at it stayed put.
        Assert.Equal(walk.Index, imported.CacheIndex);
        Assert.Equal(3, project.Animations.Count);

        AnimationSlot after = project.Animation("Walk")!;
        Assert.Equal(travel, after.Motion!.Travel, 1);
        Assert.Equal(turn, after.Motion.Turn, 2);
    }

    /// <summary>
    /// A batch of FBX files becomes new animations, appended, leaving the
    /// existing numbering alone -- and the whole thing survives being saved and
    /// read back.
    /// </summary>
    [MopperFact]
    public void ABatchImportAppendsAndSurvivesASaveAndReload()
    {
        using var fixture = SyntheticProject.Build();
        HavokProject project = fixture.Open();

        var exchange = new AnimationExchange();
        string folder = Path.Combine(fixture.Folder, "fbx");

        IReadOnlyList<ExchangeResult> exports = exchange.ExportAll(project, folder);
        Assert.Equal(3, exports.Count);
        Assert.All(exports, r => Assert.True(r.Succeeded, r.Problem));

        IReadOnlyList<ExchangeResult> imports = exchange.ImportAll(
            project,
            Directory.GetFiles(folder, "*.fbx").OrderBy(f => f, StringComparer.Ordinal),
            path => new ImportOptions
            {
                StoredName = $@"Animations\New_{Path.GetFileNameWithoutExtension(path)}.hkx",
                ClipName = $"New_{Path.GetFileNameWithoutExtension(path)}",
            });

        Assert.All(imports, r => Assert.True(r.Succeeded, r.Problem));
        Assert.Equal([3, 4, 5], imports.Select(r => r.CacheIndex!.Value).OrderBy(i => i));

        // Nothing that existed before moved.
        Assert.Equal(0, project.Clip("WalkForward")!.CacheIndex);
        Assert.Equal(1, project.Clip("RunForward")!.CacheIndex);

        // The animation list lives in the character file, so both halves save.
        Assert.True(project.CharacterModified);
        project.SaveCharacter();

        SkyrimCache cache = fixture.Cache();
        cache.AnimationData.Projects[0].Block.Clips.Clear();
        foreach (Cache.ClipGeneratorEntry clip in project.Data.Block.Clips)
            cache.AnimationData.Projects[0].Block.Clips.Add(clip);
        cache.AnimationData.Projects[0].Movements = project.Data.Movements;
        cache.Save();

        HavokProject reread = fixture.Open();

        Assert.Equal(6, reread.Animations.Count);
        Assert.Equal(6, reread.Clips.Count);
        Assert.Equal(120f, reread.Animation("New_Run")!.Motion!.Travel, 1);
        Assert.DoesNotContain(ConsistencyReport.Check(reread), f => f.Severity == Severity.Error);
    }
}
