using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzParallel
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var cross = new SortedDictionary<string, (int On, int Off, List<string> ExOn, List<string> ExOff)>();
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
            bool Locomotion(IHavokObject start, IHavokObject skip)
            {
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { skip };
                var st = new Stack<IHavokObject>(); st.Push(start);
                int budget = 6000;
                while (st.Count > 0 && budget-- > 0)
                {
                    var n = st.Pop(); if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c && travel.GetValueOrDefault(c.m_name) > 50f) return true;
                    foreach (var ch in Children(n)) st.Push(ch);
                }
                return false;
            }
            // for the machine holding the attack transition: is a sibling branch locomotion?
            string Parallel(hkbStateMachine machine)
            {
                IHavokObject child = machine;
                foreach (var a in walk.Ancestors(machine))
                {
                    if (a.Node is hkbBlenderGenerator bl)
                    {
                        var others = bl.m_children.Where(c => !ReferenceEquals(c, child) && c is not null).ToList();
                        if (others.Any(o => Locomotion(o, child))) return "parallel with locomotion";
                    }
                    if (a.Node is BSBoneSwitchGenerator bs)
                    {
                        var others = bs.m_ChildrenA.Where(c => !ReferenceEquals(c, child) && c is not null).Cast<IHavokObject>().Append(bs.m_pDefaultGenerator!).Where(o => o is not null).ToList();
                        if (others.Any(o => Locomotion(o, child))) return "parallel with locomotion";
                    }
                    child = a.Node;
                }
                return "replaces locomotion";
            }
            var byEvent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                foreach (var t in (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(x => x.m_transitions?.m_transitions ?? [])))
                {
                    if (t.m_eventId < 0 || t.m_eventId >= ev.Length) continue;
                    if (sm.m_states.All(x => x.m_stateId != t.m_toStateId)) continue;
                    var target = sm.m_states.First(x => x.m_stateId == t.m_toStateId).m_generator;
                    string verdict = Parallel(sm);
                    if (verdict == "replaces locomotion" && target is not null)
                    {
                        // does the attack state itself hold variants that travel?
                        var seen2 = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                        var st2 = new Stack<IHavokObject>(); st2.Push(target);
                        int clips = 0; bool moving = false; int budget2 = 8000;
                        while (st2.Count > 0 && budget2-- > 0)
                        {
                            var n2 = st2.Pop(); if (!seen2.Add(n2)) continue;
                            if (n2 is hkbClipGenerator c2) { clips++; if (travel.GetValueOrDefault(c2.m_name) > 50f) moving = true; }
                            foreach (var ch in Children(n2)) st2.Push(ch);
                        }
                        verdict = moving ? "has travelling variants" : clips > 1 ? "several clips, none travelling" : "single clip";
                    }
                    if (!byEvent.TryGetValue(ev[t.m_eventId], out var had) || had == "single clip") byEvent[ev[t.m_eventId]] = verdict;
                }
            }
            var seenA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seenA.Add(a.EventName)) continue;
                    if (!byEvent.TryGetValue(a.EventName, out var verdict)) continue;
                    var row = cross.GetValueOrDefault(verdict, (On: 0, Off: 0, ExOn: new List<string>(), ExOff: new List<string>()));
                    if (a.MovingAttack != 0) { row.On++; if (row.ExOn.Count < 8) row.ExOn.Add($"{p.Stem}:{a.EventName}"); }
                    else { row.Off++; if (row.ExOff.Count < 8) row.ExOff.Add($"{p.Stem}:{a.EventName}"); }
                    cross[verdict] = row;
                }
        }
        var L = new List<string> { "flag 1 / flag 0, one attack event per project" };
        foreach (var (k, v) in cross) { L.Add($"  {k,-28} {v.On,4} / {v.Off,4}"); L.Add($"      on:  {string.Join(" ", v.ExOn)}"); L.Add($"      off: {string.Join(" ", v.ExOff)}"); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/parallel.txt", string.Join("\n", L));
    }
}
