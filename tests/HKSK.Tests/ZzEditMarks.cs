using System.Text.RegularExpressions;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// How much late hand-editing does a project show? The behaviour tool names a new node after
// its class with a running number -- Behavior16, ModifierGenerator07, BSIsActiveModifier00 --
// so the share of such names is a proxy for nodes added after the graph was first laid out.
public sealed class ZzEditMarks
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flagged = shipped.Projects
            .Where(p => p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Any(a => a.MovingAttack != 0))
            .Select(p => p.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<(bool Flag, string Project, int Named, int Default, int Numbered)>();

        foreach (var project in cache.Actors())
        {
            string? path = cache.FindProjectFile(project.Name);
            if (!project.HasCache || path is null) continue;

            int named = 0, def = 0, numbered = 0;
            foreach (var s in ProjectWalk.Of(path).Steps)
            {
                if (s.Node is not hkbNode node || string.IsNullOrEmpty(node.m_name)) continue;
                named++;

                string cls = node.GetType().Name;
                string bare = cls.StartsWith("hkb", StringComparison.Ordinal) ? cls[3..] : cls;
                // the tool's own name: the class, or the class with a running number
                if (Regex.IsMatch(node.m_name, $@"^(?:{Regex.Escape(cls)}|{Regex.Escape(bare)})\d*$", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(node.m_name, @"^Behavior\d+$", RegexOptions.IgnoreCase))
                    def++;
                if (Regex.IsMatch(node.m_name, @"\d\d$")) numbered++;
            }

            rows.Add((flagged.Contains(project.Name), project.Name, named, def, numbered));
        }

        var L = new List<string> { $"{"",1} {"project",-30} {"named",6} {"tool-named",11} {"share",7} {"ends in digits",15}" };
        foreach (var r in rows.OrderByDescending(r => r.Named == 0 ? 0 : (double)r.Default / r.Named))
            L.Add($"{(r.Flag ? "F" : " ")} {r.Project,-30} {r.Named,6} {r.Default,11} {(r.Named == 0 ? 0 : 100.0 * r.Default / r.Named),6:0.0}% {r.Numbered,15}");

        double sixAvg = rows.Where(r => r.Flag).Average(r => r.Named == 0 ? 0 : 100.0 * r.Default / r.Named);
        double restAvg = rows.Where(r => !r.Flag).Average(r => r.Named == 0 ? 0 : 100.0 * r.Default / r.Named);
        L.Add("");
        L.Add($"mean tool-named share: the six {sixAvg:0.0}%, the other {rows.Count(r => !r.Flag)} {restAvg:0.0}%");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/editmarks.txt", string.Join("\n", L));
    }
}
