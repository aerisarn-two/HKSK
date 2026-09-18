using System.Reflection;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMirror3
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        // variable name -> (flag1 attacks with it on the path, flag0 attacks with it)
        var with = new Dictionary<string, (int On, int Off)>();
        int on = 0, off = 0;
        foreach (var p in shipped.Projects)
        {
            var file = cache.FindProjectFile(p.Stem);
            if (file is null) continue;
            var walk = ProjectWalk.Of(file);
            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
            var clipSteps = walk.Steps.Where(s => s.Node is hkbClipGenerator).GroupBy(s => ((hkbClipGenerator)s.Node).m_name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            HashSet<string> Mentioned(ProjectStep clip)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in walk.Ancestors(clip.Node).Append(clip))
                {
                    var table = vars.GetValueOrDefault(a.File) ?? [];
                    void Scan(IHavokObject node)
                    {
                        if (node is hkbNode n && n.m_variableBindingSet is { } b)
                            foreach (var x in b.m_bindings)
                                if (x.m_variableIndex >= 0 && x.m_variableIndex < table.Length) names.Add($"{x.m_memberPath}<-{table[x.m_variableIndex]}");
                        if (node is hkbEvaluateExpressionModifier em && em.m_expressions is { } ex)
                            foreach (var e in ex.m_expressionsData) names.Add("expr:" + e.m_expression);
                    }
                    Scan(a.Node);
                    names.Add("type:" + a.Node.GetType().Name);
                    if (a.Node is hkbBlenderGenerator bl) names.Add($"blend flags:{bl.m_flags}");
                    if (a.Node is hkbModifierGenerator mg && mg.m_modifier is { } mod)
                    {
                        Scan(mod);
                        if (mod is hkbModifierList ml) foreach (var m in ml.m_modifiers) if (m is not null) { Scan(m); names.Add("modifier:" + m.GetType().Name); }
                        names.Add("modifier:" + mod.GetType().Name);
                    }
                }
                return names;
            }

            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    var steps = a.Clips.SelectMany(c => clipSteps.GetValueOrDefault(c) ?? []).ToList();
                    if (steps.Count == 0) continue;
                    var names = steps.Select(Mentioned).Aggregate((x, y) => { x.IntersectWith(y); return x; });
                    if (a.Mirrored != 0) on++; else off++;
                    foreach (var n in names)
                    {
                        var c = with.GetValueOrDefault(n);
                        with[n] = a.Mirrored != 0 ? (c.On + 1, c.Off) : (c.On, c.Off + 1);
                    }
                }
        }
        var L = new List<string> { $"attacks with clips found: flag 1 {on}, flag 0 {off}. Best separators (on-path in flag1 / flag0):" };
        foreach (var kv in with.OrderByDescending(k => (double)k.Value.On / on - (double)k.Value.Off / off).Take(15))
            L.Add($"  +{kv.Value.On,4}/{on}  {kv.Value.Off,4}/{off}  {kv.Key}");
        L.Add("node types and blend flags:");
        foreach (var kv in with.Where(k => k.Key.StartsWith("type:") || k.Key.StartsWith("blend")).OrderByDescending(k => (double)k.Value.On / on - (double)k.Value.Off / off))
            L.Add($"  +{kv.Value.On,4}/{on}  {kv.Value.Off,4}/{off}  {kv.Key}");
        L.Add("most anti:");
        foreach (var kv in with.OrderByDescending(k => (double)k.Value.Off / off - (double)k.Value.On / on).Take(10))
            L.Add($"  +{kv.Value.On,4}/{on}  {kv.Value.Off,4}/{off}  {kv.Key}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/mirror3.txt", string.Join("\n", L));
    }
}
