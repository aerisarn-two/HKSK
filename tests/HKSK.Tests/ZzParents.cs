using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// How often does a node have more than one parent, and does it change the blend answer?
// ProjectWalk keeps one parent per node, so a clip shared by a blender and a plain state
// is only ever seen one way round.
public sealed class ZzParents
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        int sharedNodes = 0, sharedClips = 0, totalNodes = 0, totalClips = 0;
        var byKind = new SortedDictionary<string, (int Total, int Shared)>(StringComparer.Ordinal);
        var rows = new List<(string Project, string Event, int Flag, bool OnePath, bool AnyPath, bool AllPaths, int Nearest)>();

        foreach (var p in shipped.Projects)
        {
            var path = cache.FindProjectFile(p.Stem);
            if (path is null) continue;
            var walk = ProjectWalk.Of(path);

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph { m_rootGenerator: not null } g) roots.TryAdd(s.File, g.m_rootGenerator);
            string folder = Path.GetDirectoryName(path)!;

            // every parent of every node, across behaviour references
            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            var nodes = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                nodes.Add(s.Node);
                IEnumerable<IHavokObject> kids = s.Node is hkbBehaviorReferenceGenerator r
                    ? HavokPath.Resolve(folder, r.m_behaviorName) is { } q && roots.TryGetValue(Path.GetFullPath(q), out var root) ? [root] : []
                    : HavokEdges.Of(s.Node).Select(e => e.Item3);

                foreach (var kid in kids)
                {
                    if (!parents.TryGetValue(kid, out var list)) parents[kid] = list = [];
                    if (!list.Any(x => ReferenceEquals(x, s.Node))) list.Add(s.Node);
                }
            }

            foreach (var n in nodes)
            {
                int count = parents.GetValueOrDefault(n)?.Count ?? 0;
                string kind = n.GetType().Name;
                var had = byKind.GetValueOrDefault(kind);
                byKind[kind] = (had.Total + 1, had.Shared + (count > 1 ? 1 : 0));

                if (n is not hkbNode) continue;
                totalNodes++;
                if (count > 1) sharedNodes++;
                if (n is hkbClipGenerator) { totalClips++; if (count > 1) sharedClips++; }
            }

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var list)) byName[c.m_name] = list = [];
                    list.Add(c);
                }

            // a blender, by the Havok hierarchy: hkbBlenderGenerator and what derives from
            // it (hkbPoseMatchingGenerator). BSBoneSwitchGenerator and
            // BSCyclicBlendTransitionGenerator derive from hkbGenerator, not from this.
            static bool IsBlend(IHavokObject n) => n is hkbBlenderGenerator;

            static bool IsBlendNode(IHavokObject n) => IsBlend(n);

            // Blend(n): some path from n up to a root passes a blend.
            // Free(n):  some path from n up to a root passes none.
            // Both are monotone, so a fixpoint over the parent map settles them with any
            // number of parents and through the cycles a behaviour has.
            var blendUp = new Dictionary<IHavokObject, bool>(ReferenceEqualityComparer.Instance);
            var freeUp = new Dictionary<IHavokObject, bool>(ReferenceEqualityComparer.Instance);
            foreach (var n in nodes)
            {
                blendUp[n] = false;
                freeUp[n] = (parents.GetValueOrDefault(n)?.Count ?? 0) == 0;
            }

            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var n in nodes)
                {
                    var ups = parents.GetValueOrDefault(n);
                    if (ups is null) continue;

                    bool blend = ups.Any(u => IsBlendNode(u) || blendUp.GetValueOrDefault(u));
                    bool free = ups.Any(u => !IsBlendNode(u) && freeUp.GetValueOrDefault(u));

                    if (blend != blendUp[n]) { blendUp[n] = blend; changed = true; }
                    if (free != freeUp[n]) { freeUp[n] = free; changed = true; }
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seen.Add(a.EventName)) continue;
                    var found = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                    if (found.Count == 0) continue;

                    bool onePath = false, anyPath = false, allPaths = true;
                    int nearest = int.MaxValue;
                    foreach (var clip in found)
                    {
                        foreach (var up in walk.Ancestors(clip)) if (IsBlend(up.Node)) onePath = true;
                        anyPath |= blendUp.GetValueOrDefault(clip);
                        allPaths &= !freeUp.GetValueOrDefault(clip);

                        // how far up the closest blender is, over every parent
                        var queue = new Queue<(IHavokObject Node, int Depth)>();
                        var seenUp = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                        queue.Enqueue((clip, 0));
                        while (queue.Count > 0)
                        {
                            (IHavokObject at, int depth) = queue.Dequeue();
                            if (depth >= nearest || depth > 16) continue;
                            foreach (var up in parents.GetValueOrDefault(at) ?? [])
                            {
                                if (!seenUp.Add(up)) continue;
                                if (IsBlend(up)) { nearest = Math.Min(nearest, depth + 1); continue; }
                                queue.Enqueue((up, depth + 1));
                            }
                        }
                    }

                    rows.Add((p.Stem, a.EventName, a.MovingAttack, onePath, anyPath, allPaths, nearest == int.MaxValue ? -1 : nearest));
                }
        }

        L.Add($"nodes {totalNodes}, of which more than one parent: {sharedNodes} ({100.0 * sharedNodes / Math.Max(1, totalNodes):0.0}%)");
        L.Add($"clip generators {totalClips}, shared: {sharedClips} ({100.0 * sharedClips / Math.Max(1, totalClips):0.0}%)");
        L.Add("");
        L.Add("shared by type (shared / total), the types an upward walk cares about:");
        foreach (var (k, v) in byKind.OrderByDescending(x => x.Value.Shared).Take(22))
            L.Add($"   {k,-42} {v.Shared,6} / {v.Total,6}");
        L.Add("");
        L.Add("blend between the clip and a root:");
        foreach (var (label, pick) in new (string, Func<(string, string, int, bool, bool, bool, int), bool>)[]
                 {
                     ("the walk's one path ", r => r.Item4),
                     ("any path            ", r => r.Item5),
                     ("every path          ", r => r.Item6),
                 })
        {
            int t1 = rows.Count(r => r.Flag != 0 && pick(r)), f1 = rows.Count(r => r.Flag != 0 && !pick(r));
            int t0 = rows.Count(r => r.Flag == 0 && pick(r)), f0 = rows.Count(r => r.Flag == 0 && !pick(r));
            L.Add($"   {label}  flagged {t1,3} yes / {f1,3} no     clear {t0,3} yes / {f0,3} no");
        }

        L.Add("");
        L.Add("how far up the nearest blender is:");
        foreach (int d in new[] { 1, 2, 3, 4, 6, 8 })
        {
            int t1 = rows.Count(r => r.Flag != 0 && r.Nearest >= 0 && r.Nearest <= d);
            int t0 = rows.Count(r => r.Flag == 0 && r.Nearest >= 0 && r.Nearest <= d);
            L.Add($"   within {d,2} levels:  flagged {t1,3} of {rows.Count(r => r.Flag != 0),3}    clear {t0,3} of {rows.Count(r => r.Flag == 0),3}");
        }

        L.Add("");
        L.Add("nearest blender per flagged event:");
        foreach (var r in rows.Where(r => r.Flag != 0).OrderBy(r => r.Project).ThenBy(r => r.Nearest))
            L.Add($"   {r.Project,-24} {r.Event,-34} {(r.Nearest < 0 ? "none" : r.Nearest.ToString())}");
        L.Add("");
        L.Add("clear events with a blender within 2:");
        foreach (var r in rows.Where(r => r.Flag == 0 && r.Nearest is >= 0 and <= 2).OrderBy(r => r.Project))
            L.Add($"   {r.Project,-24} {r.Event,-34} {r.Nearest}");

        L.Add("");
        L.Add("by project, flagged and clear, over every path:");
        foreach (var g in rows.GroupBy(r => r.Project).OrderBy(g => g.Key))
        {
            int f1 = g.Count(r => r.Flag != 0), f1b = g.Count(r => r.Flag != 0 && r.AnyPath);
            int f0 = g.Count(r => r.Flag == 0), f0b = g.Count(r => r.Flag == 0 && r.AnyPath);
            if (f1 > 0 || f0b > 0) L.Add($"   {g.Key,-26} flagged {f1b}/{f1} blended    clear {f0b}/{f0} blended");
        }

        L.Add("");
        L.Add("flagged with no blender above them, over any path:");
        foreach (var r in rows.Where(r => r.Flag != 0 && !r.AnyPath).OrderBy(r => r.Project))
            L.Add($"   {r.Project,-24} {r.Event}");

        L.Add("");
        L.Add("events where the one path and all paths disagree:");
        foreach (var r in rows.Where(r => r.OnePath != r.AnyPath || r.AnyPath != r.AllPaths).OrderBy(r => r.Project))
            L.Add($"   [{r.Flag}] {r.Project,-24} {r.Event,-34} one={r.OnePath,-5} any={r.AnyPath,-5} all={r.AllPaths}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/parents.txt", string.Join("\n", L));
    }
}
