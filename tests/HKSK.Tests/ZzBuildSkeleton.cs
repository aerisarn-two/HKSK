using System.Numerics;
using System.Text;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// The whole skeleton route against the bought asset: read its rig, stand it on the floor, put
// it under the game's names, and fit the bone skeleton's ragdoll to it.
public sealed class ZzBuildSkeleton
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string asset = Environment.GetEnvironmentVariable("HKSK_ASSET") ?? "";
        if (corpus.Length == 0 || !File.Exists(asset)) return;

        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        string built = Path.Combine(outDir, "skeleton.hkx");
        var sb = new StringBuilder();

        AssembledSkeleton made = CreatureSkeleton.Build(
            asset,
            Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx"),
            built);

        sb.AppendLine(made.ToString());
        sb.AppendLine();
        sb.AppendLine($"grounded: lift={made.Grounded?.Lift:0.##} lowest={made.Grounded?.Lowest:0.##} rootAdded={made.Grounded?.RootAdded}");
        sb.AppendLine($"renamed: {made.Renamed}");
        sb.AppendLine($"  unmapped: {string.Join(", ", made.Renamed.Unmapped)}");
        sb.AppendLine($"ragdoll: {made.Ragdoll}");
        sb.AppendLine();

        // Read it back the way anything else would.
        var file = HavokFile.Load(built);
        var container = file.All<hkaAnimationContainer>().First();
        hkaSkeleton rig = container.m_skeletons[0], ragdoll = container.m_skeletons[1];
        var instance = file.All<hkaRagdollInstance>().First();

        sb.AppendLine($"read back: rig '{rig.m_name}' {rig.m_bones.Count} bones, ragdoll {ragdoll.m_bones.Count} bones, " +
                      $"{instance.m_rigidBodies.Count} bodies, {instance.m_constraints.Count} constraints");
        sb.AppendLine();

        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            Matrix4x4 local = RagdollRetarget.Real(rig.m_referencePose[i]);
            short parent = rig.m_parentIndices[i];
            world[i] = parent < 0 ? local : local * world[parent];
        }

        sb.AppendLine($"rig stands from {world.Min(m => m.Translation.Z):0.##} to {world.Max(m => m.Translation.Z):0.##}");
        sb.AppendLine();

        foreach (var bone in rig.m_bones.Select((b, i) => (b, i)))
            sb.AppendLine($"  [{bone.i,2}] parent={rig.m_parentIndices[bone.i],2} z={world[bone.i].Translation.Z,7:0.##} {bone.b.m_name}");

        sb.AppendLine();
        foreach (var body in instance.m_rigidBodies)
        {
            string shape = body.m_collidable?.m_shape switch
            {
                hkpCapsuleShape c => $"capsule r={c.m_radius:0.##}",
                hkpConvexVerticesShape h => $"hull ({h.m_numVertices} points)",
                var s => s?.GetType().Name ?? "none",
            };
            sb.AppendLine($"  body '{body.m_name}' at z={body.m_motion.m_motionState.m_transform.Translation.Z:0.##} {shape}");
        }

        File.WriteAllText(Path.Combine(outDir, "built-skeleton.txt"), sb.ToString());
    }
}
