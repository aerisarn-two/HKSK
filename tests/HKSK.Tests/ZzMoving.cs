using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMoving
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var table = new SortedDictionary<string, (int On, int Off, List<string> Ex)>();
        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            var file = cache.FindProjectFile(p.Stem);
            if (actor is null || file is null) continue;
            var walk = ProjectWalk.Of(file);
            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
            var clipSteps = walk.Steps.Where(s => s.Node is hkbClipGenerator).GroupBy(s => ((hkbClipGenerator)s.Node).m_name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            bool OnPath(ProjectStep clip, string variable)
            {
                foreach (var a in walk.Ancestors(clip.Node))
                {
                    var t = vars.GetValueOrDefault(a.File) ?? [];
                    IEnumerable<IHavokObject> nodes = [a.Node];
                    if (a.Node is hkbModifierGenerator mg && mg.m_modifier is { } m)
                        nodes = nodes.Append(m).Concat(m is hkbModifierList ml ? ml.m_modifiers.OfType<IHavokObject>() : []);
                    foreach (var n in nodes.OfType<hkbNode>())
                    {
                        if (n.m_variableBindingSet is { } b && b.m_bindings.Any(x => x.m_variableIndex >= 0 && x.m_variableIndex < t.Length && t[x.m_variableIndex] == variable)) return true;
                        if (n is hkbEvaluateExpressionModifier em && em.m_expressions?.m_expressionsData.Any(e => e.m_expression.Contains(variable)) == true) return true;
                    }
                }
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seen.Add(a.EventName + "|" + string.Join(",", a.Clips) + "|" + a.Mirrored)) continue;
                    var clips = actor.Clips.Where(c => a.Clips.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                    var steps = a.Clips.SelectMany(c => clipSteps.GetValueOrDefault(c) ?? []).ToList();
                    if (clips.Count == 0 || steps.Count == 0) continue;
                    float travel = clips.Max(c => c.Slot?.Motion?.Travel ?? 0f);
                    string moves = travel > 20f ? "travels" : "in place";
                    string driven = steps.Any(s => OnPath(s, "bAnimationDriven")) ? "bAnimationDriven" : "-";
                    string rotation = steps.Any(s => OnPath(s, "bAllowRotation")) ? "bAllowRotation" : "-";
                    foreach (string key in new[] { $"root motion: {moves}", $"path: {driven}", $"path: {rotation}", $"{moves} + {driven}" })
                    {
                        var row = table.GetValueOrDefault(key, (On: 0, Off: 0, Ex: new List<string>()));
                        if (a.Mirrored != 0) row.On++; else row.Off++;
                        if (row.Ex.Count < 5 && a.Mirrored != 0) row.Ex.Add($"{p.Stem}:{a.EventName}({travel:0})");
                        table[key] = row;
                    }
                }
        }
        var L = new List<string> { "feature: flag 1 / flag 0 (distinct attacks per project)" };
        foreach (var (k, v) in table) L.Add($"  {k,-40} {v.On,4} / {v.Off,4}   e.g. {string.Join(" ", v.Ex)}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/moving.txt", string.Join("\n", L));
    }
}
