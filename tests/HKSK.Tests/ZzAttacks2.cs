using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// probe: attacks from the graph, per set, honouring the set's hand variables
public sealed class ZzAttacks2
{
    static readonly string[] HandNames = ["iRightHandType", "iLeftHandType", "iRightHandEquipped", "iLeftHandEquipped", "iWantMountedWeaponAnims", "bWantMountedWeaponAnims"];

    public sealed class Graph
    {
        public required ProjectWalk Walk;
        public required string Folder;
        public readonly Dictionary<IHavokObject, string> FileOf = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<string, Variables> Tables = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, IHavokObject> FileRoot = new(StringComparer.OrdinalIgnoreCase);
        public hkbGenerator Root = null!;
        public Properties? Properties;
        public readonly Dictionary<string, int> Hand = new(StringComparer.OrdinalIgnoreCase);

        public static Graph Of(string projectFile)
        {
            var g = new Graph { Walk = ProjectWalk.Of(projectFile), Folder = Path.GetDirectoryName(projectFile)!, Properties = HKSK.Engine.Properties.OfProject(projectFile) };
            var space = new VariableSpace();
            foreach (var s in g.Walk.Steps)
            {
                g.FileOf.TryAdd(s.Node, s.File);
                if (s.Node is hkbBehaviorGraph bg)
                {
                    if (!g.Tables.ContainsKey(s.File)) g.Tables[s.File] = Variables.Of(bg, space);
                    if (bg.m_rootGenerator is not null)
                    {
                        g.FileRoot.TryAdd(s.File, bg.m_rootGenerator);
                        g.Root ??= bg.m_rootGenerator;
                    }
                }
            }
            return g;
        }

        public void SetHand(IEnumerable<HandVariable> vars)
        {
            Hand.Clear();
            foreach (var v in vars) Hand[v.Name] = v.Min;
            foreach (var t in Tables.Values)
                foreach (string n in HandNames) t.Set(n, Hand.GetValueOrDefault(n));
        }

        public IEnumerable<IHavokObject> Children(IHavokObject n)
        {
            if (n is hkbBehaviorReferenceGenerator r)
            {
                string? path = HavokPath.Resolve(Folder, r.m_behaviorName);
                if (path is not null && FileRoot.TryGetValue(Path.GetFullPath(path), out var fr)) yield return fr;
                yield break;
            }
            if (n is hkbManualSelectorGenerator ms && Bound(ms, "selectedGeneratorIndex") is { } i)
            {
                if (i >= 0 && i < ms.m_generators.Count && ms.m_generators[i] is { } only) yield return only;
                yield break;
            }
            foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
        }

        // the value a member is bound to, when it is bound to a hand variable
        public int? Bound(hkbNode n, string member)
        {
            if (n.m_variableBindingSet is not { } set || !FileOf.TryGetValue(n, out string? file) || !Tables.TryGetValue(file, out var t)) return null;
            foreach (var b in set.m_bindings)
            {
                if (b.m_memberPath != member || b.m_variableIndex < 0 || b.m_variableIndex >= t.Count) continue;
                string name = t.NameOf(b.m_variableIndex);
                if (HandNames.Contains(name) || Environment.GetEnvironmentVariable("ALLBOUND") is not null) return t.AsInt(b.m_variableIndex);
            }
            return null;
        }

        public string? EventName(hkbStateMachine sm, int id) =>
            FileOf.TryGetValue(sm, out string? f) && Tables.TryGetValue(f, out var t) ? t.EventNameOf(id) : null;

        public bool Allows(hkbStateMachine sm, hkbStateMachineTransitionInfo info, bool conditions)
        {
            if ((info.m_flags & 0x20) != 0) return false;
            if (!conditions || info.m_condition is not hkbExpressionCondition ec) return true;
            if (!HandNames.Any(h => ec.m_expression.Contains(h))) return true;
            return !FileOf.TryGetValue(sm, out string? f) || !Tables.TryGetValue(f, out var t) || Transitions.Holds(ec, t);
        }
    }

    // transitions on an event out of each state: its own if it has any, else the wildcards
    public static IEnumerable<(hkbStateMachineStateInfo From, hkbStateMachineTransitionInfo Info)> On(Graph g, hkbStateMachine sm, string e, bool conditions)
    {
        bool Match(hkbStateMachineTransitionInfo t, int from) =>
            string.Equals(g.EventName(sm, t.m_eventId), e, StringComparison.OrdinalIgnoreCase) && t.m_toStateId != from && g.Allows(sm, t, conditions);
        var wild = sm.m_wildcardTransitions?.m_transitions ?? [];
        foreach (var st in sm.m_states)
        {
            var local = (st.m_transitions?.m_transitions ?? []).Where(t => Match(t, st.m_stateId)).ToList();
            foreach (var t in local.Count > 0 ? local : wild.Where(t => Match(t, st.m_stateId)))
                yield return (st, t);
        }
    }

    static List<string> ClipsUnder(Graph g, hkbGenerator start, string e, int nested, bool conditions) => ClipNodesUnder(g, start, e, nested, conditions).Select(c => c.m_name).ToList();

    public static List<hkbClipGenerator> ClipNodesUnder(Graph g, hkbGenerator start, string e, int nested, bool conditions)
    {
        var names = new List<hkbClipGenerator>();
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!seen.Add(n)) continue;
            if (n is hkbClipGenerator c) names.Add(c);
            if (n is hkbStateMachine sm)
            {
                hkbGenerator? Gen(int id) => sm.m_states.FirstOrDefault(x => x.m_stateId == id)?.m_generator;
                var entry = new List<int>();
                if (nested >= 0) { if (Gen(nested) is not null) entry.Add(nested); nested = -1; }
                if (entry.Count == 0) entry.AddRange(On(g, sm, e, conditions).Select(x => x.Info.m_toStateId).Where(id => Gen(id) is not null));
                if (entry.Count == 0) entry.Add(g.Bound(sm, "startStateId") ?? sm.m_startStateId);
                if (Environment.GetEnvironmentVariable("CHAIN") is not null)
                {
                    var q = new Queue<int>(entry); var got = new HashSet<int>(entry);
                    while (q.Count > 0)
                    {
                        int cur = q.Dequeue(); var st = sm.m_states.FirstOrDefault(x => x.m_stateId == cur);
                        foreach (var t in st?.m_transitions?.m_transitions ?? [])
                            if ((t.m_flags & 0x20) == 0 && got.Add(t.m_toStateId)) q.Enqueue(t.m_toStateId);
                    }
                    entry = got.ToList();
                }
                foreach (int id in entry) if (Gen(id) is { } eg) stack.Push(eg);
                continue;
            }
            if (Environment.GetEnvironmentVariable("BLENDS") is not null && n is hkbBlenderGenerator bl)
            {
                var run = ActiveGenerators.Evaluate(bl, g.Walk, tables => { foreach (var tb in tables.Values) foreach (var h in HandNames) tb.Set(h, g.Hand.GetValueOrDefault(h)); }, g.Properties, Events.Of(e));
                var active = run.Active.Where(a => a.Weight > 0).Select(a => a.Generator).ToList();
                if (active.OfType<hkbClipGenerator>().Any())
                {
                    names.AddRange(active.OfType<hkbClipGenerator>());
                    continue;
                }
            }
            foreach (var child in g.Children(n)) stack.Push(child);
        }
        return names;
    }

    static HashSet<string>? AttackClips(Graph g, string e, bool conditions)
    {
        HashSet<string>? clips = null;
        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IHavokObject>();
        stack.Push(g.Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!seen.Add(n)) continue;
            if (n is hkbStateMachine sm)
                foreach (var (_, t) in On(g, sm, e, conditions))
                    if (sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_generator is { } target)
                    {
                        clips ??= new(StringComparer.OrdinalIgnoreCase);
                        int nested = (t.m_flags & 0x2000) != 0 ? t.m_toNestedStateId : -1;
                        if (Environment.GetEnvironmentVariable("ENGINE") is null)
                            clips.UnionWith(ClipsUnder(g, target, e, nested, conditions));
                        else
                        {
                            hkbGenerator from = target;
                            if (nested >= 0 && target is hkbStateMachine tsm && tsm.m_states.FirstOrDefault(x => x.m_stateId == nested)?.m_generator is { } ng) from = ng;
                            var run = ActiveGenerators.Evaluate(from, g.Walk, tables => { foreach (var tb in tables.Values) foreach (var h in HandNames) tb.Set(h, g.Hand.GetValueOrDefault(h)); }, g.Properties, Events.Of(e));
                            var got = run.Active.Where(a => a.Weight > 0).Select(a => a.Generator).OfType<hkbClipGenerator>().Select(c => c.m_name).ToList();
                            clips.UnionWith(got.Count > 0 ? got : ClipsUnder(g, target, e, nested, conditions));
                        }
                    }
            foreach (var child in g.Children(n)) stack.Push(child);
        }
        return clips;
    }

    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        SkyrimCache cache = SkyrimCache.Load(root);
        var sets = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        bool conditions = Environment.GetEnvironmentVariable("CONDITIONS") is not null;
        var L = new List<string>();
        var perProject = new List<string>();
        int attacks = 0, exact = 0, subset = 0, superset = 0, missing = 0;

        foreach (var p in sets.Projects)
        {
            string? projectFile = cache.FindProjectFile(p.Stem);
            if (projectFile is null) continue;
            var g = Graph.Of(projectFile);
            int pa = 0, pe = 0;
            for (int si = 0; si < p.Sets.Sets.Count; si++)
            {
                var set = p.Sets.Sets[si];
                g.SetHand(set.HandVariables.Variables);
                foreach (var a in set.Attacks.Attacks)
                {
                    attacks++; pa++;
                    var shipped = a.Clips.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    string where = $"  {p.Stem} {p.Sets.SetFiles[si]} {a.EventName}";
                    var built = AttackClips(g, a.EventName, conditions);
                    if (built is null) { missing++; L.Add($"{where}: no transition; shipped {string.Join(",", a.Clips)}"); continue; }
                    if (built.SetEquals(shipped)) { exact++; pe++; }
                    else if (shipped.IsSubsetOf(built)) { superset++; L.Add($"{where}: built ⊇ shipped {string.Join(",", a.Clips)} | extra {string.Join(",", built.Except(shipped).Take(8))}"); }
                    else if (built.IsSubsetOf(shipped)) { subset++; L.Add($"{where}: built ⊂ shipped; built {string.Join(",", built)} | shipped {string.Join(",", a.Clips)}"); }
                    else L.Add($"{where}: differ; built {string.Join(",", built.Take(8))} | shipped {string.Join(",", a.Clips)}");
                }
            }
            if (pa > 0) perProject.Add($"{p.Stem,-32} {pe,4} of {pa,4}");
        }
        L.Insert(0, $"attacks {attacks}: exact {exact}, built ⊇ shipped {superset}, built ⊂ shipped {subset}, no transition {missing}, other {attacks - exact - superset - subset - missing}");
        L.InsertRange(1, perProject);
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/attacks2" + (conditions ? "-cond" : "") + (Environment.GetEnvironmentVariable("ENGINE") is null ? "" : "-engine") + (Environment.GetEnvironmentVariable("ALLBOUND") is null ? "" : "-allbound") + (Environment.GetEnvironmentVariable("BLENDS") is null ? "" : "-blends") + ".txt", string.Join("\n", L));
    }
}
