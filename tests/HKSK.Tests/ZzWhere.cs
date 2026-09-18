using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzWhere
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        string projectName = Environment.GetEnvironmentVariable("PROJ") ?? "DefaultMale";
        var project = cache.OpenActor(projectName)!;
        var walk = ProjectWalk.Of(project.ProjectFile!.File.Path);
        var events = new Dictionary<string, string[]>(); var vars = new Dictionary<string, string[]>();
        foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) { events[s.File] = [.. g.m_data?.m_stringData?.m_eventNames ?? []]; vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []]; }
        var L = new List<string>();
        foreach (string stem in (Environment.GetEnvironmentVariable("STEMS") ?? "CombatIdleLookingB,MT_idle_A_left_long").Split(','))
        {
            L.Add($"=== {stem}");
            var slots = project.Animations.Where(a => a.FileStem.Equals(stem, StringComparison.OrdinalIgnoreCase)).ToList();
            L.Add("slots: " + string.Join(", ", slots.Select(s => $"[{s.Index}] {s.StoredName}")));
            foreach (var slot in slots)
                L.Add("cache clips on that slot: " + string.Join(", ", project.ClipsOf(slot).Select(c => c.Name)));
            var clipNames = slots.SelectMany(project.ClipsOf).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var step in walk.Steps.Where(s => s.Node is hkbClipGenerator c && (clipNames.Contains(c.m_name) || c.m_animationName.Contains(stem, StringComparison.OrdinalIgnoreCase))))
            {
                var c = (hkbClipGenerator)step.Node;
                L.Add($"  clip '{c.m_name}' anim {c.m_animationName} in {step.FileName}");
                foreach (var a in walk.Ancestors(step.Node).Take(8))
                {
                    string extra = "";
                    if (a.Node is hkbStateMachine sm)
                    {
                        var ev = events[a.File];
                        string E(int id) => id >= 0 && id < ev.Length ? ev[id] : id.ToString();
                        var into = sm.m_states.Where(st => ReferenceEquals(st, step.Parent) || walk.Ancestors(step.Node).Any(x => ReferenceEquals(x.Node, st))).Select(st => st.m_stateId).ToList();
                        var toIt = (sm.m_wildcardTransitions?.m_transitions ?? []).Concat(sm.m_states.SelectMany(st => st.m_transitions?.m_transitions ?? []))
                            .Where(t => into.Contains(t.m_toStateId)).Select(t => E(t.m_eventId) + (t.m_condition is hkbExpressionCondition x ? $"[{x.m_expression}]" : "")).Distinct();
                        extra = $" start={sm.m_startStateId} into state {string.Join(",", into)} via [{string.Join(" ", toIt)}]";
                    }
                    if (a.Node is hkbManualSelectorGenerator ms) extra = $" selected={ms.m_selectedGeneratorIndex}";
                    if (a.Node is hkbNode n && n.m_variableBindingSet is { } vb) extra += " bind " + string.Join(",", vb.m_bindings.Select(b => $"{b.m_memberPath}<-{(b.m_variableIndex >= 0 && b.m_variableIndex < vars[a.File].Length ? vars[a.File][b.m_variableIndex] : "?")}"));
                    if (a.Node is hkbModifierGenerator mg) extra += $" modifier {mg.m_modifier?.GetType().Name}";
                    L.Add($"     <- {a.Node.GetType().Name} '{(a.Node as hkbNode)?.m_name}'{extra}");
                }
            }
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/where.txt", string.Join("\n", L));
    }
}
