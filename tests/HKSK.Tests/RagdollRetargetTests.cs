using System.Numerics;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Fitting a shipped ragdoll to a rig of one's own: what the game's files say a fitted one
/// looks like, checked against one this code fitted.
/// </summary>
public sealed class RagdollRetargetTests
{
    private static string Skeletons => Corpus.Path_("actors", "draugr", "character assets", "skeletons.hkx");
    private static string Draugr => Corpus.Path_("actors", "draugr", "character assets", "skeleton.hkx");

    private static hkaSkeleton Rig(string path) =>
        HavokFile.Load(path).All<hkaAnimationContainer>().First().m_skeletons[0];

    private static Matrix4x4[] World(hkaSkeleton rig)
    {
        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            Matrix4x4 local = RagdollRetarget.Real(rig.m_referencePose[i]);
            short parent = rig.m_parentIndices[i];
            world[i] = parent < 0 ? local : local * world[parent];
        }
        return world;
    }

    /// <summary>
    /// A ragdoll fitted to the rig it came from is the ragdoll it came from. Nothing about the
    /// creature changed, so nothing about its bodies may have moved: this is the fixed point
    /// every other fitting is measured against.
    /// </summary>
    [CorpusFact]
    public void FittingARagdollToItsOwnRigMovesNothing()
    {
        var before = HavokFile.Load(Skeletons);
        var original = before.All<hkaRagdollInstance>().First();
        var wasAt = original.m_rigidBodies
            .ToDictionary(b => b.m_name, b => b.m_motion.m_motionState.m_transform.Translation);
        var wasSize = original.m_rigidBodies
            .Where(b => b.m_collidable?.m_shape is hkpCapsuleShape)
            .ToDictionary(b => b.m_name, b => ((hkpCapsuleShape)b.m_collidable!.m_shape!).m_radius);

        RetargetedRagdoll made = RagdollRetarget.Fit(Skeletons, Rig(Skeletons));

        Assert.Equal(19, made.Bodies);
        Assert.Empty(made.Dropped);
        Assert.Equal(1f, made.Scale, 3);

        var now = made.File.All<hkaRagdollInstance>().First();
        Assert.Equal(wasAt.Count, now.m_rigidBodies.Count);

        foreach (hkpRigidBody body in now.m_rigidBodies)
        {
            Assert.True(
                (wasAt[body.m_name] - body.m_motion.m_motionState.m_transform.Translation).Length() < 0.01f,
                $"'{body.m_name}' moved");

            if (body.m_collidable?.m_shape is hkpCapsuleShape capsule)
                Assert.Equal(wasSize[body.m_name], capsule.m_radius, 3);
        }
    }

    /// <summary>
    /// The invariant the fitting rests on, and the reason it can be this simple: in the game's
    /// own files a ragdoll bone sits exactly where the animation bone of the same name sits,
    /// and a rigid body's transform is that same pose again.
    /// </summary>
    [CorpusFact]
    public void ARagdollBoneSitsWhereItsAnimationBoneSits()
    {
        foreach (string path in new[] { Skeletons, Draugr })
        {
            var file = HavokFile.Load(path);
            var container = file.All<hkaAnimationContainer>().First();
            hkaSkeleton rig = container.m_skeletons[0], ragdoll = container.m_skeletons[1];
            var instance = file.All<hkaRagdollInstance>().First();

            Matrix4x4[] rigWorld = World(rig), ragWorld = World(ragdoll);

            // Which bone stands for which is the mapper's business, not the name's: the
            // draugr's rig calls its left foot 'NPC L Foot [LLft ]' and its ragdoll spells the
            // same bone without the doubled L.
            var mapper = file.All<hkaSkeletonMapper>().First(m =>
                ReferenceEquals(m.m_mapping?.m_skeletonA, rig) && ReferenceEquals(m.m_mapping?.m_skeletonB, ragdoll));

            foreach (var pair in mapper.m_mapping!.m_simpleMappings)
            {
                int i = pair.m_boneB, bone = pair.m_boneA;

                Assert.True(
                    (rigWorld[bone].Translation - ragWorld[i].Translation).Length() < 0.01f,
                    $"{path}: ragdoll bone '{ragdoll.m_bones[i].m_name}' is not where " +
                    $"'{rig.m_bones[bone].m_name}' is");

                Vector3 body = instance.m_rigidBodies[i].m_motion.m_motionState.m_transform.Translation;
                Assert.True(
                    (body - ragWorld[i].Translation).Length() < 0.01f,
                    $"{path}: body '{instance.m_rigidBodies[i].m_name}' is not on its bone");
            }
        }
    }

    /// <summary>
    /// A rig without one of the prototype's ragdoll bones loses that bone and the constraint
    /// that held it, and keeps everything else: a skeleton with no jaw is still a skeleton.
    /// </summary>
    [CorpusFact]
    public void ABoneTheRigHasNotGotIsLeftBehindWithItsConstraint()
    {
        hkaSkeleton rig = Rig(Skeletons);
        int jaw = -1;
        for (int i = 0; i < rig.m_bones.Count; i++)
            if (rig.m_bones[i].m_name.Contains("Jaw", StringComparison.OrdinalIgnoreCase)) jaw = i;

        Assert.True(jaw >= 0, "the bone skeleton's rig is expected to have a jaw");

        // Renamed out of the way, as a rig that never had one would be.
        rig.m_bones[jaw].m_name = "NPC Something Else";

        RetargetedRagdoll made = RagdollRetarget.Fit(Skeletons, rig);

        Assert.Equal(18, made.Bodies);
        Assert.Contains(made.Dropped, d => d.Contains("Jaw", StringComparison.OrdinalIgnoreCase));

        var instance = made.File.All<hkaRagdollInstance>().First();
        Assert.DoesNotContain(instance.m_rigidBodies, b => b.m_name!.Contains("Jaw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(instance.m_constraints, c => c.m_name!.Contains("Jaw", StringComparison.OrdinalIgnoreCase));

        // One body needs one constraint fewer than there are bodies: the root has no parent.
        Assert.Equal(made.Bodies - 1, instance.m_constraints.Count);
        Assert.Equal(made.Bodies, instance.m_skeleton!.m_bones.Count);
    }

    /// <summary>
    /// The bone skeleton's ragdoll is not the fleshed draugr's, and the difference is the
    /// reason a prototype has to be chosen rather than assumed: a skeleton's hips, shoulders
    /// and head are hinges where a draugr's are three-degree joints, and it has a jaw.
    /// </summary>
    [CorpusFact]
    public void TheBoneSkeletonsRagdollIsHingedWhereTheDraugrsIsNot()
    {
        static Dictionary<string, string> Joints(string path)
        {
            var instance = HavokFile.Load(path).All<hkaRagdollInstance>().First();
            return instance.m_constraints.ToDictionary(
                c => RagdollRetarget.Animation(c.m_name ?? ""),
                c => c.m_data!.GetType().Name);
        }

        var bone = Joints(Skeletons);
        var flesh = Joints(Draugr);

        foreach (string joint in new[] { "NPC L Thigh [LThg]", "NPC L UpperArm [LUar]", "NPC Head [Head]" })
        {
            Assert.Equal("hkpLimitedHingeConstraintData", bone[joint]);
            Assert.Equal("hkpRagdollConstraintData", flesh[joint]);
        }

        // The spine is three-degree on both: a ribcage bends whether or not it has flesh on it.
        Assert.Equal("hkpRagdollConstraintData", bone["NPC Spine1 [Spn1]"]);
        Assert.Equal("hkpRagdollConstraintData", flesh["NPC Spine1 [Spn1]"]);

        Assert.Contains(bone.Keys, k => k.Contains("Jaw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(flesh.Keys, k => k.Contains("Jaw", StringComparison.OrdinalIgnoreCase));
    }
}
