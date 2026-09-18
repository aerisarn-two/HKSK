using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
using static HKSK.Tests.ZzAttacks2;
namespace HKSK.Tests;

// probe: is an event-keyed set the files of the clips its swap events enter?
public sealed class ZzIdleSets
{
    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        var cache = SkyrimCache.Load(root);
        var sets = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        bool whole = Environment.GetEnvironmentVariable("WHOLE") is not null;
        var L = new List<string>();
        int n = 0, exact = 0, superset = 0, subset = 0, noEvent = 0, other = 0;
        foreach (var p in sets.Projects)
        {
            if (p.Sets.Sets.Count < 2) continue;
            string projectFile = cache.FindProjectFile(p.Stem)!;
            var g = Graph.Of(projectFile);
            string folder = Path.GetDirectoryName(projectFile)!;
            string File(hkbClipGenerator c)
            {
                string rel = Path.GetFullPath(Path.Combine(folder, c.m_animationName.Replace('\\', '/')));
                return "meshes\\" + Path.GetRelativePath(root, rel).Replace('/', '\\');
            }
            for (int si = 0; si < p.Sets.Sets.Count; si++)
            {
                var set = p.Sets.Sets[si];
                if (set.SwapEvents.Count == 0 || set.HandVariables.Variables.Count > 0) continue;
                n++;
                g.SetHand([]);
                var clips = new HashSet<hkbClipGenerator>(ReferenceEqualityComparer.Instance);
                bool any = false;
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IHavokObject>(); stack.Push(g.Root);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    if (!seen.Add(node)) continue;
                    if (node is hkbStateMachine sm)
                        foreach (string e in set.SwapEvents)
                            foreach (var (_, t) in On(g, sm, e, false))
                                if (sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_generator is { } target)
                                {
                                    any = true;
                                    int nested = (t.m_flags & 0x2000) != 0 ? t.m_toNestedStateId : -1;
                                    if (whole)
                                    {
                                        var s2 = new Stack<IHavokObject>(); s2.Push(target); var seen2 = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                                        while (s2.Count > 0) { var x = s2.Pop(); if (!seen2.Add(x)) continue; if (x is hkbClipGenerator cg) clips.Add(cg); foreach (var ch in g.Children(x)) s2.Push(ch); }
                                    }
                                    else clips.UnionWith(ClipNodesUnder(g, target, e, nested, false));
                                }
                    foreach (var ch in g.Children(node)) stack.Push(ch);
                }
                var built = clips.Select(c => HavokCrc.Triple(File(c))).Select(t => (t.Folder, t.Name)).ToHashSet();
                var shipped = set.Checksums.Triples().Select(t => (t.Folder, t.Name)).ToHashSet();
                string where = $"  {p.Stem} {p.Sets.SetFiles[si]} [{string.Join(",", set.SwapEvents)}] shipped {shipped.Count} built {built.Count}";
                if (!any) { noEvent++; L.Add(where + " no transition"); }
                else if (built.SetEquals(shipped)) exact++;
                else if (shipped.IsSubsetOf(built)) { superset++; L.Add(where + " ⊇  extra: " + string.Join(",", clips.Where(c => !shipped.Contains((HavokCrc.Triple(File(c)).Folder, HavokCrc.Triple(File(c)).Name))).Select(c => c.m_name).Take(6))); }
                else if (built.IsSubsetOf(shipped)) { subset++; L.Add(where + " ⊂"); }
                else { other++; L.Add(where + $" differ, common {built.Intersect(shipped).Count()}"); }
            }
        }
        L.Insert(0, $"event-keyed sets {n}: exact {exact}, built ⊇ shipped {superset}, built ⊂ shipped {subset}, no transition {noEvent}, other {other}");
        System.IO.File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idlesets" + (whole ? "-whole" : "") + (Environment.GetEnvironmentVariable("CHAIN") is null ? "" : "-chain") + ".txt", string.Join("\n", L));
    }
}
