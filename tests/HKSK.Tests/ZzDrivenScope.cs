using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// bAnimationDriven starts at 0 and is raised while a branch is active. So: for an attack's
// clip, is it inside the scope of something that raises it?
public sealed class ZzDrivenScope
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        int on1 = 0, on0 = 0, off1 = 0, off0 = 0;
        var table = new List<(bool Flag, bool A, bool R, float Travel, string Project, string Event)>();

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            string? path = cache.FindProjectFile(p.Stem);
            if (path is null || actor is null) continue;
            var travelOf = actor.Clips
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rotIndexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                    var names = g.m_data?.m_stringData?.m_variableNames ?? [];
                    for (int i = 0; i < names.Count; i++)
                        if (string.Equals(names[i], "bAnimationDriven", StringComparison.OrdinalIgnoreCase)) indexOf[s.File] = i;
                        else if (string.Equals(names[i], "bAllowRotation", StringComparison.OrdinalIgnoreCase)) rotIndexOf[s.File] = i;
                }

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

            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
                foreach (var kid in Children(s.Node))
                {
                    if (!parents.TryGetValue(kid, out var list)) parents[kid] = list = [];
                    if (!list.Any(x => ReferenceEquals(x, s.Node))) list.Add(s.Node);
                }

            // what raises bAnimationDriven, and the generator it covers
            var scopes = new List<(string Who, IHavokObject Scope)>();
            var rotScopes = new List<(string Who, IHavokObject Scope)>();
            foreach (var s in walk.Steps)
            {
                bool isRot = false;
                if (!indexOf.TryGetValue(s.File, out int at))
                {
                    if (!rotIndexOf.TryGetValue(s.File, out at)) continue;
                    isRot = true;
                }
                else if (rotIndexOf.TryGetValue(s.File, out int rotAt) && s.Node is hkbNode probe
                         && (probe.m_variableBindingSet?.m_bindings ?? []).Any(b => b.m_variableIndex == rotAt
                             && (b.m_memberPath.StartsWith("bIsActive", StringComparison.Ordinal) || b.m_memberPath == "isActive")))
                {
                    // this node writes bAllowRotation; record it there as well
                    if (s.Node is hkbStateMachine sm2) rotScopes.Add(($"{sm2.m_name}", sm2));
                    else
                    {
                        IHavokObject up2 = s.Node;
                        for (int i = 0; i < 8; i++)
                        {
                            var u = parents.GetValueOrDefault(up2)?.FirstOrDefault();
                            if (u is null) break;
                            if (u is hkbModifierGenerator mg2) { if (mg2.m_generator is not null) rotScopes.Add(($"{((hkbNode)s.Node).m_name} via {mg2.m_name}", mg2.m_generator)); break; }
                            up2 = u;
                        }
                    }
                }
                if (s.Node is not hkbNode node) continue;
                bool writes = (node.m_variableBindingSet?.m_bindings ?? [])
                    .Any(b => b.m_variableIndex == at && (b.m_memberPath.StartsWith("bIsActive", StringComparison.Ordinal) || b.m_memberPath == "isActive"));
                if (!writes) continue;

                var into = isRot ? rotScopes : scopes;
                if (node is hkbStateMachine) { into.Add(($"{node.GetType().Name}({node.m_name})", node)); continue; }

                // a modifier: the generator that attaches it
                IHavokObject at2 = node;
                for (int i = 0; i < 8; i++)
                {
                    var up = parents.GetValueOrDefault(at2)?.FirstOrDefault();
                    if (up is null) break;
                    if (up is hkbModifierGenerator mg) { if (mg.m_generator is not null) into.Add(($"{node.m_name} via {mg.m_name}", mg.m_generator)); break; }
                    at2 = up;
                }
            }

            var covered = new Dictionary<IHavokObject, List<string>>(ReferenceEqualityComparer.Instance);
            var rotCovered = new Dictionary<IHavokObject, List<string>>(ReferenceEqualityComparer.Instance);
            foreach ((bool rot, string who, var scope) in scopes.Select(x => (false, x.Who, x.Scope)).Concat(rotScopes.Select(x => (true, x.Who, x.Scope))))
            {
                var target = rot ? rotCovered : covered;
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var st = new Stack<IHavokObject>();
                st.Push(scope);
                while (st.Count > 0)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator)
                    {
                        if (!target.TryGetValue(n, out var list)) target[n] = list = [];
                        if (!list.Contains(who)) list.Add(who);
                    }
                    foreach (var c in Children(n)) st.Push(c);
                }
            }

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var list)) byName[c.m_name] = list = [];
                    list.Add(c);
                }

            var seenEv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seenEv.Add(a.EventName)) continue;
                var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (clips.Count == 0) continue;

                bool driven = clips.Any(c => covered.ContainsKey(c));
                bool rotate = clips.Any(c => rotCovered.ContainsKey(c));
                if (a.MovingAttack != 0) { if (driven) on1++; else on0++; } else { if (driven) off1++; else off0++; }
                var named = a.Clips.Where(travelOf.ContainsKey).ToList();
                float travel = named.Count == 0 ? -1f : named.Max(c => travelOf[c]);
                table.Add((a.MovingAttack != 0, driven, rotate, travel, p.Stem, a.EventName));
            }
        }

        L.Add("");
        L.Add($"attacks measured: {table.Count} ({table.Count(r => r.Flag)} flagged)");
        L.Add("A = bAnimationDriven raised over the clip, R = bAllowRotation raised over the clip");
        L.Add("");
        L.Add($"   {"A",-5} {"R",-5} {"flagged",8} {"clear",8}");
        foreach (var g in table.GroupBy(r => (r.A, r.R)).OrderBy(g => g.Key.A).ThenBy(g => g.Key.R))
            L.Add($"   {g.Key.A,-5} {g.Key.R,-5} {g.Count(r => r.Flag),8} {g.Count(r => !r.Flag),8}");

        L.Add("");
        L.Add("every boolean of the two, as a predictor of the flag:");
        (string Name, Func<(bool Flag, bool A, bool R, float Travel, string Project, string Event), bool> Pick)[] tests =
        [
            ("A", r => r.A), ("R", r => r.R),
            ("travel <= 5", r => r.Travel is >= 0 and <= 5f),
            ("not A and travel <= 5", r => !r.A && r.Travel is >= 0 and <= 5f),
            ("not A and not R and travel <= 5", r => !r.A && !r.R && r.Travel is >= 0 and <= 5f),
            ("travel <= 5 (A either way)", r => r.Travel is >= 0 and <= 5f),
            ("A or travel <= 5", r => r.A || r.Travel is >= 0 and <= 5f),
            ("not A", r => !r.A), ("not R", r => !r.R),
            ("A and R", r => r.A && r.R), ("A or R", r => r.A || r.R),
            ("not A and not R  (StartMotionDriven)", r => !r.A && !r.R),
            ("not A or not R", r => !r.A || !r.R),
            ("not A and R      (StartAllowRotation)", r => !r.A && r.R),
            ("A and not R", r => r.A && !r.R),
            ("A xor R", r => r.A ^ r.R),
        ];
        foreach ((string name, var pick) in tests)
        {
            int tp = table.Count(r => r.Flag && pick(r)), fn = table.Count(r => r.Flag && !pick(r));
            int fp = table.Count(r => !r.Flag && pick(r)), tn = table.Count(r => !r.Flag && !pick(r));
            L.Add($"   {name,-38} true: {tp,3} flagged / {fp,3} clear     false: {fn,3} flagged / {tn,3} clear     wrong {fn + fp,4}");
        }

        L.Add("");
        L.Add("the joint table: A = animation-driven, R = allow-rotation, T = named clips' travel");
        foreach (var g in table.GroupBy(r => (r.A, r.R, Bucket: r.Travel < 0 ? "?" : r.Travel <= 5f ? "<=5" : r.Travel <= 100f ? "5..100" : ">100"))
                     .OrderBy(g => g.Key.A).ThenBy(g => g.Key.R).ThenBy(g => g.Key.Bucket, StringComparer.Ordinal))
            L.Add($"   A={g.Key.A,-5} R={g.Key.R,-5} T {g.Key.Bucket,-7} flagged {g.Count(r => r.Flag),3}   clear {g.Count(r => !r.Flag),3}");

        L.Add("");
        L.Add("attacks where bAllowRotation is raised:");
        foreach (var r in table.Where(r => r.R).OrderByDescending(r => r.Flag).ThenBy(r => r.Project))
            L.Add($"   [{(r.Flag ? 1 : 0)}] A={r.A,-5} {r.Project,-24} {r.Event}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/drivenscope.txt", string.Join("\n", L));
    }
}
