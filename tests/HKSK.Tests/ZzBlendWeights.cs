using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// For the attacks of the six flagging projects: the nearest blender whose other arm is a
// movement tree, and what drives the weights -- a constant, or a variable, and which.
public sealed class ZzBlendWeights
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        string[] six = ["DefaultMale", "WerewolfBeastProject", "AtronachStormProject", "NetchProject", "WitchlightProject"];
        var L = new List<string>();

        foreach (string stem in six)
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            var actor = cache.OpenActor(stem);
            string? path = cache.FindProjectFile(stem);
            if (p is null || actor is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                }
            string FileOf(IHavokObject n) => walk.Steps.FirstOrDefault(s => ReferenceEquals(s.Node, n)).File ?? "";

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

            // what drives a blender child's weight: a bound variable, or its constant
            string Drive(hkbBlenderGeneratorChild child, string file)
            {
                var names = vars.GetValueOrDefault(file) ?? [];
                foreach (var b in child.m_variableBindingSet?.m_bindings ?? [])
                    if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length)
                        return $"{b.m_memberPath}<-{names[b.m_variableIndex]}";
                return $"weight={child.m_weight:0.##}";
            }

            bool Movement(IHavokObject arm, IHavokObject from)
            {
                var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var st = new Stack<IHavokObject>(); st.Push(arm);
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { from };
                while (st.Count > 0 && moving.Count < 4)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator c && travelOf.GetValueOrDefault(c.m_name) > 50f) moving.Add(c.m_name);
                    foreach (var k in Kids(n)) st.Push(k);
                }
                return moving.Count >= 4;
            }

            L.Add($"======== {stem}");
            var seenEv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seenEv.Add(a.EventName)) continue;
                var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (clips.Count == 0) continue;

                string found = "";
                foreach (var clip in clips)
                {
                    var q = new Queue<(IHavokObject Node, int Depth)>();
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                    q.Enqueue((clip, 0));
                    while (q.Count > 0 && found.Length == 0)
                    {
                        (IHavokObject at, int d) = q.Dequeue();
                        if (d > 10) continue;
                        foreach (var up in parents.GetValueOrDefault(at) ?? [])
                        {
                            if (up is hkbBlenderGenerator bl)
                            {
                                var ours = bl.m_children.FirstOrDefault(c => c is not null && ReferenceEquals(c, at))
                                           ?? bl.m_children.FirstOrDefault(c => c?.m_generator is not null && ReferenceEquals(c.m_generator, at));
                                var loco = bl.m_children.FirstOrDefault(c => c is not null && !ReferenceEquals(c, ours) && c.m_generator is not null && Movement(c.m_generator, at));
                                if (loco is not null)
                                {
                                    found = $"{bl.m_name} @{d + 1}  ours [{(ours is null ? "?" : Drive(ours, FileOf(bl)))}]  loco [{Drive(loco, FileOf(bl))}]  blenderFlags 0x{bl.m_flags:x} param {bl.m_blendParameter:0.##}";
                                    break;
                                }
                            }
                            if (seen.Add(up)) q.Enqueue((up, d + 1));
                        }
                    }
                    if (found.Length > 0) break;
                }

                L.Add($"   [{a.MovingAttack}] {a.EventName,-34} {(found.Length == 0 ? "-- no movement blender within 10" : found)}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/blendweights.txt", string.Join("\n", L));
    }
}
