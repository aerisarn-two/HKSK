using System.Text;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>What the shipped behaviours are actually built from.</summary>
/// <remarks>
/// An engine that evaluates these graphs has to cover the node classes the game
/// uses, and no more. The census is taken from the projects themselves rather
/// than from any list, so it says what must be implemented.
/// </remarks>
public sealed class NodeCensusTests
{
    /// <summary>What a class is, by what it derives from rather than by its name.</summary>
    private static string Kind(System.Type type)
    {
        for (System.Type? at = type; at is not null; at = at.BaseType)
            switch (at.Name)
            {
                case "hkbGenerator": return "generator";
                case "hkbModifier": return "modifier";
                case "hkbTransitionEffect": return "transition";
                case "hkbCondition": return "condition";
                case "hkbBindable": return "bindable";
            }

        return "data";
    }

    [CorpusFact]
    public void TakeTheCensus()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Dictionary<string, int> instances = [];
        Dictionary<string, HashSet<string>> projects = [];
        Dictionary<string, string> kinds = [];
        Dictionary<string, string> bases = [];

        foreach (ProjectLocation at in cache.LocateSpeedProjects().Where(p => p.Found))
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);

            foreach (ProjectStep step in walk.Steps)
            {
                string name = step.Node.GetType().Name;
                if (!name.StartsWith("hkb", StringComparison.Ordinal) &&
                    !name.StartsWith("BS", StringComparison.Ordinal)) continue;

                instances[name] = instances.GetValueOrDefault(name) + 1;
                kinds[name] = Kind(step.Node.GetType());
                bases[name] = step.Node.GetType().BaseType?.Name ?? "-";
                if (!projects.TryGetValue(name, out HashSet<string>? seen))
                    projects[name] = seen = [];
                seen.Add(at.Name);
            }
        }

        StringBuilder text = new();
        text.AppendLine($"{instances.Count} classes");
        foreach ((string name, int count) in instances.OrderByDescending(p => p.Value))
            text.AppendLine($"{count,8}  {projects[name].Count,3}  {name}  [{kinds[name]}] : {bases[name]}");

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "node-census.txt"), text.ToString());

        Assert.Equal(86, instances.Count);
        Assert.Equal(14, kinds.Values.Count(k => k == "generator"));
        Assert.Equal(32, kinds.Values.Count(k => k == "modifier"));
        Assert.Equal(1, kinds.Values.Count(k => k == "transition"));
        Assert.Equal(2, kinds.Values.Count(k => k == "condition"));

        // The five commonest classes are more than half of every executable node.
        Assert.Equal(12258, instances["hkbClipGenerator"]);
        Assert.Equal(4378, instances["hkbStateMachine"]);
        Assert.Equal(2550, instances["hkbBlenderGenerator"]);
        Assert.Equal(2311, instances["hkbModifierGenerator"]);
        Assert.Equal(791, instances["hkbManualSelectorGenerator"]);
    }
}
