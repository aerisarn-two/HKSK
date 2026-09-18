using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzBindings
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();
        foreach (string proj in (Environment.GetEnvironmentVariable("PROJS") ?? "DefaultMale,DraugrProject").Split(','))
        {
            var walk = ProjectWalk.Of(cache.FindProjectFile(proj)!);
            var vars = new Dictionary<string, string[]>();
            var events = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) { vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []]; events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; }
            var counts = new SortedDictionary<string, int>();
            foreach (var s in walk.Steps)
            {
                if (s.Node is hkbNode n && n.m_variableBindingSet is { } vb)
                    foreach (var b in vb.m_bindings)
                    {
                        string v = b.m_variableIndex >= 0 && b.m_variableIndex < vars[s.File].Length ? vars[s.File][b.m_variableIndex] : "?";
                        if (v.Contains("HandType") || v.Contains("Equipped") || v.Contains("WeapType") || v.Contains("WeaponType"))
                        { string k = $"{proj} bind {n.GetType().Name}.{b.m_memberPath} <- {v}"; counts[k] = counts.GetValueOrDefault(k) + 1; }
                    }
                if (s.Node is hkbStateMachine sm)
                {
                    var infos = (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(st => st.m_transitions?.m_transitions ?? []));
                    foreach (var t in infos)
                        if (t.m_condition is hkbExpressionCondition ec && (ec.m_expression.Contains("HandType") || ec.m_expression.Contains("Equipped")))
                        { string k = $"{proj} condition {ec.m_expression}"; counts[k] = counts.GetValueOrDefault(k) + 1; }
                }
                if (s.Node is hkbExpressionDataArray ea)
                    foreach (var e in ea.m_expressionsData)
                        if (e.m_expression.Contains("HandType"))
                        { string k = $"{proj} expression {e.m_expression}"; counts[k] = counts.GetValueOrDefault(k) + 1; }
            }
            foreach (var kv in counts) L.Add($"{kv.Value,5}  {kv.Key}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/bindings.txt", string.Join("\n", L));
    }
}
