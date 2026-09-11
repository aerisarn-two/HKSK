using HKFBX.Codec;
using HKFBX.Fbx;
using HKFBX.Hkx;
using HKSK.Fbx;
using HKSK.Havok;
using HKSK.Model;
using Xunit;
using HkFbx = HKFBX.Model;

namespace HKSK.Tests;

/// <summary>
/// Moving a paired animation out to FBX and back.
/// </summary>
/// <remarks>
/// Before this existed, exporting one succeeded and wrote a lie: the bear's
/// killmove came out as 76 bear bones with no <c>2_</c> bone anywhere and 177
/// tracks mapped onto them by position, which is a bear playing a human's motion
/// with three quarters of the animation discarded.
///
/// Nothing here writes to the corpus. The round trip writes its packfile to a
/// temporary folder and edits the cache in memory only, which is why the slots it
/// takes are new ones rather than the ones the game ships.
/// </remarks>
public class PairedExchangeTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hksk-paired-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* a temp folder */ }
    }

    private static (SkyrimCache Cache, ActorProject Bear, ActorProject Human, AnimationSlot Slot) Killmove()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ActorProject bear = cache.OpenActor("BearProject")!;
        ActorProject human = cache.OpenActor("DefaultMale")!;

        AnimationSlot slot = bear.Animations.First(
            animation => animation.StoredName.Contains("Paired_1HMKillMoveBearA", StringComparison.OrdinalIgnoreCase));

        return (cache, bear, human, slot);
    }

    private static HkFbx.Skeleton SkeletonOf(string fbxPath)
    {
        using FileStream stream = File.OpenRead(fbxPath);

        return FbxAnimationReader.ReadSkeleton(LeanMeshIO.FbxDocument.Load(stream));
    }

    /// <summary>
    /// Both actors come out, and every track has a bone to live on.
    /// </summary>
    [FbxFact]
    public void ExportsBothSkeletons()
    {
        var (_, bear, _, slot) = Killmove();
        string fbx = Path.Combine(_temp, "killmove.fbx");

        Assert.True(new AnimationExchange().Export(bear, slot, fbx).Succeeded);

        PairedAnimation paired = PairedAnimation.Read(bear.AnimationPath(slot)!);
        HkFbx.Skeleton skeleton = SkeletonOf(fbx);

        Assert.Equal(paired.Tracks.Count, skeleton.Count);
        Assert.Equal(
            paired.Tracks.Order(),
            skeleton.Bones.Select(bone => bone.Name).Order());

        Assert.Contains(skeleton.Bones, bone => PairedAnimation.IsPartnerTrack(bone.Name));
        Assert.Single(skeleton.Bones.Where(bone => bone.ParentIndex < 0));
    }

    /// <summary>
    /// Without the partner the far half is still exported, flat and at rest;
    /// with it, the rest pose is the partner's own.
    /// </summary>
    [FbxFact]
    public void ThePartnersRigFillsInItsRestPose()
    {
        var (_, bear, human, slot) = Killmove();

        string alone = Path.Combine(_temp, "alone.fbx");
        string paired = Path.Combine(_temp, "withpartner.fbx");

        var exchange = new AnimationExchange();
        Assert.True(exchange.Export(bear, slot, alone).Succeeded);
        Assert.True(exchange.Export(bear, slot, paired, new ExportOptions { Partner = human }).Succeeded);

        Assert.True(AtRest(SkeletonOf(alone)) > AtRest(SkeletonOf(paired)));

        // Only the three structural bones are left without a pose of their own.
        Assert.Equal(3, AtRest(SkeletonOf(paired)));

        static int AtRest(HkFbx.Skeleton skeleton) =>
            skeleton.Bones.Count(bone =>
                bone.ReferencePose.Translation == System.Numerics.Vector3.Zero
                && bone.ReferencePose.Rotation == System.Numerics.Quaternion.Identity);
    }

    /// <summary>
    /// Out and back with the motion landing on the same bones it left.
    /// </summary>
    /// <remarks>
    /// An FBX read back gives its bones depth first, and a paired animation's
    /// tracks are in the order its skeleton was authored in -- the bear killmove's
    /// two orders part company at bone 13. Writing the tracks in the order they
    /// came back would put every bone's motion on some other bone, and the check
    /// that catches it is the distance one: two different bones of this animation
    /// are about 30 units apart, so a scrambled import cannot pass a tolerance of
    /// a hundredth of a unit.
    /// </remarks>
    [FbxFact]
    public void RoundTripsWithoutMovingTheMotionToOtherBones()
    {
        var (cache, bear, human, slot) = Killmove();

        string source = bear.AnimationPath(slot)!;
        string fbx = Path.Combine(_temp, "roundtrip.fbx");
        string rewritten = Path.Combine(_temp, "roundtrip.hkx");

        var exchange = new AnimationExchange();
        Assert.True(exchange.Export(bear, slot, fbx, new ExportOptions { Partner = human }).Succeeded);

        PairedExchangeResult result = exchange.ImportPaired(
            cache, fbx, rewritten, [bear, human], new ImportOptions { TemplatePath = source });

        Assert.True(result.Succeeded, result.Problem);

        PairedAnimation before = PairedAnimation.Read(source);
        PairedAnimation after = PairedAnimation.Read(rewritten);

        Assert.Equal(before.Tracks, after.Tracks);
        Assert.Equal(before.BoundSkeletonName, after.BoundSkeletonName);
        Assert.Equal(before.PartnerTracks.Count, after.PartnerTracks.Count);

        var codec = new MopperAnimationCodec();
        HkFbx.SampledAnimation original = codec.Decompress(HkxAnimationFile.ReadAnimation(source).Animation);
        HkFbx.SampledAnimation copy = codec.Decompress(HkxAnimationFile.ReadAnimation(rewritten).Animation);

        Assert.Equal(original.TrackCount, copy.TrackCount);
        Assert.Equal(original.FrameCount, copy.FrameCount);

        float worst = 0;

        for (int frame = 0; frame < original.FrameCount; frame++)
            for (int track = 0; track < original.TrackCount; track++)
            {
                worst = Math.Max(worst, System.Numerics.Vector3.Distance(
                    original.Transforms[(frame * original.TrackCount) + track].Translation,
                    copy.Transforms[(frame * copy.TrackCount) + track].Translation));
            }

        Assert.True(worst < 0.01f, $"worst track moved by {worst:F4} units");
    }

    /// <summary>
    /// Every project that plays it gets a slot, and each numbers it for itself.
    /// </summary>
    [FbxFact]
    public void ListsTheAnimationInEveryProjectThatPlaysIt()
    {
        var (cache, bear, human, slot) = Killmove();

        string fbx = Path.Combine(_temp, "registered.fbx");
        string rewritten = Path.Combine(_temp, "registered.hkx");

        var exchange = new AnimationExchange();
        exchange.Export(bear, slot, fbx, new ExportOptions { Partner = human });

        int bearSlots = bear.Animations.Count;
        int humanSlots = human.Animations.Count;

        PairedExchangeResult result = exchange.ImportPaired(
            cache, fbx, rewritten, [bear, human],
            new ImportOptions { TemplatePath = bear.AnimationPath(slot)!, ClipName = "TestPairedClip" });

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(2, result.Parts!.Count);

        Assert.Equal(bearSlots + 1, bear.Animations.Count);
        Assert.Equal(humanSlots + 1, human.Animations.Count);

        foreach (PairedPart part in result.Parts)
        {
            Assert.NotNull(part.Project.Clip("TestPairedClip"));
            Assert.Equal(part.Slot.Index, part.Project.Clip("TestPairedClip")!.CacheIndex);
        }
    }

    /// <summary>
    /// An FBX holding one actor is not a pairing, and saying so is better than
    /// writing a file that claims to be one.
    /// </summary>
    [FbxFact]
    public void RefusesAnFbxWithOnlyOneActor()
    {
        var (cache, bear, _, _) = Killmove();

        AnimationSlot plain = bear.Animations.First(animation =>
            !animation.StoredName.Contains("paired", StringComparison.OrdinalIgnoreCase)
            && bear.AnimationPath(animation) is not null);

        string fbx = Path.Combine(_temp, "plain.fbx");
        Assert.True(new AnimationExchange().Export(bear, plain, fbx).Succeeded);

        PairedExchangeResult result = new AnimationExchange()
            .ImportPaired(cache, fbx, Path.Combine(_temp, "plain.hkx"), [bear]);

        Assert.False(result.Succeeded);
        Assert.Contains(PairedAnimation.PartnerPrefix, result.Problem);
    }

    /// <summary>
    /// An ordinary animation still exports against its own skeleton.
    /// </summary>
    [FbxFact]
    public void LeavesOrdinaryAnimationsAlone()
    {
        var (_, bear, _, _) = Killmove();

        AnimationSlot plain = bear.Animations.First(animation =>
            !animation.StoredName.Contains("paired", StringComparison.OrdinalIgnoreCase)
            && bear.AnimationPath(animation) is not null);

        string fbx = Path.Combine(_temp, "ordinary.fbx");
        Assert.True(new AnimationExchange().Export(bear, plain, fbx).Succeeded);

        HkFbx.Skeleton skeleton = SkeletonOf(fbx);

        Assert.DoesNotContain(skeleton.Bones, bone => bone.Name == PairedAnimation.RootBone);
        Assert.DoesNotContain(skeleton.Bones, bone => PairedAnimation.IsPartnerTrack(bone.Name));
    }
}
