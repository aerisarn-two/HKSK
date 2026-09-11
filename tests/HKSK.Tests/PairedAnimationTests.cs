using HKSK.Havok;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Reading the two skeletons out of an animation that drives both.
/// </summary>
public class PairedAnimationTests
{
    private static readonly string[] Killmove =
    [
        "PairedRoot",
        "NPC", "NPC Root [Root]", "NPC COM [COM ]",
        "2_", "2_NPC Root [Root]", "2_NPC Spine [Spn0]",
    ];

    private static PairedAnimation Read(params string[] tracks) =>
        PairedAnimation.FromTracks("test.hkx", tracks, PairedAnimation.RootBone);

    [Fact]
    public void SplitsTheTwoActorsOnThePrefix()
    {
        PairedAnimation paired = Read(Killmove);

        Assert.True(paired.IsCombined);
        Assert.Equal(["NPC Root [Root]", "NPC COM [COM ]"], paired.DriverBones);
        Assert.Equal(["NPC Root [Root]", "NPC Spine [Spn0]"], paired.PartnerBones);
    }

    /// <summary>
    /// The three bones that belong to neither actor: the pair's root and the
    /// subtree root each hangs from. No rig contains its own -- a human rig has no
    /// bone called <c>NPC</c> -- so counting them as bones makes both halves look
    /// one short of matching.
    /// </summary>
    [Fact]
    public void KeepsTheStructuralRootsOutOfBothHalves()
    {
        PairedAnimation paired = Read(Killmove);

        Assert.Equal(0, paired.RootTrack);
        Assert.Equal(1, paired.DriverRootTrack);
        Assert.Equal(4, paired.PartnerRootTrack);

        Assert.DoesNotContain("NPC", paired.DriverBones);
        Assert.DoesNotContain(PairedAnimation.PartnerPrefix, paired.PartnerBones);
        Assert.Equal(Killmove.Length, paired.Tracks.Count);
    }

    /// <summary>
    /// The bare prefix is the partner's subtree root and was once counted as a
    /// bone of the driver, which put a bone of the second actor in the first
    /// actor's half.
    /// </summary>
    [Fact]
    public void TheBarePrefixBelongsToThePartner()
    {
        PairedAnimation paired = Read("PairedRoot", "NPC Root [Root]", "2_");

        Assert.Equal(2, paired.PartnerRootTrack);
        Assert.Equal(["NPC Root [Root]"], paired.DriverBones);
        Assert.True(paired.IsCombined);
    }

    [Fact]
    public void AnAnimationWithNoPartnerHalfIsNotCombined()
    {
        PairedAnimation paired = Read("NPC Root [Root]", "NPC COM [COM ]");

        Assert.False(paired.IsCombined);
        Assert.Empty(paired.PartnerBones);
    }

    [Theory]
    [InlineData("2_NPC Root [Root]", true)]
    [InlineData("2_", false)]
    [InlineData("NPC Root [Root]", false)]
    [InlineData(null, false)]
    public void KnowsAPrefixedBoneFromTheRest(string? name, bool prefixed) =>
        Assert.Equal(prefixed, PairedAnimation.IsPartnerTrack(name));

    [Theory]
    [InlineData("2_NPC Spine [Spn0]", "NPC Spine [Spn0]")]
    [InlineData("NPC Spine [Spn0]", "NPC Spine [Spn0]")]
    [InlineData("2_", "2_")]
    public void TakesThePrefixOffWhereThereIsOne(string track, string bone) =>
        Assert.Equal(bone, PairedAnimation.WithoutPrefix(track));

    /// <summary>
    /// The packfile says what it is rigged to, which is the cheapest test there
    /// is: only a paired animation names PairedRoot.
    /// </summary>
    [Theory]
    [InlineData("PairedRoot", true)]
    [InlineData("pairedroot", true)]
    [InlineData("NPC Root [Root]", false)]
    [InlineData(null, false)]
    public void RecognisesThePairedBindingByName(string? skeleton, bool paired) =>
        Assert.Equal(paired, PairedAnimation.IsPairedSkeletonName(skeleton));
}
