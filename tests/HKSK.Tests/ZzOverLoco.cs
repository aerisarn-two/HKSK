using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Is the attack played over locomotion? For every state an attack event can enter, walk up:
// if an ancestor blends or bone-switches it against a branch that holds the movement tree,
// the character keeps moving while it plays.
public sealed class ZzOverLoco
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var rows = new List<(string Project, string Event, int Flag, bool Over)>();

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            var path = cache.FindProjectFile(p.Stem);
            if (actor is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

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
                    string? q = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (q is not null && roots.TryGetValue(Path.GetFullPath(q), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            // a movement tree: eight or more travelling clips under one branch
            var isLoco = new Dictionary<IHavokObject, bool>(ReferenceEqualityComparer.Instance);
            bool Locomotion(IHavokObject from, IHavokObject skip)
            {
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { skip };
                var st = new Stack<IHavokObject>();
                st.Push(from);
                var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (st.Count > 0)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c && travel.GetValueOrDefault(c.m_name) > 50f) moving.Add(c.m_name);
                    if (moving.Count >= 8) return true;
                    foreach (var ch in Children(n)) st.Push(ch);
                }
                return false;
            }

            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                    flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);

            var over = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

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
                    if (sm.m_states.All(x => x.m_stateId != t.m_toStateId)) continue;

                    bool verdict = over.GetValueOrDefault(name);
                    if (verdict) continue;

                    IHavokObject child = sm;
                    foreach (var a in walk.Ancestors(sm))
                    {
                        IEnumerable<IHavokObject?> arms = a.Node switch
                        {
                            hkbBlenderGenerator b => b.m_children,
                            BSBoneSwitchGenerator bs => bs.m_ChildrenA.Cast<IHavokObject?>().Append(bs.m_pDefaultGenerator),
                            _ => [],
                        };

                        foreach (var arm in arms)
                            if (arm is not null && !ReferenceEquals(arm, child))
                            {
                                if (!isLoco.TryGetValue(arm, out bool loco)) isLoco[arm] = loco = Locomotion(arm, child);
                                if (loco) { verdict = true; break; }
                            }

                        if (verdict) break;
                        child = a.Node;
                    }

                    over[name] = verdict;
                }
            }

            foreach ((string name, int flag) in flags)
                if (over.TryGetValue(name, out bool o))
                    rows.Add((p.Stem, name, flag, o));
        }

        int tp = rows.Count(r => r.Flag != 0 && r.Over), fn = rows.Count(r => r.Flag != 0 && !r.Over);
        int fp = rows.Count(r => r.Flag == 0 && r.Over), tn = rows.Count(r => r.Flag == 0 && !r.Over);

        var L = new List<string>
        {
            $"attack events: {rows.Count}",
            $"  flag 1, played over locomotion : {tp}",
            $"  flag 1, not                    : {fn}",
            $"  flag 0, played over locomotion : {fp}",
            $"  flag 0, not                    : {tn}",
            "",
            "by project (flagged over / flagged total, clear over / clear total):",
        };
        foreach (var g in rows.GroupBy(r => r.Project).OrderBy(g => g.Key))
        {
            int f1 = g.Count(r => r.Flag != 0), f1o = g.Count(r => r.Flag != 0 && r.Over);
            int f0 = g.Count(r => r.Flag == 0), f0o = g.Count(r => r.Flag == 0 && r.Over);
            if (f1 > 0 || f0o > 0) L.Add($"   {g.Key,-26} flagged {f1o}/{f1}   clear {f0o}/{f0}");
        }

        L.Add("");
        L.Add("flagged, not over locomotion:");
        foreach (var r in rows.Where(r => r.Flag != 0 && !r.Over).OrderBy(r => r.Project)) L.Add($"   {r.Project,-26} {r.Event}");
        L.Add("");
        L.Add("clear, over locomotion:");
        foreach (var r in rows.Where(r => r.Flag == 0 && r.Over).OrderBy(r => r.Project).Take(60)) L.Add($"   {r.Project,-26} {r.Event}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/overloco.txt", string.Join("\n", L));
    }
}
