using System.Text;
using HKFBX.Fbx;
using HKFBX.Model;
using HKSK.Assembly;
using LeanMeshIO;
using Xunit;
namespace HKSK.Tests;

// A folder of clips read the way a front end would read it: what each animation is,
// how fast its feet say it goes, and what creature the lot of them make.
public sealed class ZzReadCreature
{
    [Fact]
    public void Read()
    {
        string path = Environment.GetEnvironmentVariable("CLIPS_FBX") ?? "";
        string feet = Environment.GetEnvironmentVariable("FEET") ?? "";
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        if (!File.Exists(path)) return;

        var document = FbxDocument.Load(path);
        Skeleton rig = FbxAnimationReader.ReadSkeleton(document);
        string[] toes = feet.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        var roled = new List<RoledAnimation>();

        foreach (string take in FbxAnimationReader.ReadTakeNames(document))
        {
            ReadRole read = RoleReader.Of(take);
            SampledAnimation clip = FbxAnimationReader.ReadAnimation(document, rig, takeName: take);
            InferredMotion? motion = toes.Length > 0 ? FootMotion.Infer(clip, rig, toes) : null;

            string what = read.Roles.Count == 0 ? "not recognised" : string.Join("; ", read.Roles);
            string moves = motion is null ? "nothing planted"
                : $"{motion.Speed,6:F0} u/s at {motion.Heading,5:F0} deg, {motion.Confidence:P0} sure";

            sb.AppendLine($"{take,-16} {clip.FrameCount,4} frames {clip.Duration,6:F2}s  {what,-42} {moves}");

            if (read.Roles.Count > 0)
                roled.Add(new RoledAnimation($"{take}.hkx", read.Roles,
                    Speed: motion is { Confidence: > 0.5f, Speed: > 1f } ? motion.Speed : null));
        }

        CreaturePlan plan = CreaturePlanner.Of(roled);
        sb.AppendLine();
        sb.AppendLine($"== {roled.Count} of {FbxAnimationReader.ReadTakeNames(document).Count} takes were recognised");
        sb.AppendLine($"== {plan}");
        foreach (string note in plan.Notes) sb.AppendLine($"   {note}");
        foreach (string made in plan.Synthesised) sb.AppendLine($"   made: {made}");
        foreach (string no in plan.Refusals) sb.AppendLine($"   refused: {no}");

        File.WriteAllText(Path.Combine(outDir, "read_creature.txt"), sb.ToString());
    }
}
