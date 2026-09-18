using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// What sets bAnimationDriven in each project, and to what.
public sealed class ZzAnimDriven
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();

        foreach (string stem in new[] { "AtronachStormProject", "NetchProject", "WitchlightProject", "WerewolfBeastProject", "DefaultMale", "BearProject", "WispProject" })
        {
            string? path = cache.FindProjectFile(stem);
            if (path is null) continue;
            L.Add($"======== {stem}");

            var walk = ProjectWalk.Of(path);
            var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in walk.Steps)
            {
                if (s.Node is hkbBehaviorGraph g && g.m_data is { } data)
                {
                    var names = data.m_stringData?.m_variableNames ?? [];
                    int at = -1;
                    for (int i = 0; i < names.Count; i++)
                        if (string.Equals(names[i], "bAnimationDriven", StringComparison.OrdinalIgnoreCase)) at = i;
                    if (at < 0) continue;

                    var vals = data.m_variableInitialValues?.m_wordVariableValues;
                    string init = vals is not null && at < vals.Count ? vals[at].m_value.ToString() : "?";
                    indexOf[s.File] = at;
                    L.Add($"   {Path.GetFileName(s.File),-34} bAnimationDriven is variable {at}, initial {init}");
                }
            }

            // every expression that assigns it, and every node that binds it
            foreach (var s in walk.Steps)
            {
                if (!indexOf.TryGetValue(s.File, out int at)) continue;
                string file = s.File;

                if (s.Node is hkbEvaluateExpressionModifier ev)
                    foreach (var e in ev.m_expressions?.m_expressionsData ?? [])
                        if (e.m_assignmentVariableIndex == at)
                            L.Add($"   expression   {ev.m_name,-40} \"{e.m_expression}\"   in {Path.GetFileName(file)}");

                if (s.Node is hkbNode node and not hkbEvaluateExpressionModifier)
                    foreach (var b in node.m_variableBindingSet?.m_bindings ?? [])
                        if (b.m_variableIndex == at)
                            L.Add($"   binding      {node.GetType().Name}({node.m_name}) {b.m_memberPath}   in {Path.GetFileName(file)}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/animdriven.txt", string.Join("\n", L));
    }
}
