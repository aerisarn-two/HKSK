using System.Text;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>Running the shipped graphs far enough to say what they are evaluating.</summary>
public sealed class ActiveGeneratorTests
{
    [CorpusFact]
    public void EveryProjectResolvesToASetOfActiveGenerators()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        StringBuilder report = new();
        Dictionary<string, int> leaves = [];
        int projects = 0, reachedClips = 0, unresolved = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects().Where(p => p.Found))
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);

            hkbBehaviorGraph? graph = null;
            foreach (ProjectStep step in walk.Steps)
                if (step.Node is hkbBehaviorGraph found) { graph = found; break; }

            if (graph is null) { report.AppendLine($"{at.Name,-28} no graph"); continue; }

            Variables variables = Variables.Of(graph);
            IReadOnlyList<ActiveNode> active = ActiveGenerators.Under(graph, walk);
            int clips = active.Count(a => a.Generator is hkbClipGenerator or BSSynchronizedClipGenerator);

            projects++;
            if (clips > 0) reachedClips++;

            // A leaf here is a generator the walk selected nothing below: either a
            // clip, which is right, or a class the engine does not select through yet.
            HashSet<int> parents = [];
            for (int i = 1; i < active.Count; i++)
                for (int j = i - 1; j >= 0; j--)
                    if (active[j].Depth < active[i].Depth) { parents.Add(j); break; }

            for (int i = 0; i < active.Count; i++)
                if (!parents.Contains(i))
                {
                    string kind = active[i].Generator.GetType().Name;
                    leaves[kind] = leaves.GetValueOrDefault(kind) + 1;

                    // A machine whose chosen state was already reached elsewhere looks
                    // like a leaf without being one; only report the ones that chose nothing.
                    if (active[i].Generator is hkbStateMachine stuck &&
                        ActiveGenerators.StateOf(stuck, variables) is null)
                    {
                        unresolved++;
                        int bound = Bindings.VariableFor(stuck, "startStateId");
                        report.AppendLine(
                            $"    stuck: {at.Name} '{stuck.m_name}' startStateId={stuck.m_startStateId} " +
                            $"bound={(bound < 0 ? "no" : variables.NameOf(bound) + "=" + variables.AsInt(bound))} " +
                            $"sync={stuck.m_syncVariableIndex} mode={stuck.m_startStateMode} " +
                            $"states=[{string.Join(",", (stuck.m_states ?? []).Select(x => x.m_stateId))}]");
                    }
                }

            report.AppendLine(
                $"{at.Name,-28} vars {variables.Count,4}  active {active.Count,4}  clips {clips,3}");
        }

        report.AppendLine();
        report.AppendLine("leaves the walk ended on:");
        foreach ((string kind, int count) in leaves.OrderByDescending(p => p.Value))
            report.AppendLine($"{count,6}  {kind}");

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "active-report.txt"), report.ToString());
        Assert.Equal(49, projects);

        // Every state machine the walk reaches picks a state. Getting here needed two
        // things the file does not say: variable indices are per file, and the sync
        // variable is only consulted in START_STATE_MODE_SYNC.
        Assert.Equal(0, unresolved);

        // Every project resolves to at least one clip, so the walk selects all the
        // way down rather than stopping on a class it does not know.
        Assert.Equal(49, reachedClips);
    }

    /// <summary>What one creature's graph is evaluating, spelled out.</summary>
    [CorpusFact]
    public void DeerChain()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ProjectLocation at = cache.LocateSpeedProjects().First(p => p.Name == "DeerProject");
        ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);

        hkbBehaviorGraph graph = walk.Steps.Select(s => s.Node).OfType<hkbBehaviorGraph>().First();
        IReadOnlyList<ActiveNode> active = ActiveGenerators.Under(graph, walk);

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "deer-chain.txt"),
            string.Join("\n", active.Select(a => a.ToString())));
        Assert.NotEmpty(active);
    }
}
