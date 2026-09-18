using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The vanilla flags the speed-parametric rule does not derive: what chooses their clips?
public sealed class ZzMissedFlags
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        foreach (string stem in shipped.Projects.Select(x => x.Stem))
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            var actor = cache.OpenActor(stem);
            string? path = cache.FindProjectFile(stem);
            if (p is null || actor is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var vars = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                }
            string FileOf(IHavokObject n) => walk.Steps.FirstOrDefault(x => ReferenceEquals(x.Node, n)).File ?? "";

            IEnumerable<IHavokObject> Kids(IHavokObject n)
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
                foreach (var kid in Kids(s.Node))
                {
                    if (!parents.TryGetValue(kid, out var l)) parents[kid] = l = [];
                    if (!l.Any(x => ReferenceEquals(x, s.Node))) l.Add(s.Node);
                }

            var travelOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var l)) byName[c.m_name] = l = [];
                    l.Add(c);
                }


            var seen0 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seen0.Add(a.EventName)) continue;
                var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (clips.Count == 0) { L.Add($"   {a.EventName,-34} (clips not found by name)"); continue; }

                // every ancestor that chooses between children on a variable
                var choosers = new List<string>();
                bool varied = false, sprint = false, speedPar = false, strict = false, near = false;
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var q = new Queue<(IHavokObject Node, int Depth)>();
                foreach (var c in clips) { seen.Add(c); q.Enqueue((c, 0)); }

                while (q.Count > 0)
                {
                    (IHavokObject at, int d) = q.Dequeue();
                    if (d > 8) continue;
                    foreach (var up in parents.GetValueOrDefault(at) ?? [])
                    {
                        if (!seen.Add(up)) continue;
                        var names = vars.GetValueOrDefault(FileOf(up)) ?? [];
                        string Bound(string member)
                        {
                            var set = up switch
                            {
                                hkbNode n => n.m_variableBindingSet,
                                hkbBlenderGeneratorChild ch => ch.m_variableBindingSet,
                                _ => null,
                            };
                            foreach (var b in set?.m_bindings ?? [])
                                if (b.m_memberPath == member && b.m_variableIndex >= 0 && b.m_variableIndex < names.Length)
                                    return names[b.m_variableIndex];
                            return "";
                        }

                        // any variable this ancestor reads or writes, and the modifiers it attaches
                        var touched = new List<string>();
                        void Vars(IHavokObject n)
                        {
                            var seenV = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                            var st = new Stack<IHavokObject>(); st.Push(n);
                            while (st.Count > 0)
                            {
                                var at2 = st.Pop();
                                if (!seenV.Add(at2)) continue;
                                if (at2 is hkbGenerator && !ReferenceEquals(at2, n)) continue;
                                if (at2 is hkbNode hn)
                                    foreach (var b in hn.m_variableBindingSet?.m_bindings ?? [])
                                        if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length) touched.Add(names[b.m_variableIndex]);
                                foreach (var (_, _, k) in HavokEdges.Of(at2)) st.Push(k);
                            }
                        }

                        if (up is hkbNode upNode)
                            foreach (var b in upNode.m_variableBindingSet?.m_bindings ?? [])
                                if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length) touched.Add(names[b.m_variableIndex]);
                        if (up is hkbModifierGenerator mg2 && mg2.m_modifier is not null) Vars(mg2.m_modifier);
                        if (touched.Any(v => v.Contains("sprint", StringComparison.OrdinalIgnoreCase))) sprint = true;
                        if (touched.Any(v => string.Equals(v, "IsSprinting", StringComparison.OrdinalIgnoreCase))) strict = true;
                        if (d < 4 && touched.Any(v => v.Contains("sprint", StringComparison.OrdinalIgnoreCase))) near = true;

                        if (up is hkbBlenderGenerator bp && (bp.m_variableBindingSet?.m_bindings ?? [])
                                .Any(b => b.m_memberPath == "blendParameter" && b.m_variableIndex >= 0 && b.m_variableIndex < names.Length
                                          && names[b.m_variableIndex].Contains("speed", StringComparison.OrdinalIgnoreCase)))
                            speedPar = true;

                        if (up is hkbManualSelectorGenerator sel)
                        {
                            string v = Bound("selectedGeneratorIndex");
                            var travels = sel.m_generators.OfType<hkbClipGenerator>()
                                .Select(c2 => travelOf.GetValueOrDefault(c2.m_name)).ToList();
                            if (v.Length > 0 && travels.Count > 1 && travels.Max() - travels.Min() > 50f) varied = true;
                        }
                        else if (up is hkbBlenderGenerator bl2)
                        {
                            string v = Bound("blendParameter");
                            choosers.Add($"@{d + 1} blender({bl2.m_name}) param<-{(v.Length == 0 ? "constant" : v)} flags 0x{bl2.m_flags:x}");
                        }

                        q.Enqueue((up, d + 1));
                    }
                }

                L.Add($"{(a.MovingAttack != 0 ? 1 : 0)}\t{(varied ? 1 : 0)}\t{(sprint ? 1 : 0)}\t{(speedPar ? 1 : 0)}\t{(strict ? 1 : 0)}\t{(near ? 1 : 0)}\t{stem}\t{a.EventName}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/missed.txt", string.Join("\n", L));
    }
}
