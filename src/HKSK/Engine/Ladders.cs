using HKSK.Behavior;
using HKX2;

namespace HKSK.Engine;

/// <summary>The speed ladders a graph is standing on.</summary>
/// <remarks>
/// <para>
/// A speed ladder is a parametric blend whose parameter is the value
/// <c>BSSpeedSamplerModifier</c> writes -- its rungs are speeds and its output is
/// the animation the creature travels by. That is the blend
/// <c>speeddatasinglefile.txt</c> is a table for, and finding it is what used to
/// be guessed.
/// </para>
/// <para>
/// The variable is not assumed. Projects name it <c>SpeedSampled</c>,
/// <c>SampledSpeed</c> or <c>HorseSpeedSampled</c>, so it is read from the
/// sampler's own <c>speedOut</c> binding.
/// </para>
/// </remarks>
public static class Ladders
{
    private const int FlagParametric = 16;

    /// <summary>
    /// The variable a project's sampler writes its answer to, or null when the
    /// project has no sampler.
    /// </summary>
    public static string? ParameterOf(ProjectWalk walk)
    {
        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not BSSpeedSamplerModifier sampler) continue;

            int at = Bindings.VariableFor(sampler, "speedOut");
            if (at < 0) continue;

            foreach (ProjectStep graph in walk.Steps)
                if (graph.Node is hkbBehaviorGraph g && graph.File == step.File)
                {
                    IList<string> names = g.m_data?.m_stringData?.m_variableNames ?? [];
                    return at < names.Count ? names[at] : null;
                }
        }

        return null;
    }

    /// <summary>
    /// The ladders the evaluation has live, outermost first, with the weight the
    /// graph gives each.
    /// </summary>
    public static IReadOnlyList<(hkbBlenderGenerator Blend, float Weight)> ActiveIn(
        Evaluation run, ProjectWalk walk, string? parameter = null)
    {
        parameter ??= ParameterOf(walk);
        if (parameter is null) return [];

        List<(hkbBlenderGenerator, float)> found = [];

        foreach (ActiveNode node in run.Active)
        {
            if (node.Generator is not hkbBlenderGenerator blend) continue;
            if ((blend.m_flags & FlagParametric) == 0) continue;

            int at = Bindings.VariableFor(blend, "blendParameter");
            if (at < 0) continue;

            if (walk.StepOf(blend) is not { } step) continue;
            if (!run.Variables.TryGetValue(step.File, out Variables? variables)) continue;
            if (at >= variables.Count || variables.NameOf(at) != parameter) continue;

            found.Add((blend, node.Weight));
        }

        return found;
    }
}
