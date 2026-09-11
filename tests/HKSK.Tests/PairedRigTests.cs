using System.Numerics;
using HKSK.Fbx;
using HKSK.Havok;
using Xunit;
using HkFbx = HKFBX.Model;

namespace HKSK.Tests;

/// <summary>
/// Rebuilding the skeleton a paired animation is rigged to.
/// </summary>
/// <remarks>
/// Nothing ships <c>PairedRoot</c>, so it is built from the animation's track
/// names and whatever rigs the caller has. These pin what that build guarantees:
/// every track gets a bone, the order is the animation's, and the hierarchy is
/// the rigs' where a rig knows it.
/// </remarks>
public class PairedRigTests
{
    private static readonly string[] Tracks =
    [
        "PairedRoot",
        "NPC", "NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]",
        "2_", "2_NPC Root [Root]", "2_NPC Pelvis",
    ];

    private static PairedAnimation Paired(params string[] tracks) =>
        PairedAnimation.FromTracks("test.hkx", tracks.Length > 0 ? tracks : Tracks, PairedAnimation.RootBone);

    /// <summary>A rig shaped like the game's: a root, and bones under it.</summary>
    private static HkFbx.Skeleton Rig(params string[] names)
    {
        var bones = new List<HkFbx.Bone>();

        for (int bone = 0; bone < names.Length; bone++)
        {
            var pose = new HkFbx.BoneTransform(
                new Vector3(bone, 0, 0), Quaternion.Identity, Vector3.One);

            bones.Add(new HkFbx.Bone(names[bone], bone == 0 ? -1 : bone - 1, pose));
        }

        return new HkFbx.Skeleton { Name = "rig", Bones = bones };
    }

    [Fact]
    public void EveryTrackGetsABone()
    {
        HkFbx.Skeleton built = PairedRig.Build(Paired(), null, null);

        Assert.Equal(Tracks.Length, built.Count);
        Assert.Equal(Tracks, built.Bones.Select(bone => bone.Name));
    }

    /// <summary>
    /// The order is the animation's, not either skeleton's. The binding maps track
    /// to bone one for one, and the game's own files are not in tree order --
    /// Human&amp;Draugr lists the partner first, Human&amp;Dragon second.
    /// </summary>
    [Fact]
    public void KeepsTheAnimationsOrderEvenWhenTheRigDiffers()
    {
        var reversed = Rig("NPC Spine [Spn0]", "NPC COM [COM ]", "NPC Root [Root]");

        HkFbx.Skeleton built = PairedRig.Build(Paired(), reversed, null);

        Assert.Equal(Tracks, built.Bones.Select(bone => bone.Name));
    }

    [Fact]
    public void OnlyThePairsRootHasNoParent()
    {
        HkFbx.Skeleton built = PairedRig.Build(Paired(), null, null);

        var root = Assert.Single(built.Bones.Where(bone => bone.ParentIndex < 0));
        Assert.Equal(PairedAnimation.RootBone, root.Name);
    }

    [Fact]
    public void BothActorsHangFromThePairsRoot()
    {
        HkFbx.Skeleton built = PairedRig.Build(Paired(), null, null);
        var names = built.Bones.Select(bone => bone.Name).ToList();

        foreach (string subtree in new[] { PairedAnimation.DriverRootBone, PairedAnimation.PartnerPrefix })
            Assert.Equal(PairedAnimation.RootBone, built.Bones[built.Bones[names.IndexOf(subtree)].ParentIndex].Name);
    }

    /// <summary>
    /// Where a rig knows a bone, its hierarchy and its rest pose come across.
    /// </summary>
    [Fact]
    public void TakesHierarchyAndPoseFromTheRig()
    {
        var human = Rig("NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]");

        HkFbx.Skeleton built = PairedRig.Build(Paired(), human, null);
        var names = built.Bones.Select(bone => bone.Name).ToList();

        int spine = names.IndexOf("NPC Spine [Spn0]");
        Assert.Equal("NPC COM [COM ]", built.Bones[built.Bones[spine].ParentIndex].Name);
        Assert.Equal(new Vector3(2, 0, 0), built.Bones[spine].ReferencePose.Translation);

        // A rig root keeps the subtree root as its parent: the pair's tree is one
        // tree, and only PairedRoot is rootless.
        int actorRoot = names.IndexOf("NPC Root [Root]");
        Assert.Equal(PairedAnimation.DriverRootBone, built.Bones[built.Bones[actorRoot].ParentIndex].Name);
    }

    /// <summary>
    /// Exporting a killmove when nobody has said who the other actor is still has
    /// to produce every track, or the animation is silently cut in half.
    /// </summary>
    [Fact]
    public void SynthesisesTheHalfNoRigAccountsFor()
    {
        var human = Rig("NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]");

        HkFbx.Skeleton built = PairedRig.Build(Paired(), human, null);
        var names = built.Bones.Select(bone => bone.Name).ToList();

        int pelvis = names.IndexOf("2_NPC Pelvis");
        Assert.Equal(PairedAnimation.PartnerPrefix, built.Bones[built.Bones[pelvis].ParentIndex].Name);
        Assert.Equal(HkFbx.BoneTransform.Identity.Translation, built.Bones[pelvis].ReferencePose.Translation);
    }

    /// <summary>
    /// Which half a project is cannot be assumed from the folder the animation
    /// sits in, so the rigs are matched against the halves.
    /// </summary>
    [Fact]
    public void MatchesARigToTheHalfItAccountsFor()
    {
        Assert.True(PairedRig.DriverIs(Paired(), Rig("NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]")));
        Assert.False(PairedRig.DriverIs(Paired(), Rig("NPC Root [Root]", "NPC Pelvis")));
    }

    [Fact]
    public void RefusesAnAnimationThatIsNotPaired()
    {
        var single = PairedAnimation.FromTracks("plain.hkx", ["NPC Root [Root]", "NPC COM [COM ]"]);

        Assert.Throws<ArgumentException>(() => PairedRig.Build(single, null, null));
    }
}
