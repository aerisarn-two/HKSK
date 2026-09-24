using System.Text;
using HKSK.Havok;
using Xunit;

namespace HKSK.Tests;

// Where in a clip the game's own attacks announce their swing and their hit, and where a
// walk announces its feet: the times an authored annotation would have to land at.
public sealed class ZzHitFrames
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        if (corpus.Length == 0) return;

        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT")
            ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hksk-creature-dump")).FullName;

        var sb = new StringBuilder();
        var fractions = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
        var perClip = new List<string>();

        foreach (string creature in new[] { "draugr", "falmer", "sabrecat", "wolf" })
        {
            string folder = Path.Combine(corpus, "actors", creature, "animations");
            if (!Directory.Exists(folder)) folder = Path.Combine(corpus, "actors", "canine", "animations", creature);
            if (!Directory.Exists(folder)) continue;

            foreach (string path in Directory.GetFiles(folder, "*.hkx").Order())
            {
                (float duration, var tracks) = UncompressedAnimation.Events(path);
                if (duration <= 0) continue;

                var events = tracks.SelectMany(t => t.Events).ToList();
                foreach (var e in events)
                {
                    if (!fractions.TryGetValue(e.Text, out var list)) fractions[e.Text] = list = [];
                    list.Add(e.Time / duration);
                }

                if (events.Any(e => e.Text.Equals("HitFrame", StringComparison.OrdinalIgnoreCase)) && perClip.Count < 12)
                    perClip.Add($"  {creature}/{Path.GetFileName(path),-30} {duration,5:0.##}s  " +
                        string.Join(", ", events.OrderBy(e => e.Time).Select(e => $"{e.Text}@{e.Time / duration:P0}")));
            }
        }

        sb.AppendLine("--- clips that announce a hit, with every event as a fraction of the clip");
        foreach (string line in perClip) sb.AppendLine(line);

        sb.AppendLine();
        sb.AppendLine("--- where each event sits, as a fraction of its clip");
        foreach ((string name, var list) in fractions.Where(f => f.Value.Count >= 5).OrderByDescending(f => f.Value.Count))
        {
            var sorted = list.Order().ToList();
            sb.AppendLine($"  {name,-40} x{sorted.Count,-5} " +
                $"low {sorted[0]:P0}  quarter {sorted[sorted.Count / 4]:P0}  " +
                $"middle {sorted[sorted.Count / 2]:P0}  three-quarter {sorted[sorted.Count * 3 / 4]:P0}  high {sorted[^1]:P0}");
        }

        File.WriteAllText(Path.Combine(outDir, "hitframes.txt"), sb.ToString());
    }
}
