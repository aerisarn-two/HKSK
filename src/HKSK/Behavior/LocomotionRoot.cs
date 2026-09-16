using HKX2;

namespace HKSK.Behavior;

/// <summary>
/// The node a project's locomotion hangs off: the generator carrying the speed
/// sampler.
/// </summary>
/// <param name="Generator">The <c>hkbModifierGenerator</c> the sampler modifies.</param>
/// <param name="Sampler">The sampler itself.</param>
/// <param name="Subtree">
/// What the generator generates -- everything the sampler's output is in force
/// over.
/// </param>
/// <param name="File">The packfile the generator lives in.</param>
/// <param name="Depth">How far below the project's first container it sits.</param>
public readonly record struct LocomotionRoot(
    hkbModifierGenerator Generator,
    BSSpeedSamplerModifier Sampler,
    hkbGenerator? Subtree,
    string File,
    int Depth)
{
    /// <summary>The generator's name, which is not a reliable way to find it.</summary>
    public string Name => Generator.m_name;

    /// <summary>The packfile's own name, without folder or extension.</summary>
    public string FileName => Path.GetFileNameWithoutExtension(File);

    public override string ToString() =>
        $"{Name} [{FileName}] -> {Subtree?.GetType().Name} '{(Subtree as hkbNode)?.m_name}'";
}

/// <summary>
/// Finds where a project's locomotion is rooted.
/// </summary>
/// <remarks>
/// <para>
/// <c>BSSpeedSamplerModifier</c> is the only node that reads the speed table, and
/// it is a <em>modifier</em>: it does not generate anything itself, it runs
/// alongside a generator and writes the sampled speed into a variable the blends
/// below that generator read. So the node the locomotion hangs off is the
/// generator being modified, and the subtree it is in force over is that
/// generator's own.
/// </para>
/// <para>
/// The shape is the same everywhere in the shipped game: all 44 samplers sit in a
/// <c>hkbModifierList</c> which is the <c>m_modifier</c> of a
/// <c>hkbModifierGenerator</c>. Nothing else holds one. The direct case, a sampler
/// that <em>is</em> the modifier rather than one of a list, is accepted too and
/// does not occur.
/// </para>
/// <para>
/// The name is not the way in. 38 of the 44 generators are called
/// <c>RootModifierGenerator</c> and the rest are <c>Root Mod Gen</c>,
/// <c>RootModGen</c>, <c>NetchRootModifierGenerator</c> and <c>MG_RootState</c> --
/// five spellings for one role, which is what looking for the structure avoids.
/// </para>
/// </remarks>
public static class Locomotion
{
    /// <summary>Every locomotion root in a project, found by visiting it across files.</summary>
    public static IEnumerable<LocomotionRoot> RootsOf(string projectHkx) =>
        RootsIn(ProjectVisitor.Visit(projectHkx));

    /// <summary>The same, over a visit already made.</summary>
    public static IEnumerable<LocomotionRoot> RootsIn(IEnumerable<ProjectStep> walk)
    {
        foreach (ProjectStep step in walk)
        {
            if (step.Node is not hkbModifierGenerator generator) continue;
            if (SamplerOf(generator.m_modifier) is not { } sampler) continue;

            yield return new LocomotionRoot(
                generator, sampler, generator.m_generator, step.File, step.Depth);
        }
    }

    /// <summary>
    /// The variables a project's samplers write, by name.
    /// </summary>
    /// <remarks>
    /// There are three spellings across the game -- <c>SpeedSampled</c>,
    /// <c>SampledSpeed</c> and <c>HorseSpeedSampled</c> -- so what a blend reads has
    /// to be matched against its own project's samplers and never against an
    /// assumed name. The name is also what carries across files: an index is
    /// per file, so a consumer in a referenced graph is only recognisable by the
    /// name its own file's table gives the index.
    /// </remarks>
    public static IReadOnlyCollection<string> OutputsIn(
        IEnumerable<ProjectStep> walk, ProjectVariables variables)
    {
        var outputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (LocomotionRoot root in RootsIn(walk))
            foreach (hkbVariableBindingSetBinding binding in
                     root.Sampler.m_variableBindingSet?.m_bindings ?? [])
                if (binding.m_memberPath == "speedOut" &&
                    variables.NameOf(root.File, binding.m_variableIndex) is { } name)
                    outputs.Add(name);

        return outputs;
    }

    /// <summary>
    /// Everything that reads a sampler's output, which is everything the speed
    /// table actually drives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sampler writes a number and walks away; what that number <em>does</em> is
    /// decided by whoever binds the variable it wrote. Across the shipped game all
    /// 1,037 consumers are the same thing -- a <c>hkbBlenderGenerator</c> binding
    /// its <c>blendParameter</c> -- so the sampled speed only ever chooses a
    /// position within a blend.
    /// </para>
    /// <para>
    /// The visit must be the multi-file one. The dog's sampler is in
    /// <c>quadrupedbehavior.hkx</c> and every blend reading it is in
    /// <c>forwardlocomotion.hkx</c>, so a reader confined to one file finds the
    /// sampler and none of its consumers.
    /// </para>
    /// </remarks>
    public static IEnumerable<SpeedConsumer> ConsumersIn(IEnumerable<ProjectStep> walk)
    {
        List<ProjectStep> steps = [.. walk];
        var variables = new ProjectVariables(steps);
        IReadOnlyCollection<string> outputs = OutputsIn(steps, variables);

        // Eight projects carry no sampler at all and the game still ships a table
        // for each: the flame atronach and the dragon priest drive their locomotion
        // blends straight from Speed, and the rest do not move on a curve. So where
        // nothing writes a sampled speed, the speed itself is what a blend would be
        // reading -- which is the same fallback Ladders.ActiveIn already makes.
        bool fallback = outputs.Count == 0;
        if (fallback) outputs = ["Speed"];

        foreach (ProjectStep step in steps)
        {
            if (step.Node is BSSpeedSamplerModifier) continue;
            if (step.Node is not hkbNode node) continue;

            foreach (hkbVariableBindingSetBinding binding in node.m_variableBindingSet?.m_bindings ?? [])
            {
                string? name = variables.NameOf(step.File, binding.m_variableIndex);
                if (name is null || !outputs.Contains(name)) continue;

                // Speed is read by things a sampled speed never is -- an
                // interpolator on the storm atronach reads and writes it -- so the
                // fallback keeps to what a ladder looks like.
                if (fallback && (node is not hkbBlenderGenerator || binding.m_memberPath != "blendParameter"))
                    continue;

                yield return new SpeedConsumer(
                    node, binding.m_memberPath, name, step.File, step.Depth);
            }
        }
    }

    /// <summary>The speed sampler a modifier is, or holds, if any.</summary>
    private static BSSpeedSamplerModifier? SamplerOf(hkbModifier? modifier)
    {
        if (modifier is BSSpeedSamplerModifier direct) return direct;
        if (modifier is not hkbModifierList list) return null;

        foreach (hkbModifier? held in list.m_modifiers)
            if (held is BSSpeedSamplerModifier found) return found;

        return null;
    }
}
/// <summary>
/// Something reading the speed the sampler produced.
/// </summary>
/// <param name="Node">The node with the binding.</param>
/// <param name="MemberPath">The member it binds, e.g. <c>blendParameter</c>.</param>
/// <param name="Variable">The variable's name, which is a sampler's output.</param>
/// <param name="File">The packfile the node lives in.</param>
/// <param name="Depth">How far below the project's first container it sits.</param>
public readonly record struct SpeedConsumer(
    IHavokObject Node,
    string MemberPath,
    string Variable,
    string File,
    int Depth)
{
    /// <summary>The node's class, e.g. <c>hkbBlenderGenerator</c>.</summary>
    public string Kind => Node.GetType().Name;

    /// <summary>The node's own name, for the types that carry one.</summary>
    public string? Name => (Node as hkbNode)?.m_name;

    /// <summary>The packfile's own name, without folder or extension.</summary>
    public string FileName => Path.GetFileNameWithoutExtension(File);

    public override string ToString() =>
        $"{Kind} '{Name}'.{MemberPath} <- {Variable} [{FileName}]";
}
