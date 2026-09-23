using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;

// What a skeleton packfile actually carries, for the draugr's three: the animation rig,
// the ragdoll rig, the bodies and constraints under it, and the two mappings between.
// The bone creature is the one in question -- whether it has a ragdoll of its own.
public sealed class ZzRagdolls
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (corpus.Length == 0) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        var sb = new StringBuilder();

        string[] files =
        [
            Path.Combine(corpus, "actors", "draugr", "character assets", "skeleton.hkx"),
            Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx"),
            Path.Combine(corpus, "actors", "draugr", "character assets", "skeletonf.hkx"),
            Path.Combine(corpus, "actors", "character", "character assets", "skeleton.hkx"),
        ];

        foreach (string path in files)
        {
            sb.AppendLine($"=== {Path.GetRelativePath(corpus, path)}");
            if (!File.Exists(path)) { sb.AppendLine("  (absent)"); continue; }
            HavokFile file;
            try { file = HavokFile.Load(path); } catch (Exception e) { sb.AppendLine($"  ! {e.Message}"); continue; }

            foreach (var v in file.Root.m_namedVariants)
                sb.AppendLine($"  variant {v.m_name} : {v.m_className}");

            foreach (var container in file.All<hkaAnimationContainer>())
            {
                sb.AppendLine($"  hkaAnimationContainer: {container.m_skeletons.Count} skeletons, " +
                              $"{container.m_animations.Count} animations, {container.m_bindings.Count} bindings");
                foreach (var rig in container.m_skeletons)
                    sb.AppendLine($"    skeleton '{rig.m_name}': {rig.m_bones.Count} bones, " +
                                  $"{rig.m_floatSlots.Count} float slots, root[0]={(rig.m_bones.Count > 0 ? rig.m_bones[0].m_name : "-")}");
            }

            foreach (var physics in file.All<hkpPhysicsSystem>())
                sb.AppendLine($"  hkpPhysicsSystem '{physics.m_name}': {physics.m_rigidBodies.Count} bodies, " +
                              $"{physics.m_constraints.Count} constraints, {physics.m_phantoms.Count} phantoms");

            foreach (var rag in file.All<hkaRagdollInstance>())
                sb.AppendLine($"  hkaRagdollInstance: {rag.m_rigidBodies.Count} bodies, {rag.m_constraints.Count} constraints, " +
                              $"skeleton='{rag.m_skeleton?.m_name}'");

            foreach (var map in file.All<hkaSkeletonMapper>())
            {
                var d = map.m_mapping;
                sb.AppendLine($"  hkaSkeletonMapper: '{d?.m_skeletonA?.m_name}' -> '{d?.m_skeletonB?.m_name}', " +
                              $"{d?.m_simpleMappings.Count} simple, {d?.m_chainMappings.Count} chain, " +
                              $"{d?.m_unmappedBones.Count} unmapped");
            }

            foreach (var rag in file.All<hkaRagdollInstance>())
            {
                for (int i = 0; i < rag.m_rigidBodies.Count; i++)
                {
                    var body = rag.m_rigidBodies[i];
                    string bone = i < (rag.m_skeleton?.m_bones.Count ?? 0) ? rag.m_skeleton!.m_bones[i].m_name : "?";
                    sb.AppendLine($"    [{i,2}] bone={bone} body='{body?.m_name}' shape={Shape(body)}");
                }
                for (int i = 0; i < rag.m_constraints.Count; i++)
                {
                    var c = rag.m_constraints[i];
                    sb.AppendLine($"    con[{i,2}] '{c?.m_name}' {c?.m_data?.GetType().Name} " +
                                  $"A='{(c?.m_entities is { Length: > 0 } e ? (e[0] as hkpRigidBody)?.m_name : null)}' " +
                                  $"B='{(c?.m_entities is { Length: > 1 } f ? (f[1] as hkpRigidBody)?.m_name : null)}'");
                }
            }

            var other = file.All<IHavokObject>()
                .Select(o => o.GetType().Name)
                .Where(n => n.StartsWith("hkp") || n.StartsWith("hkb") || n.StartsWith("BS"))
                .GroupBy(n => n).OrderByDescending(g => g.Count());
            sb.AppendLine("  classes: " + string.Join(", ", other.Select(g => $"{g.Key}x{g.Count()}")));
            sb.AppendLine();
        }

        string Shape(hkpRigidBody? body)
        {
            var shape = body?.m_collidable?.m_shape;
            return shape switch
            {
                hkpCapsuleShape cap => $"capsule r={cap.m_radius:0.##} len={System.Numerics.Vector3.Distance(new(cap.m_vertexA.X, cap.m_vertexA.Y, cap.m_vertexA.Z), new(cap.m_vertexB.X, cap.m_vertexB.Y, cap.m_vertexB.Z)):0.##}",
                hkpConvexVerticesShape hull => $"convex hull, radius={hull.m_radius:0.##}",
                null => "(none)",
                _ => shape.GetType().Name,
            };
        }

        string outPath = Path.Combine(outDir, "ragdolls.txt");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine(outPath);
    }
}
