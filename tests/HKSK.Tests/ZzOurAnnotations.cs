using System.Text;
using HKSK.Havok;
using Xunit;

namespace HKSK.Tests;

// What the clips we wrote actually announce, against what a shipped creature's do.
public sealed class ZzOurAnnotations
{
    [Fact]
    public void Dump()
    {
        string mine = Environment.GetEnvironmentVariable("HKSK_OURS") ?? "";
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (!Directory.Exists(mine)) return;

        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT")
            ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hksk-creature-dump")).FullName;

        var sb = new StringBuilder();

        void Look(string what, string folder, int take)
        {
            sb.AppendLine($"=== {what}");
            int carrying = 0, total = 0;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in Directory.GetFiles(folder, "*.hkx").Order())
            {
                total++;
                (float duration, var tracks) = UncompressedAnimation.Events(path);
                var events = tracks
                    .SelectMany(t => t.Events.Select(a => (t.Name, a.Text, a.Time)))
                    .ToList();

                if (events.Count > 0) carrying++;
                foreach (var e in events) seen[e.Text] = seen.GetValueOrDefault(e.Text) + 1;

                if (total <= take || Path.GetFileName(path).StartsWith("Swing", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).StartsWith("Walk", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"  {Path.GetFileName(path),-24} {duration,6:0.##}s  {tracks.Count} tracks, " +
                                  $"{events.Count} events: " +
                                  string.Join(", ", events.Take(8).Select(e => $"{e.Text}@{e.Time:0.##}")));
            }

            sb.AppendLine($"  {carrying} of {total} carry any event at all");
            sb.AppendLine("  events across them all: " +
                string.Join(", ", seen.OrderByDescending(k => k.Value).Take(20).Select(k => $"{k.Key} x{k.Value}")));
            sb.AppendLine();
        }

        Look("ours", mine, 8);

        if (Directory.Exists(corpus))
        {
            string draugr = Path.Combine(corpus, "actors", "draugr", "animations");
            if (Directory.Exists(draugr)) Look("the draugr's", draugr, 8);
        }

        File.WriteAllText(Path.Combine(outDir, "our-annotations.txt"), sb.ToString());
    }
}
