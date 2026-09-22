using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Where can each werewolf attack be started from? The source states of its transitions,
// and the condition on each transition.
public sealed class ZzFromState
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        string stem = "WerewolfBeastProject";
        var p = shipped.Projects.First(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
        var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);

        var walk = ProjectWalk.Of(cache.FindProjectFile(stem)!);
        foreach (var s in walk.Steps)
        {
            if (s.Node is not hkbStateMachine sm || s.Node is null) continue;
            var g = walk.Steps.First(x => x.File == s.File && x.Node is hkbBehaviorGraph).Node as hkbBehaviorGraph;
            var ev = g?.m_data?.m_stringData?.m_eventNames ?? [];
            var vars = g?.m_data?.m_stringData?.m_variableNames ?? [];

            void Show(string from, hkbStateMachineTransitionInfo t)
            {
                if (t.m_eventId < 0 || t.m_eventId >= ev.Count) return;
                string name = ev[t.m_eventId];
                if (!flags.TryGetValue(name, out int f)) return;
                string to = sm.m_states.FirstOrDefault(x => x.m_stateId == t.m_toStateId)?.m_name ?? "?";
                string cond = t.m_condition switch
                {
                    hkbExpressionCondition e => e.m_expression,
                    hkbStringCondition c => c.m_conditionString,
                    null => "",
                    var o => o.GetType().Name,
                };
                string effect = t.m_transition switch
                {
                    hkbBlendingTransitionEffect b => $"{b.m_name} blend {b.m_duration:0.##}s flags 0x{b.m_flags:x} mode {b.m_endMode} sync {b.m_selfTransitionMode}",
                    null => "(none)",
                    var o => o.GetType().Name,
                };
                L.Add($"[{f}] {name,-30} flags 0x{t.m_flags:x4} -> {to,-38} {effect}");
            }

            foreach (var t in sm.m_wildcardTransitions?.m_transitions ?? []) Show("(any state)", t);
            foreach (var st in sm.m_states)
                foreach (var t in st.m_transitions?.m_transitions ?? []) Show(st.m_name, t);
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/fromstate.txt", string.Join("\n", L.Distinct().OrderBy(x => x.Substring(4))));
    }
}
