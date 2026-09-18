using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// What is actually inside the state an attack event reaches: node types, the variables its
// nodes bind, and the events its modifiers send. Printed side by side, flagged and clear.
public sealed class ZzInside
{
    private const int FlagToNestedStateIdIsValid = 0x2000;

    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        foreach (string stem in new[] { "defaultmale", "netchproject", "witchlightproject", "werewolfbeastproject", "bearproject", "chaurusproject", "atronachstormproject" })
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            var path = cache.FindProjectFile(stem);
            if (p is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var events = new Dictionary<string, string[]>();
            var vars = new Dictionary<string, string[]>();
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []];
                    vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                }

            string FileOf(IHavokObject n) => walk.Steps.FirstOrDefault(s => ReferenceEquals(s.Node, n)).File ?? "";

            IEnumerable<IHavokObject> Children(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    string? q = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (q is not null && roots.TryGetValue(Path.GetFullPath(q), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                    flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);

            var best = new Dictionary<string, (int Depth, string Line)>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                foreach (var t in (sm.m_wildcardTransitions?.m_transitions ?? [])
                             .Concat(sm.m_states.SelectMany(x => x.m_transitions?.m_transitions ?? [])))
                {
                    if (t.m_eventId < 0 || t.m_eventId >= ev.Length) continue;
                    string name = ev[t.m_eventId];
                    if (!flags.ContainsKey(name)) continue;
                    var state = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId);
                    if (state?.m_generator is null) continue;

                    IHavokObject target = state.m_generator;
                    string stateName = state.m_name;
                    int depth = walk.Ancestors(sm).Count();

                    if ((t.m_flags & FlagToNestedStateIdIsValid) != 0)
                        foreach (var inner in Nested(target))
                            if (inner.m_states.FirstOrDefault(x => x.m_stateId == t.m_toNestedStateId) is { m_generator: not null } deeper)
                            {
                                target = deeper.m_generator!; stateName = deeper.m_name; depth++; break;
                            }

                    // everything below the state: node types, bound variables, sent events
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                    var st = new Stack<IHavokObject>();
                    st.Push(target);
                    var kinds = new SortedSet<string>(StringComparer.Ordinal);
                    var bound = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    int clips = 0;
                    while (st.Count > 0)
                    {
                        var n = st.Pop();
                        if (!seen.Add(n)) continue;
                        if (n is hkbClipGenerator) clips++;
                        else if (n is hkbNode) kinds.Add(n.GetType().Name.Replace("hkb", "").Replace("Generator", ""));

                        if (n is hkbNode node)
                            foreach (var b in node.m_variableBindingSet?.m_bindings ?? [])
                            {
                                var names = vars.GetValueOrDefault(FileOf(n)) ?? vars.Values.FirstOrDefault() ?? [];
                                if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length)
                                    bound.Add(names[b.m_variableIndex]);
                            }

                        foreach (var ch in Children(n)) st.Push(ch);
                    }

                    string line = $"[{flags[name]}] {name,-36} {stateName,-34} {clips,3} clips  {string.Join(" ", kinds)}"
                                + (bound.Count > 0 ? "   binds: " + string.Join(" ", bound) : "");
                    if (!best.TryGetValue(name, out var had) || depth > had.Depth) best[name] = (depth, line);
                }

                IEnumerable<hkbStateMachine> Nested(IHavokObject from)
                {
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                    var st = new Stack<IHavokObject>();
                    st.Push(from);
                    while (st.Count > 0)
                    {
                        var n = st.Pop();
                        if (!seen.Add(n)) continue;
                        if (n is hkbStateMachine m) { yield return m; continue; }
                        foreach (var ch in Children(n)) st.Push(ch);
                    }
                }
            }

            L.Add($"======== {stem}");
            foreach (var line in best.Values.Select(v => v.Line).OrderBy(x => x, StringComparer.Ordinal)) L.Add("  " + line);
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/inside.txt", string.Join("\n", L));
    }
}
