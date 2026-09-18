using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// probe: are a set's attacks the clips the graph is playing once the attack event is raised?
public sealed class ZzAttacksRun
{
    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        SkyrimCache cache = SkyrimCache.Load(root);
        var sets = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        int attacks = 0, exact = 0, contains = 0, none = 0;
        string[] prelude = (Environment.GetEnvironmentVariable("PRELUDE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var p in sets.Projects)
        {
            string? projectFile = cache.FindProjectFile(p.Stem);
            if (projectFile is null) continue;

            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    attacks++;
                    void Drive(IReadOnlyDictionary<string, Variables> tables)
                    {
                        foreach (var t in tables.Values)
                            foreach (var hv in set.HandVariables.Variables)
                                t.Set(hv.Name, (float)hv.Min);
                    }
                    var events = Events.Of([.. prelude, .. set.SwapEvents.Take(1), a.EventName]);
                    var run = ActiveGenerators.Of(projectFile, Drive, events);
                    var clips = run.Active.Select(n => n.Generator).OfType<hkbClipGenerator>().Select(c => c.m_name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var shipped = a.Clips.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (clips.SetEquals(shipped)) exact++;
                    else if (shipped.Count > 0 && shipped.IsSubsetOf(clips)) { contains++; L.Add($"  {p.Stem} {set.Name()} {a.EventName}: running {string.Join(",", clips)} ⊇ shipped {string.Join(",", a.Clips)}"); }
                    else { none++; L.Add($"  {p.Stem} {set.Name()} {a.EventName}: running {string.Join(",", clips.Take(8))} shipped {string.Join(",", a.Clips)}"); }
                }
        }
        L.Insert(0, $"attacks {attacks}: exact {exact}, running clips include shipped {contains}, other {none}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/attacks-run.txt", string.Join("\n", L));
    }
}

static class ZzSetName
{
    public static string Name(this ProjectAttackBlock b) => "";
}
