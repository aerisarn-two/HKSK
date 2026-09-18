using HKSK.Behavior;
using HKSK.Havok;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// From the clips the set data names, up to the root: which variables the path writes,
// counting the modifiers attached beside the clip as well as the nodes above it.
public sealed class ZzDriven
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var rows = new List<(string Project, string Event, int Flag, bool Blend, SortedSet<string> Vars)>();

        foreach (var p in shipped.Projects)
        {
            var path = cache.FindProjectFile(p.Stem);
            if (path is null) continue;
            var walk = ProjectWalk.Of(path);

            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];

            string FileOf(IHavokObject n) => walk.Steps.FirstOrDefault(s => ReferenceEquals(s.Node, n)).File ?? "";

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var list)) byName[c.m_name] = list = [];
                    list.Add(c);
                }

            void Collect(IHavokObject node, SortedSet<string> into)
            {
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var st = new Stack<IHavokObject>();
                st.Push(node);
                while (st.Count > 0)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbGenerator && !ReferenceEquals(n, node)) continue; // modifiers only, not other branches

                    if (n is hkbNode hn)
                        foreach (var b in hn.m_variableBindingSet?.m_bindings ?? [])
                        {
                            var names = vars.GetValueOrDefault(FileOf(n)) ?? vars.Values.FirstOrDefault() ?? [];
                            if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length) into.Add(names[b.m_variableIndex]);
                        }

                    foreach (var (_, _, c) in HavokEdges.Of(n)) st.Push(c);
                }
            }

            var seenEntry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seenEntry.Add(a.EventName)) continue;

                    var found = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                    if (found.Count == 0) continue;

                    bool blend = false;
                    var written = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var clip in found)
                        foreach (var up in walk.Ancestors(clip))
                        {
                            if (up.Node is hkbBlenderGenerator or BSBoneSwitchGenerator or BSCyclicBlendTransitionGenerator) blend = true;
                            if (up.Node is hkbModifierGenerator m && m.m_modifier is not null) Collect(m.m_modifier, written);
                            if (up.Node is hkbNode n2)
                                foreach (var b in n2.m_variableBindingSet?.m_bindings ?? [])
                                {
                                    var names = vars.GetValueOrDefault(FileOf(up.Node)) ?? vars.Values.FirstOrDefault() ?? [];
                                    if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length) written.Add(names[b.m_variableIndex]);
                                }
                        }

                    rows.Add((p.Stem, a.EventName, a.MovingAttack, blend, written));
                }
        }

        var L = new List<string> { $"attack events: {rows.Count} (flagged {rows.Count(r => r.Flag != 0)})", "" };

        // which variable best separates the two
        var allVars = rows.SelectMany(r => r.Vars).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        L.Add("variables written on the path, flagged with / flagged without / clear with / clear without:");
        foreach (string v in allVars
                     .Select(v => (v, On: rows.Count(r => r.Flag != 0 && r.Vars.Contains(v)), Off: rows.Count(r => r.Flag == 0 && r.Vars.Contains(v))))
                     .Where(x => x.On > 0 || x.Off > 0)
                     .OrderByDescending(x => Math.Abs(x.On / Math.Max(1.0, rows.Count(r => r.Flag != 0)) - x.Off / Math.Max(1.0, rows.Count(r => r.Flag == 0))))
                     .Take(18)
                     .Select(x => $"   {x.v,-28} {x.On,4} / {rows.Count(r => r.Flag != 0) - x.On,4}   {x.Off,4} / {rows.Count(r => r.Flag == 0) - x.Off,4}"))
            L.Add(v);

        L.Add("");
        L.Add("the blend rule, with what the rest write:");
        foreach (var r in rows.Where(r => r.Flag != 0 && !r.Blend).OrderBy(r => r.Project))
            L.Add($"   flagged, no blend  {r.Project,-24} {r.Event,-32} {string.Join(" ", r.Vars)}");
        foreach (var r in rows.Where(r => r.Flag == 0 && r.Blend).OrderBy(r => r.Project))
            L.Add($"   clear, blend       {r.Project,-24} {r.Event,-32} {string.Join(" ", r.Vars)}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/driven.txt", string.Join("\n", L));
    }
}
