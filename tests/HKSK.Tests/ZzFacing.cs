using System.Numerics;
using System.Text;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// Which way a creature faces, read off its own feet: a toe is in front of the ankle it hangs
// from, so the sign of that offset is the direction the creature looks in.
public sealed class ZzFacing
{
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

    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string built = Environment.GetEnvironmentVariable("HKSK_BUILT") ?? "";
        if (corpus.Length == 0) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();

        var sb = new StringBuilder();

        foreach ((string what, string path) in new[]
                 {
                     ("draugr", Path.Combine(corpus, "actors", "draugr", "character assets", "skeleton.hkx")),
                     ("bone skeleton", Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx")),
                     ("human", Path.Combine(corpus, "actors", "character", "character assets", "skeleton.hkx")),
                     ("ours", built),
                 })
        {
            if (!File.Exists(path)) { sb.AppendLine($"{what}: absent"); continue; }

            hkaSkeleton rig = HavokFile.Load(path).All<hkaAnimationContainer>().First().m_skeletons[0];
            Matrix4x4[] world = World(rig);

            int At(params string[] names)
            {
                foreach (string name in names)
                    for (int i = 0; i < rig.m_bones.Count; i++)
                        if (rig.m_bones[i].m_name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }

            int foot = At("NPC L Foot"), toe = At("NPC L Toe0"), head = At("NPC Head [Head]"), com = At("NPC COM");
            if (foot < 0 || toe < 0) { sb.AppendLine($"{what}: no left foot and toe"); continue; }

            Vector3 ankle = world[foot].Translation, tip = world[toe].Translation;
            sb.AppendLine($"{what}:");
            sb.AppendLine($"  L ankle ({ankle.X,7:0.##},{ankle.Y,7:0.##},{ankle.Z,7:0.##})  " +
                          $"L toe ({tip.X,7:0.##},{tip.Y,7:0.##},{tip.Z,7:0.##})");
            sb.AppendLine($"  toe is {(tip.Y - ankle.Y):+0.##;-0.##} along Y from the ankle -> faces {(tip.Y > ankle.Y ? "+Y" : "-Y")}");
            if (head >= 0 && com >= 0)
                sb.AppendLine($"  height: COM {world[com].Translation.Z:0.#}, head {world[head].Translation.Z:0.#}");
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outDir, "facing.txt"), sb.ToString());
    }
}
