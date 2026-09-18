using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMoving4
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var table = new SortedDictionary<string, (int On, int Off, List<string> ExOn, List<string> ExOff)>();
        foreach (var p in shipped.Projects)
        {
            var file = cache.FindProjectFile(p.Stem);
            if (file is null) continue;
            var walk = ProjectWalk.Of(file);
            string folder = Path.GetDirectoryName(file)!;
            var events = new Dictionary<string, string[]>(); var vars = new Dictionary<string, string[]>();
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var fileOf = new Dictionary<IHavokObject, string>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                fileOf.TryAdd(s.Node, s.File);
                if (s.Node is hkbBehaviorGraph g) { events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []]; if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator); }
            }
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
            string Bound(hkbNode n, string member)
            {
                if (n.m_variableBindingSet is not { } b || !fileOf.TryGetValue(n, out var f)) return "";
                var t = vars.GetValueOrDefault(f) ?? [];
                return string.Join(",", b.m_bindings.Where(x => x.m_memberPath == member && x.m_variableIndex >= 0 && x.m_variableIndex < t.Length).Select(x => t[x.m_variableIndex]));
            }
            var features = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                var infos = (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(x => x.m_transitions?.m_transitions ?? []));
                foreach (var t in infos)
                {
                    if (t.m_eventId < 0 || t.m_eventId >= ev.Length) continue;
                    var target = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_generator;
                    if (target is null) continue;
                    if (!features.TryGetValue(ev[t.m_eventId], out var fs)) features[ev[t.m_eventId]] = fs = new();
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance); var st = new Stack<IHavokObject>(); st.Push(target);
                    while (st.Count > 0)
                    {
                        var n = st.Pop(); if (!seen.Add(n)) continue;
                        if (n is BSBoneSwitchGenerator) fs.Add("bone switch");
                        if (n is BSCyclicBlendTransitionGenerator) fs.Add("cyclic blend");
                        if (n is hkbBlenderGenerator bl)
                        {
                            string param = Bound(bl, "blendParameter");
                            if (param.Contains("Speed", StringComparison.OrdinalIgnoreCase)) fs.Add("speed blend");
                            if (param.Contains("Direction", StringComparison.OrdinalIgnoreCase)) fs.Add("direction blend");
                            if (bl.m_children.Any(c => c.m_worldFromModelWeight == 0f)) fs.Add("motion-free child");
                        }
                        if (n is hkbNode hn && Bound(hn, "isActive").Contains("bAnimationDriven")) fs.Add("animation driven");
                        if (n is hkbStateMachine) continue; // the entered state only
                        foreach (var c in Children(n)) st.Push(c);
                    }
                }
            }
            var seenAttack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seenAttack.Add(a.EventName + "|" + a.Mirrored)) continue;
                    var fs = features.GetValueOrDefault(a.EventName);
                    string key = fs is null ? "(no transition)" : fs.Count == 0 ? "(none)" : string.Join(" + ", fs);
                    var row = table.GetValueOrDefault(key, (On: 0, Off: 0, ExOn: new List<string>(), ExOff: new List<string>()));
                    if (a.Mirrored != 0) { row.On++; if (row.ExOn.Count < 8) row.ExOn.Add($"{p.Stem}:{a.EventName}"); }
                    else { row.Off++; if (row.ExOff.Count < 8) row.ExOff.Add($"{p.Stem}:{a.EventName}"); }
                    table[key] = row;
                }
        }
        var L = new List<string> { "features of the entered state: flag 1 / flag 0 (distinct attack events per project)" };
        foreach (var (k, v) in table.OrderByDescending(x => x.Value.On + x.Value.Off)) { L.Add($"  {k,-70} {v.On,4} / {v.Off,4}"); L.Add($"      on:  {string.Join(" ", v.ExOn)}"); L.Add($"      off: {string.Join(" ", v.ExOff)}"); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/moving4.txt", string.Join("\n", L));
    }
}
