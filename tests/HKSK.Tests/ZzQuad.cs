using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The quadrupeds share quadrupedbehavior.hkx. Why is attackStart_Attack2 animation-driven
// for the cow and not for the bear?
public sealed class ZzQuad
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        foreach (string stem in new[] { "BearProject", "HighlandCowProject", "GoatProject", "MammothProject", "SabreCatProject", "WolfProject", "DeerProject", "DogProject" })
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            string? path = cache.FindProjectFile(stem);
            if (p is null || path is null) continue;
            var walk = ProjectWalk.Of(path);

            var files = walk.Steps.Select(s => Path.GetFileName(s.File)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            var writers = new List<string>();
            var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    var names = g.m_data?.m_stringData?.m_variableNames ?? [];
                    for (int i = 0; i < names.Count; i++)
                        if (string.Equals(names[i], "bAnimationDriven", StringComparison.OrdinalIgnoreCase)) indexOf[s.File] = i;
                }

            foreach (var s in walk.Steps)
            {
                if (!indexOf.TryGetValue(s.File, out int at) || s.Node is not hkbNode node) continue;
                foreach (var b in node.m_variableBindingSet?.m_bindings ?? [])
                    if (b.m_variableIndex == at && (b.m_memberPath.StartsWith("bIsActive", StringComparison.Ordinal) || b.m_memberPath == "isActive"))
                        writers.Add($"{node.GetType().Name}({node.m_name}).{b.m_memberPath} in {Path.GetFileName(s.File)}");
            }

            var attacks = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks)
                .GroupBy(a => a.EventName, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}=[{string.Join(",", g.First().Clips)}]");

            L.Add($"======== {stem}   ({files.Count} behaviour files)");
            L.Add($"   files   : {string.Join(" ", files)}");
            L.Add($"   attacks : {string.Join("  ", attacks)}");
            L.Add($"   writers : {(writers.Count == 0 ? "(none)" : string.Join("; ", writers.Distinct()))}");
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/quad.txt", string.Join("\n", L));
    }
}
