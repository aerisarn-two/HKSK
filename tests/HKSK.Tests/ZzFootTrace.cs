using System.Numerics;
using System.Text;
using HKFBX.Fbx;
using HKFBX.Model;
using LeanMeshIO;
using Xunit;

namespace HKSK.Tests;

// Frame by frame through a clip's feet: where each one is, which the inference calls planted,
// and which way it slides. A clip whose measured travel disagrees with what its author says
// it does is either a bad clip or a bad reading, and this says which.
public sealed class ZzFootTrace
{
    [Fact]
    public void Dump()
    {
        string clips = Environment.GetEnvironmentVariable("HKSK_CLIPS") ?? "";
        if (!File.Exists(clips)) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();

        FbxDocument document = FbxDocument.Load(clips);
        Skeleton rig = FbxAnimationReader.ReadSkeleton(document);

        int Bone(string name)
        {
            for (int i = 0; i < rig.Count; i++)
                if (string.Equals(rig.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        int left = Bone("Bip01_L_Toe0"), right = Bone("Bip01_R_Toe0");
        var sb = new StringBuilder();
        sb.AppendLine($"left toe = bone {left}, right toe = bone {right}");

        sb.AppendLine("--- how far each foot rises, per clip, against the creature's height");
        foreach (string take in FbxAnimationReader.ReadTakeNames(document))
        {
            SampledAnimation c = FbxAnimationReader.ReadAnimation(document, rig, takeName: take);
            if (c.FrameCount < 2) continue;

            var w = new Matrix4x4[c.FrameCount][];
            for (int f = 0; f < c.FrameCount; f++) w[f] = Pose(c, rig, f);

            float tall = Enumerable.Range(0, c.FrameCount)
                .SelectMany(f => w[f].Select(m => m.Translation.Z)).Max()
                - Enumerable.Range(0, c.FrameCount).SelectMany(f => w[f].Select(m => m.Translation.Z)).Min();

            float RiseOf(int b) => Enumerable.Range(0, c.FrameCount).Max(f => w[f][b].Translation.Z)
                                 - Enumerable.Range(0, c.FrameCount).Min(f => w[f][b].Translation.Z);

            float l = RiseOf(left), r = RiseOf(right);
            sb.AppendLine($"  {take,-16} height {tall,6:0.#}  left rises {l,6:0.##} ({l / tall,6:P1})  " +
                          $"right {r,6:0.##} ({r / tall,6:P1})");
        }
        sb.AppendLine();

        foreach (string take in new[] { "Walk01", "WalkBack", "WalkLeft" })
        {
            SampledAnimation clip = FbxAnimationReader.ReadAnimation(document, rig, takeName: take);
            var world = new Matrix4x4[clip.FrameCount][];
            for (int f = 0; f < clip.FrameCount; f++) world[f] = Pose(clip, rig, f);

            float Z(int bone, int f) => world[f][bone].Translation.Z;
            Vector3 At(int bone, int f) => world[f][bone].Translation;

            float lowL = Enumerable.Range(0, clip.FrameCount).Min(f => Z(left, f));
            float highL = Enumerable.Range(0, clip.FrameCount).Max(f => Z(left, f));
            float lowR = Enumerable.Range(0, clip.FrameCount).Min(f => Z(right, f));
            float highR = Enumerable.Range(0, clip.FrameCount).Max(f => Z(right, f));

            sb.AppendLine();
            sb.AppendLine($"=== {take}: {clip.FrameCount} frames");
            sb.AppendLine($"  left toe rises {lowL:0.##} to {highL:0.##} ({highL - lowL:0.##}), " +
                          $"right toe {lowR:0.##} to {highR:0.##} ({highR - lowR:0.##})");
            sb.AppendLine($"  ceilings at 0.35 of rise: left {lowL + (highL - lowL) * 0.35f:0.##}, " +
                          $"right {lowR + (highR - lowR) * 0.35f:0.##}");
            sb.AppendLine("  frame   Lz     Rz   down   slide(x,y)   |slide|");

            int previous = -1;
            var slides = new List<Vector3>();
            float ceilL = lowL + (highL - lowL) * 0.35f, ceilR = lowR + (highR - lowR) * 0.35f;

            for (int f = 1; f < clip.FrameCount; f++)
            {
                float hL = (Z(left, f) + Z(left, f - 1)) / 2f, hR = (Z(right, f) + Z(right, f - 1)) / 2f;
                int down = hL <= hR ? left : right;
                float height = Math.Min(hL, hR), ceiling = down == left ? ceilL : ceilR;

                string mark;
                Vector3 slide = Vector3.Zero;
                if (height > ceiling) { mark = "  -  "; previous = -1; }
                else if (down != previous) { mark = down == left ? " L^  " : " R^  "; previous = down; }
                else
                {
                    slide = At(down, f) - At(down, f - 1);
                    slide.Z = 0;
                    slides.Add(-slide);
                    mark = down == left ? " L   " : " R   ";
                }

                sb.AppendLine($"  {f,5} {Z(left, f),6:0.#} {Z(right, f),6:0.#}  {mark} " +
                              $"({-slide.X,6:0.##},{-slide.Y,6:0.##}) {slide.Length(),7:0.##}");
            }

            Vector3 summed = slides.Aggregate(Vector3.Zero, (a, b) => a + b);
            float seen = slides.Count / 30f;
            float scalar = slides.Sum(s => s.Length());

            sb.AppendLine($"  {slides.Count} sliding frames");
            sb.AppendLine($"  summed as a vector: {summed.Length():0.##} units -> {(seen > 0 ? summed.Length() / seen : 0):0.#} a second");
            sb.AppendLine($"  summed as lengths:  {scalar:0.##} units -> {(seen > 0 ? scalar / seen : 0):0.#} a second");
        }

        // Is the last frame of one clip the first frame of the next? If the cut ranges are
        // half-open and were read as inclusive, every clip ends on a pose from its neighbour.
        sb.AppendLine();
        sb.AppendLine("--- does each clip end on its neighbour's first frame?");

        var order = new[]
        {
            "Walk01", "Walk02", "WalkBack", "WalkLeft", "WalkRight",
            "Death", "Hit", "Hit2", "Idle",
        };

        var first = new Dictionary<string, BoneTransform[]>();
        var last = new Dictionary<string, BoneTransform[]>();
        var second = new Dictionary<string, BoneTransform[]>();

        foreach (string take in order)
        {
            SampledAnimation clip = FbxAnimationReader.ReadAnimation(document, rig, takeName: take);
            BoneTransform[] Frame(int f) =>
                [.. Enumerable.Range(0, clip.TrackCount).Select(t => clip[f, t])];

            first[take] = Frame(0);
            last[take] = Frame(clip.FrameCount - 1);
            second[take] = Frame(clip.FrameCount - 2);
        }

        float Apart(BoneTransform[] a, BoneTransform[] b) =>
            Enumerable.Range(0, Math.Min(a.Length, b.Length))
                .Sum(i => (a[i].Translation - b[i].Translation).Length());

        // clips.tsv, in the order it lists them, so each pair is a real neighbour.
        foreach ((string ends, string starts) in new[]
                 {
                     ("Walk01", "Walk02"), ("Walk02", "WalkBack"),
                     ("WalkBack", "WalkLeft"), ("WalkLeft", "WalkRight"),
                     ("Death", "Hit"), ("Hit", "Hit2"), ("Hit2", "Idle"),
                 })
        {
            sb.AppendLine($"  {ends} last vs {starts} first: {Apart(last[ends], first[starts]):0.####}   " +
                          $"(vs its own previous frame: {Apart(last[ends], second[ends]):0.####})");
        }

        File.WriteAllText(Path.Combine(outDir, "foottrace.txt"), sb.ToString());
    }

    private static Matrix4x4[] Pose(SampledAnimation animation, Skeleton skeleton, int frame)
    {
        var world = new Matrix4x4[skeleton.Count];
        for (int bone = 0; bone < skeleton.Count; bone++)
        {
            BoneTransform local = bone < animation.TrackCount
                ? animation[frame, bone]
                : skeleton.Bones[bone].ReferencePose;
            Matrix4x4 at = Matrix4x4.CreateScale(local.Scale)
                * Matrix4x4.CreateFromQuaternion(local.Rotation)
                * Matrix4x4.CreateTranslation(local.Translation);
            int parent = skeleton.Bones[bone].ParentIndex;
            world[bone] = parent < 0 ? at : at * world[parent];
        }
        return world;
    }
}
