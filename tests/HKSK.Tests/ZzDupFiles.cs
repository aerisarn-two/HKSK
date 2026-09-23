using System.Text;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

// Do any of the game's own projects name the same file twice in their file list?
public sealed class ZzDupFiles
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (corpus.Length == 0) return;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();

        SkyrimCache cache = SkyrimCache.Load(corpus);
        var sb = new StringBuilder();

        int withDup = 0, creatures = 0;
        foreach (var p in cache.AnimationData.Projects)
        {
            var files = p.Block.Files;
            if (files.Count == 0) continue;
            creatures++;

            var dups = files
                .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (dups.Count == 0) continue;
            withDup++;
            sb.AppendLine($"{p.Stem}: {string.Join(", ", dups.Select(d => $"'{d.Key}' x{d.Count()}"))}");
        }

        sb.Insert(0, $"{withDup} of {creatures} projects with files name something twice\n\n");

        // Does any project name its own project file, and what comes first in a list?
        int namesItself = 0;
        var firstFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<int, int>();

        foreach (var p in cache.AnimationData.Projects)
        {
            var files = p.Block.Files;
            if (files.Count == 0) continue;

            if (files.Any(f => f.EndsWith("Project.hkx", StringComparison.OrdinalIgnoreCase))) namesItself++;

            string head = files[0].Contains('\\') ? files[0].Split('\\')[0] : "(no folder)";
            firstFolder[head] = firstFolder.GetValueOrDefault(head) + 1;
            counts[files.Count] = counts.GetValueOrDefault(files.Count) + 1;
        }

        sb.AppendLine($"projects naming their own Project.hkx: {namesItself}");
        sb.AppendLine("first entry's folder: " +
            string.Join(", ", firstFolder.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}")));
        sb.AppendLine("file counts: " +
            string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key} files x{k.Value}")));

        // And what a typical creature's list looks like, for shape.
        foreach (string name in new[] { "DraugrProject", "DraugrSkeletonProject", "SabreCatProject" })
        {
            var p = cache.AnimationData.Projects.FirstOrDefault(x =>
                x.Stem.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p is null) continue;
            sb.AppendLine();
            sb.AppendLine($"=== {name} ({p.Block.Files.Count} files)");
            foreach (string f in p.Block.Files) sb.AppendLine($"  {f}");
        }

        File.WriteAllText(Path.Combine(outDir, "dupfiles.txt"), sb.ToString());
    }
}
