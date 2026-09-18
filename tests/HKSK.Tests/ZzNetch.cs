using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzNetch
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        foreach (string stem in new[] { "netchproject", "witchlightproject", "atronachstormproject", "werewolfbeastproject", "defaultmale" })
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            var file = cache.FindProjectFile(stem);
            if (p is null || file is null) continue;
            var walk = ProjectWalk.Of(file);
            string folder = Path.GetDirectoryName(file)!;
            var events = new Dictionary<string, string[]>();
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []];
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                }

            var flag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                    flag[a.EventName] = Math.Max(flag.GetValueOrDefault(a.EventName), a.MovingAttack);

            L.Add($"======== {stem}");

            static string Name(IHavokObject n) => n switch
            {
                hkbNode node => $"{n.GetType().Name}({node.m_name})",
                _ => n.GetType().Name,
            };

            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                foreach (var t in (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(x => x.m_transitions?.m_transitions ?? [])))
                {
                    if (t.m_eventId < 0 || t.m_eventId >= ev.Length) continue;
                    string name = ev[t.m_eventId];
                    if (!flag.TryGetValue(name, out int f)) continue;
                    var state = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId);
                    if (state is null) continue;

                    L.Add($"-- [{f}] {name}  ->  state '{state.m_name}' of {sm.m_name}   transition flags 0x{t.m_flags:x}");
                    L.Add($"     state generator: {(state.m_generator is null ? "none" : Name(state.m_generator))}");

                    var chain = new List<string>();
                    IHavokObject child = sm;
                    foreach (var a in walk.Ancestors(sm))
                    {
                        chain.Add(Name(a.Node) + Extra(a.Node, child));
                        child = a.Node;
                    }
                    chain.Reverse();
                    L.Add("     from root: " + string.Join(" > ", chain));

                    // what the state itself holds
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                    var st = new Stack<IHavokObject>();
                    if (state.m_generator is not null) st.Push(state.m_generator);
                    var inside = new List<string>();
                    while (st.Count > 0)
                    {
                        var n = st.Pop();
                        if (!seen.Add(n)) continue;
                        if (n is hkbNode) inside.Add(Name(n));
                        foreach (var (_, _, c) in HavokEdges.Of(n)) st.Push(c);
                        if (n is hkbBehaviorReferenceGenerator r && HavokPath.Resolve(folder, r.m_behaviorName) is { } path
                            && roots.TryGetValue(Path.GetFullPath(path), out var root)) st.Push(root);
                    }
                    L.Add("     inside: " + string.Join(" ", inside.Take(24)));
                }
            }
        }

        string Extra(IHavokObject node, IHavokObject child) => node switch
        {
            hkbBlenderGenerator b => $"[{b.m_children.Count} arms, ours #{b.m_children.ToList().FindIndex(c => ReferenceEquals(c, child))}]",
            BSBoneSwitchGenerator s => $"[bone switch, ours {(ReferenceEquals(s.m_pDefaultGenerator, child) ? "default" : "bone child")}]",
            hkbModifierGenerator m => $"[modifier {m.m_modifier?.GetType().Name}]",
            _ => "",
        };

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/netch.txt", string.Join("\n", L));
    }
}
