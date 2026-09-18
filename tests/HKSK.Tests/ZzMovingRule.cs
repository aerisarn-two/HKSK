using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Does the innermost state an attack event reaches hold movement-blended variants?
// The hypothesis: the flag marks an attack performed while locomotion runs.
public sealed class ZzMovingRule
{
    private const int FlagToNestedStateIdIsValid = 0x2000;

    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var rows = new List<(string Project, string Event, int Flag, bool Travels, int Clips, float Max)>();

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            var file = cache.FindProjectFile(p.Stem);
            if (actor is null || file is null) continue;
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

            var travel = actor.Clips
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            IEnumerable<IHavokObject> Children(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    string? path = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (path is not null && roots.TryGetValue(Path.GetFullPath(path), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            // the clips of one state, not descending into the machines nested below it
            (int Count, float Max) Own(IHavokObject generator)
            {
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var st = new Stack<IHavokObject>();
                st.Push(generator);
                int count = 0; float max = 0;
                while (st.Count > 0)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c) { count++; max = Math.Max(max, travel.GetValueOrDefault(c.m_name)); }
                    foreach (var ch in Children(n)) st.Push(ch);
                }
                return (count, max);
            }

            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                    flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);

            // the innermost state each event reaches: the nested state a transition names, and
            // the state a machine below the target enters on the same event
            var best = new Dictionary<string, (int Depth, int Clips, float Max)>(StringComparer.OrdinalIgnoreCase);

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
                    int depth = walk.Ancestors(sm).Count();

                    // follow the nested state the transition names
                    if ((t.m_flags & FlagToNestedStateIdIsValid) != 0)
                        foreach (var inner in Nested(target))
                            if (inner.m_states.FirstOrDefault(x => x.m_stateId == t.m_toNestedStateId)?.m_generator is { } g)
                            {
                                target = g; depth++; break;
                            }

                    (int clips, float max) = Own(target);
                    if (!best.TryGetValue(name, out var had) || depth > had.Depth)
                        best[name] = (depth, clips, max);
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

            foreach ((string name, int flag) in flags)
                if (best.TryGetValue(name, out var b))
                    rows.Add((p.Stem, name, flag, b.Max > 50f && b.Clips >= 2, b.Clips, b.Max));
        }

        var L = new List<string>();
        int tp = rows.Count(r => r.Flag != 0 && r.Travels), fn = rows.Count(r => r.Flag != 0 && !r.Travels);
        int fp = rows.Count(r => r.Flag == 0 && r.Travels), tn = rows.Count(r => r.Flag == 0 && !r.Travels);
        L.Add($"attack events with a state found: {rows.Count}");
        L.Add($"  flag 1, travelling variants (>=2 clips, one travels) : {tp}");
        L.Add($"  flag 1, not                                           : {fn}");
        L.Add($"  flag 0, travelling variants                           : {fp}");
        L.Add($"  flag 0, not                                           : {tn}");
        L.Add("");
        L.Add("flagged but no travelling variants:");
        foreach (var r in rows.Where(r => r.Flag != 0 && !r.Travels).OrderBy(r => r.Project))
            L.Add($"   {r.Project,-28} {r.Event,-34} {r.Clips} clips, max travel {r.Max:0.0}");
        L.Add("");
        L.Add("unflagged but travelling variants:");
        foreach (var r in rows.Where(r => r.Flag == 0 && r.Travels).OrderBy(r => r.Project))
            L.Add($"   {r.Project,-28} {r.Event,-34} {r.Clips} clips, max travel {r.Max:0.0}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/movingrule.txt", string.Join("\n", L));
    }
}
