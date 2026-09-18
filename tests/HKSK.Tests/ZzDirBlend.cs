using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Is the attack's clip blended by the movement direction? Structural, not by name: a
// blender whose weight is bound to Direction (or Speed / SampledSpeed), anywhere above the
// clip, over every parent.
public sealed class ZzDirBlend
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var rows = new List<(string Project, string Event, int Flag, int Depth, string Who)>();

        foreach (var p in shipped.Projects)
        {
            var path = cache.FindProjectFile(p.Stem);
            if (path is null) continue;
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

            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                IEnumerable<IHavokObject> kids = s.Node is hkbBehaviorReferenceGenerator r
                    ? HavokPath.Resolve(folder, r.m_behaviorName) is { } q && roots.TryGetValue(Path.GetFullPath(q), out var root) ? [root] : []
                    : HavokEdges.Of(s.Node).Select(e => e.Item3);

                foreach (var kid in kids)
                {
                    if (!parents.TryGetValue(kid, out var list)) parents[kid] = list = [];
                    if (!list.Any(x => ReferenceEquals(x, s.Node))) list.Add(s.Node);
                }
            }

            // a blender steered by how the character is moving
            bool Directional(IHavokObject n)
            {
                if (n is not hkbBlenderGenerator and not BSBoneSwitchGenerator and not BSCyclicBlendTransitionGenerator) return false;

                var names = vars.GetValueOrDefault(FileOf(n)) ?? [];
                var stack = new Stack<IHavokObject>();
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                stack.Push(n);

                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    if (!seen.Add(at)) continue;
                    if (at is hkbGenerator && !ReferenceEquals(at, n) && at is not hkbBlenderGeneratorChild) continue;

                    if (at is hkbNode node)
                        foreach (var b in node.m_variableBindingSet?.m_bindings ?? [])
                            if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length
                                && names[b.m_variableIndex] is "Direction" or "Speed" or "SampledSpeed" or "SpeedSampled"
                                    or "iSyncTurnState" or "TurnDelta")
                                return true;

                    foreach (var (_, _, c) in HavokEdges.Of(at)) stack.Push(c);
                }

                return false;
            }

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var list)) byName[c.m_name] = list = [];
                    list.Add(c);
                }

            var isDir = new Dictionary<IHavokObject, bool>(ReferenceEqualityComparer.Instance);
            bool Dir(IHavokObject n)
            {
                if (!isDir.TryGetValue(n, out bool v)) isDir[n] = v = Directional(n);
                return v;
            }

            var seenEvent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seenEvent.Add(a.EventName)) continue;
                var found = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (found.Count == 0) continue;

                int best = int.MaxValue;
                string who = "";

                foreach (var clip in found)
                {
                    var queue = new Queue<(IHavokObject Node, int Depth)>();
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                    queue.Enqueue((clip, 0));

                    while (queue.Count > 0)
                    {
                        (IHavokObject at, int depth) = queue.Dequeue();
                        if (depth >= 6) continue;

                        foreach (var up in parents.GetValueOrDefault(at) ?? [])
                        {
                            if (!seen.Add(up)) continue;
                            if (Dir(up))
                            {
                                if (depth + 1 < best)
                                {
                                    best = depth + 1;
                                    who = up is hkbNode hn ? hn.m_name ?? up.GetType().Name : up.GetType().Name;
                                }
                                continue;
                            }
                            queue.Enqueue((up, depth + 1));
                        }
                    }
                }

                rows.Add((p.Stem, a.EventName, a.MovingAttack, best == int.MaxValue ? -1 : best, who));
            }
        }

        var L = new List<string> { $"attack events with clips found: {rows.Count} (flagged {rows.Count(r => r.Flag != 0)})", "" };

        foreach (int limit in new[] { 1, 2, 3, 4, 6 })
        {
            bool Hit((string, string, int, int, string) r) => r.Item4 >= 0 && r.Item4 <= limit;
            int t1 = rows.Count(r => r.Flag != 0 && Hit(r)), f1 = rows.Count(r => r.Flag != 0 && !Hit(r));
            int t0 = rows.Count(r => r.Flag == 0 && Hit(r)), f0 = rows.Count(r => r.Flag == 0 && !Hit(r));
            L.Add($"  a directional blend within {limit} levels:  flagged {t1,3} yes / {f1,3} no    clear {t0,3} yes / {f0,3} no");
        }

        L.Add("");
        L.Add("flagged, no directional blend within 6:");
        foreach (var r in rows.Where(r => r.Flag != 0 && r.Depth < 0).OrderBy(r => r.Project))
            L.Add($"   {r.Project,-24} {r.Event}");
        L.Add("");
        L.Add("clear, with a directional blend:");
        foreach (var r in rows.Where(r => r.Flag == 0 && r.Depth >= 0).OrderBy(r => r.Depth).ThenBy(r => r.Project))
            L.Add($"   {r.Project,-24} {r.Event,-34} at {r.Depth}  {r.Who}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/dirblend.txt", string.Join("\n", L));
    }
}
