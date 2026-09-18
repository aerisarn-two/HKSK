using HKSK.Behavior;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzDump
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        string proj = cache.FindProjectFile(Environment.GetEnvironmentVariable("PROJ") ?? "ChaurusProject")!;
        string clip = Environment.GetEnvironmentVariable("CLIP") ?? "Attack_RBite";
        var walk = ProjectWalk.Of(proj);
        var L = new List<string>();
        var events = new Dictionary<string, string[]>();
        var vars = new Dictionary<string, string[]>();
        foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) { events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []]; }
        var target = walk.Steps.First(s => s.Node is hkbClipGenerator c && c.m_name == clip);
        foreach (var a in walk.Ancestors(target.Node).Reverse().Append(target))
        {
            string extra = "";
            if (a.Node is hkbStateMachine sm)
            {
                var ev = events[a.File];
                string T(hkbStateMachineTransitionInfoArray? arr) => arr is null ? "" : string.Join(" ", arr.m_transitions.Select(t => $"{(t.m_eventId >= 0 && t.m_eventId < ev.Length ? ev[t.m_eventId] : t.m_eventId.ToString())}->{t.m_toStateId}/n{t.m_toNestedStateId}/f{t.m_flags:x}"));
                extra = $" start={sm.m_startStateId} wild[{T(sm.m_wildcardTransitions)}] states: " + string.Join(" | ", sm.m_states.Select(st => $"{st.m_stateId}:{st.m_name} [{T(st.m_transitions)}]"));
            }
            if (a.Node is hkbManualSelectorGenerator ms) extra = $" selected={ms.m_selectedGeneratorIndex} children: " + string.Join(",", ms.m_generators.Select(x => (x as hkbNode)?.m_name));
            if (a.Node is hkbNode nd && nd.m_variableBindingSet is { } vb) extra += " bindings: " + string.Join(",", vb.m_bindings.Select(b => $"{b.m_memberPath}<-{(b.m_variableIndex >= 0 && b.m_variableIndex < vars[a.File].Length ? vars[a.File][b.m_variableIndex] : b.m_variableIndex.ToString())}"));
            if (a.Node is hkbModifierGenerator mg) extra += " modifier: " + mg.m_modifier?.GetType().Name + " " + (mg.m_modifier as hkbNode)?.m_name;
            if (a.Node is hkbClipGenerator cg) extra += $" flags={cg.m_flags} anim={cg.m_animationName}";
            L.Add($"{a.Node.GetType().Name} '{(a.Node as hkbNode)?.m_name}' via {a.Member}[{a.Index}] ({a.FileName}){extra}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/dump.txt", string.Join("\n", L));
    }
}
