using System.Text;
using HKFBX.Model;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// Which way is forward. A shipped clip that is known to walk forwards, read the same way the
// bought ones are, says what heading the inference calls forward -- and so whether the bought
// creature is authored facing the way Skyrim expects.
public sealed class ZzHeading
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (corpus.Length == 0) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();

        string folder = Path.Combine(corpus, "actors", "draugr");
        var file = HavokFile.Load(Path.Combine(folder, "character assets", "skeleton.hkx"));
        hkaSkeleton havok = file.All<hkaAnimationContainer>().First().m_skeletons[0];

        var rig = new Skeleton
        {
            Name = havok.m_name,
            Bones = [.. havok.m_bones.Select((b, i) => new Bone(
                b.m_name,
                havok.m_parentIndices[i],
                new BoneTransform(
                    new System.Numerics.Vector3(
                        havok.m_referencePose[i].M11, havok.m_referencePose[i].M12, havok.m_referencePose[i].M13),
                    new System.Numerics.Quaternion(
                        havok.m_referencePose[i].M21, havok.m_referencePose[i].M22,
                        havok.m_referencePose[i].M23, havok.m_referencePose[i].M24),
                    new System.Numerics.Vector3(
                        havok.m_referencePose[i].M31, havok.m_referencePose[i].M32, havok.m_referencePose[i].M33)))),
            ],
        };

        var sb = new StringBuilder();
        sb.AppendLine($"draugr rig: {rig.Count} bones");
        sb.AppendLine($"feet present: {string.Join(", ", CreatureClips.Feet.Where(f => rig.Bones.Any(b => string.Equals(b.Name, f, StringComparison.OrdinalIgnoreCase))))}");
        sb.AppendLine();

        foreach (string name in new[]
                 {
                     "1hmforward", "1hmfastforward", "1hmbackward", "1hmforwardleft", "1hmforwardright",
                     "1hmidle",
                 })
        {
            string path = Path.Combine(folder, "animations", name + ".hkx");
            if (!File.Exists(path)) { sb.AppendLine($"{name}: absent"); continue; }

            SampledAnimation? clip = UncompressedAnimation.Read(path);
            if (clip is null) { sb.AppendLine($"{name}: not readable uncompressed"); continue; }

            InferredMotion? motion = FootMotion.Infer(clip, rig, CreatureClips.Feet);
            sb.AppendLine($"{name}: {clip.FrameCount} frames, {clip.Duration:0.##}s -> {motion?.ToString() ?? "standing still"}");
        }

        File.WriteAllText(Path.Combine(outDir, "heading.txt"), sb.ToString());
    }
}
