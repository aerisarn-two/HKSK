using System.Text;
using HKFBX.Fbx;
using HKFBX.Model;
using LeanMeshIO;
using Xunit;

namespace HKSK.Tests;

// Cuts the merged take again, with the ranges read the way the table means them.
public sealed class ZzRecut
{
    [Fact]
    public void Dump()
    {
        string source = Environment.GetEnvironmentVariable("HKSK_MERGED") ?? "";
        string table = Environment.GetEnvironmentVariable("HKSK_CUTS") ?? "";
        if (!File.Exists(source) || !File.Exists(table)) return;

        // Never the bare temp root: a skeleton.hkx or a skeleton.nif left there makes every
        // folder-scanning route read the whole of it as a creature.
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT")
            ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hksk-creature-dump")).FullName;

        var cuts = File.ReadAllLines(table)
            .Select(line => line.Split('\t'))
            .Where(f => f.Length >= 3)
            .Select(f => new TakeCut(f[0].Trim(), int.Parse(f[1]), int.Parse(f[2])))
            .ToList();

        FbxDocument document = FbxDocument.Load(source);
        Skeleton rig = FbxAnimationReader.ReadSkeleton(document);

        var sb = new StringBuilder();
        sb.AppendLine($"{cuts.Count} cuts, touching = {FbxTakeCutter.Touching(cuts)}");

        CutReport report = FbxTakeCutter.Cut(document, cuts, rig: rig);
        sb.AppendLine($"end frame kept: {report.EndIsInside}");
        foreach (CutStack cut in report.Cuts)
            sb.AppendLine($"  {cut.Name,-16} {cut.Frames,5} frames  {cut.Duration:0.##}s  {cut.BoundBones} bones");
        foreach ((string name, string why) in report.Refused) sb.AppendLine($"  !! {name}: {why}");

        string outPath = Path.Combine(outDir, "recut.fbx");
        document.Save(outPath);
        sb.AppendLine($"written to {outPath}");

        File.WriteAllText(Path.Combine(outDir, "recut.txt"), sb.ToString());
    }
}
