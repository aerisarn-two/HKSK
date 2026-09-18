using System.Reflection;
using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzImplicit
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();
        foreach (string projectName in new[] { "DefaultMale", "DraugrProject", "ChaurusProject" })
        {
            var walk = ProjectWalk.Of(cache.OpenActor(projectName)!.ProjectFile!.File.Path);
            var events = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []];
            var machines = walk.Steps.Where(s => s.Node is hkbStateMachine).ToList();
            var modes = machines.GroupBy(s => ((hkbStateMachine)s.Node).m_startStateMode).Select(g => $"mode {g.Key}: {g.Count()}");
            var chooser = machines.Count(s => ((hkbStateMachine)s.Node).m_startStateChooser is not null);
            string E(ProjectStep s, int id) => id >= 0 && id < events[s.File].Length ? events[s.File][id] : "";
            var random = machines.Where(s => ((hkbStateMachine)s.Node).m_randomTransitionEventId >= 0).Select(s => $"{((hkbStateMachine)s.Node).m_name}:{E(s, ((hkbStateMachine)s.Node).m_randomTransitionEventId)}").ToList();
            var higher = machines.Count(s => ((hkbStateMachine)s.Node).m_transitionToNextHigherStateEventId >= 0);
            var lower = machines.Count(s => ((hkbStateMachine)s.Node).m_transitionToNextLowerStateEventId >= 0);
            var back = machines.Count(s => ((hkbStateMachine)s.Node).m_returnToPreviousStateEventId >= 0);
            L.Add($"== {projectName}: machines {machines.Count}; {string.Join(", ", modes)}; chooser {chooser}; random-transition {random.Count}; next-higher {higher}; next-lower {lower}; return-to-previous {back}");
            L.Add("   random events: " + string.Join(" ", random.Take(20)));

            // every reference to an event id by name, outside transitions: which node types and fields carry events
            var carriers = new Dictionary<string, int>();
            var named = Environment.GetEnvironmentVariable("EVENT") ?? "idle_A_left_longTrans";
            foreach (var s in walk.Steps)
            {
                if (!events.ContainsKey(s.File)) continue;
                int wanted = Array.FindIndex(events[s.File], e => e.Equals(named, StringComparison.OrdinalIgnoreCase));
                if (wanted < 0) continue;
                foreach (var p in s.Node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.PropertyType != typeof(int) || !p.Name.Contains("vent", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((int)p.GetValue(s.Node)! == wanted)
                    {
                        string key = $"{s.Node.GetType().Name}.{p.Name} (parent {s.Parent?.GetType().Name}.{s.Member}) in {s.FileName}";
                        carriers[key] = carriers.GetValueOrDefault(key) + 1;
                    }
                }
            }
            L.Add($"   references to '{named}': " + string.Join(" | ", carriers.Select(kv => $"{kv.Value}x {kv.Key}")));
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/implicit.txt", string.Join("\n", L));
    }
}
