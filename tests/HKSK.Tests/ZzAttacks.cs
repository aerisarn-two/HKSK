using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// probe: are a set's attacks the clip generators under the states an attack event enters?
public sealed class ZzAttacks
{
    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        SkyrimCache cache = SkyrimCache.Load(root);
        var sets = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        int attacks = 0, exact = 0, subset = 0, superset = 0, eventMissing = 0;
        string mode = Environment.GetEnvironmentVariable("MODE") ?? "all";

        foreach (var p in sets.Projects)
        {
            string? projectFile = cache.FindProjectFile(p.Stem);
            if (projectFile is null) { L.Add($"{p.Stem}: no project file"); continue; }
            var walk = ProjectWalk.Of(projectFile);

            // each file's event names, and each referenced file's root
            var eventsOf = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var rootOf = new Dictionary<IHavokObject, IHavokObject>(ReferenceEqualityComparer.Instance);
            var fileRoot = new Dictionary<string, IHavokObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
            {
                if (s.Node is hkbBehaviorGraph g)
                {
                    eventsOf[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []];
                    if (g.m_rootGenerator is not null) fileRoot[s.File] = g.m_rootGenerator;
                }
            }
            foreach (var s in walk.Steps)
                if (s.Parent is hkbBehaviorReferenceGenerator r && s.Node is hkRootLevelContainer) rootOf[r] = s.Node;
            string folder = Path.GetDirectoryName(projectFile)!;

            IEnumerable<IHavokObject> Children(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    string? path = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (path is not null && fileRoot.TryGetValue(Path.GetFullPath(path), out var fr)) yield return fr;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            // transitions of a machine on one event: (from any state) -> target generators
            IEnumerable<hkbGenerator> TargetsOn(hkbStateMachine sm, string file, string eventName)
            {
                string[] ev = eventsOf.GetValueOrDefault(file) ?? [];
                var infos = new List<hkbStateMachineTransitionInfo>();
                if (sm.m_wildcardTransitions is not null) infos.AddRange(sm.m_wildcardTransitions.m_transitions);
                foreach (var st in sm.m_states)
                    if (st.m_transitions is not null) infos.AddRange(st.m_transitions.m_transitions);
                foreach (var t in infos)
                    if (t.m_eventId >= 0 && t.m_eventId < ev.Length && string.Equals(ev[t.m_eventId], eventName, StringComparison.OrdinalIgnoreCase)
                        && sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_generator is { } g)
                        yield return g;
            }
            var fileOfNode = new Dictionary<IHavokObject, string>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps) fileOfNode[s.Node] = s.File;

            List<string> ClipsUnder(hkbGenerator start, string eventName, int nested = -1)
            {
                var names = new List<string>();
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IHavokObject>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c) names.Add(c.m_name);
                    if (mode != "all" && n is hkbStateMachine sm)
                    {
                        if (nested >= 0 && mode.Contains("nested"))
                        {
                            var ns = sm.m_states.FirstOrDefault(x => x.m_stateId == nested);
                            nested = -1;
                            if (ns?.m_generator is not null) { stack.Push(ns.m_generator); continue; }
                        }
                        var on = TargetsOn(sm, fileOfNode.GetValueOrDefault(sm) ?? "", eventName).ToList();
                        if (on.Count > 0) { foreach (var g in on) stack.Push(g); continue; }
                        var st = sm.m_states.FirstOrDefault(x => x.m_stateId == sm.m_startStateId);
                        if (st?.m_generator is not null) stack.Push(st.m_generator);
                        continue;
                    }
                    if (mode.Contains("selectdefault") && n is hkbManualSelectorGenerator ms)
                    {
                        int i = ms.m_selectedGeneratorIndex;
                        if (i >= 0 && i < ms.m_generators.Count && ms.m_generators[i] is { } g) stack.Push(g);
                        continue;
                    }
                    foreach (var c2 in Children(n)) stack.Push(c2);
                }
                return names;
            }

            // event name -> clip names, over every transition in every machine
            var byEvent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string name, IEnumerable<string> clips)
            {
                if (!byEvent.TryGetValue(name, out var set)) byEvent[name] = set = new(StringComparer.OrdinalIgnoreCase);
                set.UnionWith(clips);
            }
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                string[] ev = eventsOf.GetValueOrDefault(s.File) ?? [];
                string? E(hkbStateMachineTransitionInfo t) => t.m_eventId >= 0 && t.m_eventId < ev.Length ? ev[t.m_eventId] : null;
                var wild = (sm.m_wildcardTransitions?.m_transitions ?? []).Where(t => (t.m_flags & 0x20) == 0).OrderByDescending(t => t.m_priority).ToList();
                var names = sm.m_states.SelectMany(st => st.m_transitions?.m_transitions ?? []).Concat(wild).Select(E).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string name in names)
                    foreach (var st in sm.m_states)
                    {
                        var local = (st.m_transitions?.m_transitions ?? []).Where(t => (t.m_flags & 0x20) == 0).OrderByDescending(t => t.m_priority);
                        bool Match(hkbStateMachineTransitionInfo t) => string.Equals(E(t), name, StringComparison.OrdinalIgnoreCase) && t.m_toStateId != st.m_stateId;
                        var chosen = local.Where(Match).ToList();
                        if (chosen.Count == 0) chosen = wild.Where(Match).ToList();
                        if (mode.Contains("first")) chosen = chosen.Take(1).ToList();
                        foreach (var t in chosen)
                        {
                            var target = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId);
                            if (target?.m_generator is null) continue;
                            Add(name, ClipsUnder(target.m_generator, name, (t.m_flags & 0x2000) != 0 ? t.m_toNestedStateId : -1));
                        }
                    }
            }

            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    attacks++;
                    var shipped = a.Clips.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!byEvent.TryGetValue(a.EventName, out var built)) { eventMissing++; L.Add($"  {p.Stem} {a.EventName}: no transition on it; shipped {string.Join(",", a.Clips)}"); continue; }
                    if (built.SetEquals(shipped)) exact++;
                    else if (shipped.IsSubsetOf(built)) { superset++; if (Environment.GetEnvironmentVariable("SHOWSUPER") is not null) L.Add($"  {p.Stem} {a.EventName}: built {built.Count} ⊇ shipped {string.Join(",", a.Clips)} | extra {string.Join(",", built.Except(shipped).Take(8))}"); }
                    else if (built.IsSubsetOf(shipped)) { subset++; L.Add($"  {p.Stem} {a.EventName}: built ⊂ shipped; built {string.Join(",", built)} shipped {string.Join(",", a.Clips)}"); }
                    else L.Add($"  {p.Stem} {a.EventName}: differ; built {string.Join(",", built.Take(10))} shipped {string.Join(",", a.Clips)}");
                }
        }
        L.Insert(0, $"attacks {attacks}: exact {exact}, built is a superset {superset}, built is a subset {subset}, event has no transition {eventMissing}, other {attacks - exact - superset - subset - eventMissing}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/attacks-" + mode + ".txt", string.Join("\n", L));
    }
}
