using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMoving2
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var table = new SortedDictionary<string, (int On, int Off, List<string> ExOn, List<string> ExOff)>();
        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            var file = cache.FindProjectFile(p.Stem);
            if (actor is null || file is null) continue;
            var walk = ProjectWalk.Of(file);
            string folder = Path.GetDirectoryName(file)!;
            var events = new Dictionary<string, string[]>();
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) { events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator); }
            var travel = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            IEnumerable<IHavokObject> Children(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    var path = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (path is not null && roots.TryGetValue(Path.GetFullPath(path), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }
            bool PlaysTravel(hkbGenerator? g)
            {
                if (g is null) return false;
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance); var st = new Stack<IHavokObject>(); st.Push(g);
                int budget = 4000;
                while (st.Count > 0 && budget-- > 0)
                {
                    var n = st.Pop(); if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c && travel.GetValueOrDefault(c.m_name) > 50f) return true;
                    foreach (var ch in Children(n)) st.Push(ch);
                }
                return false;
            }

            var sources = new Dictionary<string, (bool Moving, bool Wild)>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                string? E(int id) => id >= 0 && id < ev.Length ? ev[id] : null;
                foreach (var st in sm.m_states)
                    foreach (var t in st.m_transitions?.m_transitions ?? [])
                        if (E(t.m_eventId) is { } name)
                        {
                            var cur = sources.GetValueOrDefault(name);
                            sources[name] = (cur.Moving || PlaysTravel(st.m_generator), cur.Wild);
                        }
                foreach (var t in sm.m_wildcardTransitions?.m_transitions ?? [])
                    if (E(t.m_eventId) is { } name)
                    {
                        var cur = sources.GetValueOrDefault(name);
                        sources[name] = (cur.Moving || sm.m_states.Any(x => PlaysTravel(x.m_generator)), true);
                    }
            }

            var seenAttack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seenAttack.Add(a.EventName + "|" + a.Mirrored)) continue;
                    if (!sources.TryGetValue(a.EventName, out var src)) continue;
                    string key = $"from a travelling state: {src.Moving}{(src.Wild ? " (wildcard)" : "")}";
                    var row = table.GetValueOrDefault(key, (On: 0, Off: 0, ExOn: new List<string>(), ExOff: new List<string>()));
                    if (a.Mirrored != 0) { row.On++; if (row.ExOn.Count < 8) row.ExOn.Add($"{p.Stem}:{a.EventName}"); }
                    else { row.Off++; if (row.ExOff.Count < 8) row.ExOff.Add($"{p.Stem}:{a.EventName}"); }
                    table[key] = row;
                }
        }
        var L = new List<string> { "flag 1 / flag 0 (distinct attack events per project)" };
        foreach (var (k, v) in table) { L.Add($"  {k,-45} {v.On,4} / {v.Off,4}"); L.Add($"      on:  {string.Join(" ", v.ExOn)}"); L.Add($"      off: {string.Join(" ", v.ExOff)}"); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/moving2.txt", string.Join("\n", L));
    }
}
