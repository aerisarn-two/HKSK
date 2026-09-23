using System.Numerics;
using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;

// How a ragdoll sits on its rig: which way round the mapper's aFromBTransform goes, and
// whether a rigid body's own transform is just its ragdoll bone's world pose. Both have to
// be known before a ragdoll can be moved onto another skeleton.
public sealed class ZzRagdollFit
{
    // HKX2 keeps an hkQsTransform in a Matrix4x4: translation in the first row, the
    // rotation quaternion in the second, scale in the third. It is a container, not a
    // matrix, so it has to be unpacked before anything is multiplied.
    private static Matrix4x4 Real(Matrix4x4 qs) =>
        Matrix4x4.CreateScale(new Vector3(qs.M31, qs.M32, qs.M33))
        * Matrix4x4.CreateFromQuaternion(new Quaternion(qs.M21, qs.M22, qs.M23, qs.M24))
        * Matrix4x4.CreateTranslation(new Vector3(qs.M11, qs.M12, qs.M13));

    private static Matrix4x4[] World(hkaSkeleton rig)
    {
        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            Matrix4x4 local = Real(rig.m_referencePose[i]);
            short parent = rig.m_parentIndices[i];
            world[i] = parent < 0 ? local : local * world[parent];
        }
        return world;
    }

    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (corpus.Length == 0) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        var sb = new StringBuilder();

        foreach (string which in new[] { "skeleton.hkx", "skeletons.hkx" })
        {
            var f2 = HavokFile.Load(Path.Combine(corpus, "actors", "draugr", "character assets", which));
            var c2 = f2.All<hkaAnimationContainer>().First();
            sb.AppendLine($"-- {which}");
            sb.AppendLine("   rig feet: " + string.Join(" | ", c2.m_skeletons[0].m_bones.Select(b => b.m_name).Where(n => n.Contains("Foot") || n.Contains("Jaw"))));
            sb.AppendLine("   rag feet: " + string.Join(" | ", c2.m_skeletons[1].m_bones.Select(b => b.m_name).Where(n => n.Contains("Foot") || n.Contains("Jaw"))));
        }
        sb.AppendLine();

        string path = Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx");
        var file = HavokFile.Load(path);
        var container = file.All<hkaAnimationContainer>().First();
        hkaSkeleton anim = container.m_skeletons[0], rag = container.m_skeletons[1];
        var ragdoll = file.All<hkaRagdollInstance>().First();
        var mapper = file.All<hkaSkeletonMapper>().First(m => m.m_mapping?.m_skeletonA == anim);

        Matrix4x4[] animWorld = World(anim), ragWorld = World(rag);

        sb.AppendLine($"mapper A='{mapper.m_mapping!.m_skeletonA!.m_name}' B='{mapper.m_mapping.m_skeletonB!.m_name}' " +
                      $"type={mapper.m_mapping.m_mappingType} keepUnmappedLocal={mapper.m_mapping.m_keepUnmappedLocal}");
        sb.AppendLine($"boneToRigidBodyMap: [{string.Join(", ", ragdoll.m_boneToRigidBodyMap)}]");
        sb.AppendLine();

        static string P(Matrix4x4 m) => $"({m.Translation.X,8:0.##},{m.Translation.Y,8:0.##},{m.Translation.Z,8:0.##})";

        // Is aFromB identity for every mapping, and does the ragdoll bone sit exactly where
        // the animation bone sits -- rotation as well as position?
        float worstT = 0, worstR = 0, worstM = 0, worstBody = 0;
        foreach (var map in mapper.m_mapping.m_simpleMappings)
        {
            Matrix4x4 qs = map.m_aFromBTransform;
            worstM = Math.Max(worstM, new Vector3(qs.M11, qs.M12, qs.M13).Length());
            worstM = Math.Max(worstM, Math.Abs(1f - new Quaternion(qs.M21, qs.M22, qs.M23, qs.M24).W));
            Matrix4x4 a = animWorld[map.m_boneA], b = ragWorld[map.m_boneB];
            worstT = Math.Max(worstT, (a.Translation - b.Translation).Length());
            worstR = Math.Max(worstR, (Quaternion.CreateFromRotationMatrix(a) - Quaternion.CreateFromRotationMatrix(b)).Length());
            var st = ragdoll.m_rigidBodies[map.m_boneB]?.m_motion?.m_motionState;
            if (st is not null) worstBody = Math.Max(worstBody, (st.m_transform.Translation - b.Translation).Length());
        }
        sb.AppendLine($"worst aFromB from identity: {worstM:0.######}");
        sb.AppendLine($"worst ragdoll bone vs animation bone: {worstT:0.######} units, {worstR:0.######} quat");
        sb.AppendLine($"worst body transform vs ragdoll bone: {worstBody:0.######} units");
        sb.AppendLine();

        // The ragdoll skeleton's own shape, and how its names relate to the rig's.
        for (int i = 0; i < rag.m_bones.Count; i++)
            sb.AppendLine($"  rag[{i,2}] parent={rag.m_parentIndices[i],2} {rag.m_bones[i].m_name}");
        sb.AppendLine();

        foreach (var map in mapper.m_mapping.m_simpleMappings.Take(3))
        {
            Matrix4x4 a = animWorld[map.m_boneA], b = ragWorld[map.m_boneB], m = Real(map.m_aFromBTransform);
            Matrix4x4.Invert(m, out Matrix4x4 mi);

            sb.AppendLine($"A[{map.m_boneA}]={anim.m_bones[map.m_boneA].m_name}  B[{map.m_boneB}]={rag.m_bones[map.m_boneB].m_name}");
            sb.AppendLine($"  animWorld {P(a)}   ragWorld {P(b)}   aFromB {P(m)}");
            sb.AppendLine($"  b*m      {P(m * b)}    m*b {P(b * m)}    b*mi {P(mi * b)}   mi*b {P(b * mi)}");

            // A rigid body's own transform, against its ragdoll bone's world pose.
            var body = ragdoll.m_rigidBodies[map.m_boneB];
            var state = body?.m_motion?.m_motionState;
            if (state is not null)
                sb.AppendLine($"  body '{body!.m_name}' transform {P(state.m_transform)}");
            sb.AppendLine();
        }

        string outPath = Path.Combine(outDir, "ragdollfit.txt");
        File.WriteAllText(outPath, sb.ToString());
    }
}
