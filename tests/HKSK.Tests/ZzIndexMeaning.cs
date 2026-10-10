using System.Text;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

// What the cache index is an index INTO. The engine looks a clip up by name, reads a signed
// 16-bit number off it, bounds-checks it against a count and uses it to index the project's
// movement array. So: is that array one entry per animation slot, or dense over clips?
public sealed class ZzIndexMeaning
{
    [CorpusFact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT")
            ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hksk-dumps")).FullName;

        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var sb = new StringBuilder();
        int contiguous = 0, cached = 0, beyond16 = 0;

        sb.AppendLine("project                        anims  clips  movs  movIdx range  contiguous  maxIdx");
        foreach (ActorProject project in cache.Actors())
        {
            var movs = project.Data.Movements?.Movements ?? [];
            if (movs.Count == 0) continue;
            cached++;

            var indices = movs.Select(m => m.CacheIndex).ToList();
            int lo = indices.Min(), hi = indices.Max();
            bool dense = indices.Count == hi - lo + 1 && indices.Distinct().Count() == indices.Count;
            if (dense && lo == 0) contiguous++;

            int anims = project.Character?.AnimationNames.Count ?? -1;
            int maxClip = project.Data.Block.Clips.Count == 0 ? -1 : project.Data.Block.Clips.Max(c => c.CacheIndex);
            if (hi > short.MaxValue || maxClip > short.MaxValue) beyond16++;

            if (cached <= 14 || anims != hi + 1)
                sb.AppendLine($"{project.Name,-30} {anims,5}  {project.Data.Block.Clips.Count,5}  " +
                              $"{movs.Count,4}  {lo,4}..{hi,-5}  {(dense && lo == 0 ? "yes" : "NO "),-10}  {maxClip,5}" +
                              (anims == hi + 1 ? "" : "   <- movements do not match the animation count"));
        }

        sb.AppendLine();
        sb.AppendLine($"{cached} projects carry movements; {contiguous} have them numbered 0..n-1 with no gaps");
        sb.AppendLine($"{beyond16} have an index above a signed 16-bit maximum ({short.MaxValue})");

        File.WriteAllText(Path.Combine(outDir, "index-meaning.txt"), sb.ToString());
    }
}
