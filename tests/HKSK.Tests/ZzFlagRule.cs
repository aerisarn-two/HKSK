using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzFlagRule
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var cross = new SortedDictionary<string, (int On, int Off, List<string> ExOn, List<string> ExOff)>();
        foreach (var p in shipped.Projects)
        {
            var file = cache.FindProjectFile(p.Stem);
            if (file is null) continue;
            var walk = ProjectWalk.Of(file);
            string folder = Path.GetDirectoryName(file)!;
            var vars = new Dictionary<string, string[]>(); var events = new Dictionary<string, string[]>();
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var fileOf = new Dictionary<IHavokObject, string>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                fileOf.TryAdd(s.Node, s.File);
                if (s.Node is hkbBehaviorGraph g) { vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []]; events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator); }
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
            bool Writes(IHavokObject n, string variable)
            {
                if (n is not hkbNode node || node.m_variableBindingSet is not { } b || !fileOf.TryGetValue(n, out var f)) return false;
                var t = vars.GetValueOrDefault(f) ?? [];
                return b.m_bindings.Any(x => x.m_variableIndex >= 0 && x.m_variableIndex < t.Length && t[x.m_variableIndex] == variable);
            }
            // features of the subtree an attack event enters, following nested machines' start states
            (bool Driven, bool Layered) Features(hkbGenerator target)
            {
                bool driven = false, layered = false;
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var st = new Stack<IHavokObject>(); st.Push(target);
                while (st.Count > 0)
                {
                    var n = st.Pop(); if (!seen.Add(n)) continue;
                    if (Writes(n, "bAnimationDriven")) driven = true;
                    if (n is hkbModifierGenerator mg && mg.m_modifier is { } mod)
                    {
                        foreach (var m in mod is hkbModifierList ml ? ml.m_modifiers.OfType<IHavokObject>().Append(mod) : [mod])
                        {
                            if (Writes(m, "bAnimationDriven")) driven = true;
                            if ((m as hkbNode)?.m_name?.Contains("AnimationDriven", StringComparison.OrdinalIgnoreCase) == true) driven = true;
                        }
                    }
                    if (n is BSBoneSwitchGenerator or BSBoneSwitchGeneratorBoneData) layered = true;
                    if (n is hkbBlenderGeneratorChild c && (c.m_worldFromModelWeight == 0f || c.m_boneWeights?.m_boneWeights.Any(w => w < 1f) == true)) layered = true;
                    foreach (var ch in Children(n)) st.Push(ch);
                }
                return (driven, layered);
            }
            var byEvent = new Dictionary<string, (bool Driven, bool Layered)>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbStateMachine sm) continue;
                var ev = events.GetValueOrDefault(s.File) ?? [];
                foreach (var t in (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(x => x.m_transitions?.m_transitions ?? [])))
                {
                    if (t.m_eventId < 0 || t.m_eventId >= ev.Length) continue;
                    var target = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_generator;
                    if (target is null) continue;
                    var f = Features(target);
                    var cur = byEvent.GetValueOrDefault(ev[t.m_eventId]);
                    byEvent[ev[t.m_eventId]] = (cur.Driven || f.Driven, cur.Layered || f.Layered);
                }
            }
            var seenA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seenA.Add(a.EventName)) continue;
                    if (!byEvent.TryGetValue(a.EventName, out var f)) continue;
                    string key = $"animation driven {(f.Driven ? "yes" : "no ")} | layered {(f.Layered ? "yes" : "no ")}";
                    var row = cross.GetValueOrDefault(key, (On: 0, Off: 0, ExOn: new List<string>(), ExOff: new List<string>()));
                    if (a.MovingAttack != 0) { row.On++; if (row.ExOn.Count < 6) row.ExOn.Add($"{p.Stem}:{a.EventName}"); }
                    else { row.Off++; if (row.ExOff.Count < 6) row.ExOff.Add($"{p.Stem}:{a.EventName}"); }
                    cross[key] = row;
                }
        }
        var L = new List<string> { "flag 1 / flag 0, one attack event per project" };
        foreach (var (k, v) in cross) { L.Add($"  {k}   {v.On,4} / {v.Off,4}"); L.Add($"      on:  {string.Join(" ", v.ExOn)}"); L.Add($"      off: {string.Join(" ", v.ExOff)}"); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/flagrule.txt", string.Join("\n", L));
    }
}
