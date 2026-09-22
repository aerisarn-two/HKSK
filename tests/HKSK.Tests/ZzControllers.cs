using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Every creature's character controller capsule, beside how tall its rig stands.
public sealed class ZzControllers
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        if (corpus.Length == 0) return;
        var sb = new StringBuilder();

        foreach (string path in Directory.EnumerateFiles(corpus, "*.hkx", SearchOption.AllDirectories).Order())
        {
            if (!path.Contains("characters", StringComparison.OrdinalIgnoreCase)) continue;
            hkbCharacterData? data;
            try { data = HavokFile.Load(path).First<hkbCharacterData>(); } catch { continue; }
            if (data?.m_characterControllerInfo is not { } info) continue;

            string folder = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
            string? rig = Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase));
            float top = 0f;
            if (rig is not null)
                try
                {
                    var file = HKFBX.Hkx.HkxSkeletonFile.Read(rig);
                    var world = new System.Numerics.Matrix4x4[file.Rig.Count];
                    for (int i = 0; i < world.Length; i++)
                    {
                        var p = file.Rig.Bones[i].ReferencePose;
                        var local = System.Numerics.Matrix4x4.CreateFromQuaternion(p.Rotation) * System.Numerics.Matrix4x4.CreateTranslation(p.Translation);
                        int parent = file.Rig.Bones[i].ParentIndex;
                        world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
                    }
                    top = world.Max(w => w.Translation.Z);
                }
                catch { }

            sb.AppendLine($"{Path.GetRelativePath(corpus, path),-70} capsule h={info.m_capsuleHeight,-8:F3} r={info.m_capsuleRadius,-8:F3} filter=0x{info.m_collisionFilterInfo:x} scale={data.m_scale} rig top={top,7:F1} units, h*70={info.m_capsuleHeight * 70f,7:F1}");
        }

        File.WriteAllText(Path.Combine(outDir, "controllers.txt"), sb.ToString());
    }
}
