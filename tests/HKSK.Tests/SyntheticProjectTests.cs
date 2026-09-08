using System.Numerics;
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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
        ActorProject project = fixture.Open();

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
    /// The travel stays in the cache and out of the animation.
    /// </summary>
    /// <remarks>
    /// In Skyrim an animation's root bone does not move -- the cache says how
    /// far the clip carries the actor and the game applies it. FBX has nowhere
    /// to put that, so exporting drives the root bone with it, which means
    /// importing finds the travel twice: once as the root's animation and once
    /// as root motion. Left alone the actor moves twice as far.
    ///
    /// So the root has to come back to the origin, and the cache has to keep
    /// exactly what it had.
    /// </remarks>
    [MopperFact]
    public void ImportingTakesTheTravelOutOfTheAnimationAndLeavesItInTheCache()
    {
        using var fixture = SyntheticProject.Build();
        ActorProject project = fixture.Open();

        AnimationSlot run = project.Animation("Run")!;
        Assert.Equal(120f, run.Motion!.Travel, 2);

        // As shipped: the root never leaves the origin.
        Assert.Equal(0f, RootExcursion(project, run), 2);

        var exchange = new AnimationExchange();
        string fbx = Path.Combine(fixture.Folder, "run.fbx");

        Assert.True(exchange.Export(project, run, fbx).Succeeded);
        Assert.True(exchange.Import(project, fbx, new ImportOptions { StoredName = run.StoredName }).Succeeded);

        // Still at the origin, and the cache still records the travel once.
        Assert.Equal(0f, RootExcursion(project, project.Animation("Run")!), 1);
        Assert.Equal(120f, project.Animation("Run")!.Motion!.Travel, 1);
    }

    /// <summary>
    /// The exported curve ramps from the origin rather than starting at the
    /// destination.
    /// </summary>
    /// <remarks>
    /// The cache stores a displacement that is implicitly zero at t=0 and
    /// usually holds a single key at the end carrying the whole of it. Sampled
    /// as written that is a constant, so the animation would begin at its final
    /// offset and stay there -- a chicken standing 251 units from where it
    /// should be instead of running there.
    /// </remarks>
    [MopperFact]
    public void TheExportedRootMotionRampsFromTheOrigin()
    {
        using var fixture = SyntheticProject.Build();
        ActorProject project = fixture.Open();

        AnimationSlot run = project.Animation("Run")!;
        Assert.Single(run.Motion!.Translations);          // one key, at the end

        var exchange = new AnimationExchange();
        string fbx = Path.Combine(fixture.Folder, "run.fbx");
        Assert.True(exchange.Export(project, run, fbx).Succeeded);

        LeanMeshIO.FbxDocument document;
        using (FileStream stream = File.OpenRead(fbx)) document = LeanMeshIO.FbxDocument.Load(stream);

        HKFBX.Model.Skeleton skeleton = HKFBX.Fbx.FbxAnimationReader.ReadSkeleton(document);
        HKFBX.Model.RootMotion motion = HKFBX.Fbx.FbxAnimationReader.ReadRootMotion(document, skeleton);

        Assert.True(motion.Translations.Count > 2, "the motion should be sampled across the clip");
        Assert.Equal(0f, motion.Translations[0].Value.Length(), 2);
        Assert.Equal(120f, motion.Translations[^1].Value.Length(), 1);

        // And it is monotonic, not a step at the end.
        float half = motion.TranslationAt(run.Motion.Duration / 2f).Length();
        Assert.InRange(half, 40f, 80f);
    }

    /// <summary>
    /// An FBX that never came from here: an animator moved the root bone, and
    /// the travel has to end up in the cache.
    /// </summary>
    /// <remarks>
    /// The other exchange tests are a closed loop -- exported from a project and
    /// imported straight back -- so the FBX already carries the motion where the
    /// exporter put it. That proves the round trip and not much else. This is
    /// the case the exchange actually exists for: a take authored elsewhere,
    /// with the travel on the root bone and no root motion declared anywhere,
    /// which is what any animation package produces.
    /// </remarks>
    [MopperFact]
    public void AnFbxAuthoredElsewhereHasItsTravelExtractedIntoTheCache()
    {
        using var fixture = SyntheticProject.Build();
        ActorProject project = fixture.Open();

        const float travel = 200f;
        const float turn = MathF.PI / 2f;

        HKFBX.Model.Skeleton skeleton = HKFBX.Hkx.HkxAnimationFile.ReadSkeleton(project.SkeletonPath!);
        string fbx = Path.Combine(fixture.Folder, "authored.fbx");
        WriteAuthoredTake(skeleton, fbx, travel, turn);

        var exchange = new AnimationExchange();
        ExchangeResult result = exchange.Import(project, fbx, new ImportOptions
        {
            StoredName = @"Animations\Authored.hkx",
            ClipName = "AuthoredClip",
        });

        Assert.True(result.Succeeded, result.Problem);

        // The travel was taken off the root and written to the cache.
        AnimationSlot slot = project.Animation("Authored")!;
        Assert.NotNull(slot.Motion);
        Assert.Equal(travel, slot.Motion!.Travel, 1);
        Assert.Equal(turn, slot.Motion.Turn, 2);

        // And the animation itself keeps its root at the origin, as Skyrim's do.
        Assert.Equal(0f, RootExcursion(project, slot), 1);

        Assert.Equal(3, result.CacheIndex);
        Assert.NotNull(project.Clip("AuthoredClip"));
    }

    /// <summary>
    /// A take as an animation package writes one: the travel is the root bone's
    /// own animation, and nothing declares it as root motion.
    /// </summary>
    private static void WriteAuthoredTake(
        HKFBX.Model.Skeleton skeleton, string path, float travel, float turn)
    {
        const int frames = 31;
        const float frameDuration = 1f / 30f;

        var transforms = new HKFBX.Model.BoneTransform[frames * skeleton.Count];

        for (int frame = 0; frame < frames; frame++)
        {
            float progress = frame / (float)(frames - 1);

            for (int bone = 0; bone < skeleton.Count; bone++)
                transforms[frame * skeleton.Count + bone] = bone == 0
                    ? new HKFBX.Model.BoneTransform(
                        new Vector3(0f, progress * travel, 0f),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, progress * turn),
                        Vector3.One)
                    : new HKFBX.Model.BoneTransform(
                        new Vector3(0f, MathF.Sin(frame * frameDuration * MathF.Tau) * 1.5f, 0f),
                        Quaternion.Identity,
                        Vector3.One);
        }

        var animation = new HKFBX.Model.SampledAnimation
        {
            FrameCount = frames,
            TrackCount = skeleton.Count,
            Duration = (frames - 1) * frameDuration,
            FrameDuration = frameDuration,
            Transforms = transforms,

            // Nothing declares root motion: it is simply how the root is animated.
            RootMotion = HKFBX.Model.RootMotion.None,
        };

        using FileStream stream = File.Create(path);
        HKFBX.Fbx.FbxAnimationWriter.Build(skeleton, animation, "authored").Save(stream);
    }

    /// <summary>How far the root bone strays from the origin, over the whole clip.</summary>
    private static float RootExcursion(ActorProject project, AnimationSlot slot)
    {
        (HKFBX.Codec.SplineAnimationData spline, _, _) =
            HKFBX.Hkx.HkxAnimationFile.ReadAnimation(project.AnimationPath(slot)!);

        HKFBX.Model.SampledAnimation sampled = new HKFBX.Codec.MopperAnimationCodec().Decompress(spline);

        float worst = 0f;
        for (int frame = 0; frame < sampled.FrameCount; frame++)
            worst = Math.Max(worst, sampled[frame, 0].Translation.Length());

        return worst;
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
        ActorProject project = fixture.Open();

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

        ActorProject reread = fixture.Open();

        Assert.Equal(6, reread.Animations.Count);
        Assert.Equal(6, reread.Clips.Count);
        Assert.Equal(120f, reread.Animation("New_Run")!.Motion!.Travel, 1);
        Assert.DoesNotContain(ConsistencyReport.Check(reread), f => f.Severity == Severity.Error);
    }
}
