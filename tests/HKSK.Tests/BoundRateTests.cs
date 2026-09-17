using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Where a speed reaches a clip or a blend through an expression rather than a
/// direct binding.
/// </summary>
/// <remarks>
/// The rebuild reads a speed ladder off <c>blendParameter</c> bound to the sampled
/// speed, and a clip's playback speed as stored. The spider centurion broke both
/// assumptions at once -- its four arms play at
/// <c>(max(5, SpeedSampled)) / speedForward</c> and its blend binds nothing -- and
/// its block went from unbuildable to 61 of 61 once the expression was evaluated.
/// These census the corpus for the same shape, so a creature that uses it is found
/// by a failing count rather than by a block that quietly scores zero.
/// </remarks>
public sealed class BoundRateTests
{
    private static IEnumerable<(string Project, ProjectWalk Walk,
        Dictionary<string, List<string>> Names, Dictionary<(string, string), string> Writers)> Projects()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            if (at.ProjectFile is null) continue;
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile);

            var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var writers = new Dictionary<(string, string), string>();

            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is hkbBehaviorGraphData data && data.m_stringData?.m_variableNames is { } variables)
                    names[step.File] = [.. variables];

                if (step.Node is hkbEvaluateExpressionModifier modifier)
                    foreach (var expression in modifier.m_expressions?.m_expressionsData ?? [])
                        if (Expression.Parse(expression.m_expression) is { Effect.Target: { } target })
                            writers[(step.File.ToLowerInvariant(), target.ToLowerInvariant())] = expression.m_expression;
            }

            yield return (at.Name, walk, names, writers);
        }
    }

    private static string? WriterOf(ProjectStep step, int variable,
        Dictionary<string, List<string>> names, Dictionary<(string, string), string> writers)
    {
        List<string> table = names.GetValueOrDefault(step.File) ?? [];
        if (variable < 0 || variable >= table.Count) return null;
        return writers.GetValueOrDefault((step.File.ToLowerInvariant(), table[variable].ToLowerInvariant()));
    }

    /// <summary>
    /// Three expressions make a clip's playback follow the speed, and only the
    /// spider's reaches a table.
    /// </summary>
    /// <remarks>
    /// The twelve quadrupeds' backward walk is a state their tables are not sampled
    /// in -- all twelve already rebuild at 100% or within a point of it -- and the
    /// witchlight's two clips travel nothing, so any rate leaves its zero table zero.
    /// </remarks>
    [CorpusFact]
    public void OnlyTheSpiderPlaysLocomotionAtARateTheSpeedComputes()
    {
        var found = new SortedDictionary<string, SortedSet<string>>();

        foreach (var (project, walk, names, writers) in Projects())
            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is not hkbClipGenerator clip) continue;
                var binding = clip.m_variableBindingSet?.m_bindings?.FirstOrDefault(b => b.m_memberPath == "playbackSpeed");
                if (binding is null) continue;

                if (WriterOf(step, binding.m_variableIndex, names, writers) is not { } writer) continue;
                // What it reads, not what it writes: turnSpeedMult is named for speed
                // and computed from TurnDelta.
                if (!writer[(writer.IndexOf('=') + 1)..].Contains("Speed", StringComparison.Ordinal)) continue;

                string shape = writer[..writer.IndexOf('=')].Trim().StartsWith("speedMult") ? "speedMult* = (max(5, SpeedSampled))/speed*" : writer;
                if (!found.TryGetValue(shape, out var projects)) found[shape] = projects = [];
                projects.Add(project);
            }

        Assert.Equal(["DwarvenSpiderCenturionProject"], found["speedMult* = (max(5, SpeedSampled))/speed*"]);
        Assert.Equal(12, found["walkBackSpeedMult = clamp( Speed/walkBackRate, 0.01, Speed )"].Count);
        Assert.Equal(["WitchlightProject"], found["runSpeedMult = Speed/runRate"]);
        Assert.Equal(3, found.Count);
    }

    /// <summary>
    /// No blend reads a speed through an expression either.
    /// </summary>
    /// <remarks>
    /// Six blends read a variable an expression writes from something named for
    /// speed, and none is a speed axis: four are the frost atronach's turns, damped
    /// at a rate called <c>SpeedAcc</c>, and two are the vampire lord's bat sprint,
    /// whose direction is gated on <c>Speed &gt; 5</c>.
    /// </remarks>
    [CorpusFact]
    public void NoBlendReadsASpeedThroughAnExpression()
    {
        var found = new List<string>();

        foreach (var (project, walk, names, writers) in Projects())
            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is not hkbBindable bindable || bindable.m_variableBindingSet?.m_bindings is not { } bindings) continue;

                foreach (var binding in bindings)
                {
                    if (binding.m_memberPath is not ("blendParameter" or "weight" or "worldFromModelWeight" or "fBlendParameter")) continue;
                    if (WriterOf(step, binding.m_variableIndex, names, writers) is not { } writer) continue;
                    if (writer[(writer.IndexOf('=') + 1)..].Contains("Speed", StringComparison.OrdinalIgnoreCase))
                        found.Add($"{project}:{(step.Node as hkbGenerator)?.m_name}");
                }
            }

        Assert.Equal(
        [
            "AtronachFrostProject:TurnLBlend", "AtronachFrostProject:TurnLBlend00",
            "AtronachFrostProject:TurnRBlend", "AtronachFrostProject:TurnRBlend00",
            "VampireLord:BatSprintBlend", "VampireLord:BatSprint_GroundBlend",
        ], found.Distinct().Order(StringComparer.Ordinal));
    }
}
