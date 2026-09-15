using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Every reference to the <c>iState</c> variable in the shipped game.
/// </summary>
/// <remarks>
/// <para>
/// <c>iState</c> is the speed table's key, so the obvious expectation is that the
/// graph binds it in several places and switches on it -- that it is how one
/// generator is chosen over another. <strong>It is not.</strong> Across all 49
/// projects it is bound on exactly two member paths, and 30 of the 41 projects
/// that read the table never write it anywhere in the graph at all.
/// </para>
/// <para>
/// These tests find the paths rather than assume them, which is why they are
/// worth having even though the answer is mostly negative.
/// </para>
/// </remarks>
public sealed class IStateBindingTests
{
    /// <summary>
    /// <c>iState</c> is bound on two member paths in the whole game: the sampler
    /// reads it, the state manager writes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 43 <c>BSSpeedSamplerModifier.state</c> and 3
    /// <c>BSIStateManagerModifier.iStateVar</c>, and nothing else anywhere. So
    /// <c>iState</c> is not bound into conditions, selectors or blend parameters:
    /// no generator is chosen by a binding on it.
    /// </para>
    /// <para>
    /// Names are resolved per file, because an index means nothing on its own --
    /// <c>iState</c> appears in the variable tables of 90 of the 125 behaviour
    /// files, at a different index in each.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void IStateIsBoundOnTwoMemberPathsInTheWholeGame()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var paths = new Dictionary<string, int>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            var variables = new ProjectVariables(walk);

            foreach (ProjectStep step in walk)
                foreach (var binding in Bindings(step.Node))
                {
                    if (variables.NameOf(step.File, binding.m_variableIndex) != "iState") continue;

                    string path = $"{step.Node.GetType().Name}.{binding.m_memberPath}";
                    paths.TryGetValue(path, out int count);
                    paths[path] = count + 1;
                }
        }

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["BSSpeedSamplerModifier.state"] = 43,
                ["BSIStateManagerModifier.iStateVar"] = 3,
            },
            paths);
    }

    /// <summary>
    /// Only three projects have anything besides the sampler referring to
    /// <c>iState</c>.
    /// </summary>
    /// <remarks>
    /// The spriggan, the vampire lord and the werewolf. For the other 38 projects
    /// with a sampler, the sampler's own <c>state</c> binding is the only mention
    /// of <c>iState</c> in the entire behaviour, across every file it reaches.
    /// </remarks>
    [CorpusFact]
    public void OnlyThreeProjectsReferToIStateAnywhereElse()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var others = new List<string>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            if (!Locomotion.RootsIn(walk).Any()) continue;

            var variables = new ProjectVariables(walk);

            bool elsewhere = walk.Any(step =>
                step.Node is not BSSpeedSamplerModifier &&
                Bindings(step.Node).Any(b =>
                    variables.NameOf(step.File, b.m_variableIndex) == "iState"));

            if (elsewhere) others.Add(at.Name);
        }

        Assert.Equal(["Spriggan", "VampireLord", "WerewolfBeastProject"], others.Order());
    }

    /// <summary>
    /// The node that actually discriminates does not bind <c>iState</c> at all.
    /// </summary>
    /// <remarks>
    /// <c>BSiStateTaggingGenerator</c> is what puts a subtree under a key, and
    /// there are 249 of them across 8 projects. Not one binds <c>iState</c>:
    /// <c>m_iStateToSetAs</c> is a plain serialised integer and the node writes the
    /// variable by its nature. So looking for bindings is exactly the wrong way to
    /// find the discrimination -- the structure carries it, not the wiring.
    /// </remarks>
    [CorpusFact]
    public void TheTaggingGeneratorCarriesItsKeyAsAFieldNotABinding()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int taggers = 0, bound = 0;
        var projects = new List<string>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            var variables = new ProjectVariables(walk);
            int here = 0;

            foreach (ProjectStep step in walk)
            {
                if (step.Node is not BSiStateTaggingGenerator tag) continue;

                taggers++;
                here++;

                if (Bindings(tag).Any(b => variables.NameOf(step.File, b.m_variableIndex) == "iState"))
                    bound++;
            }

            if (here > 0) projects.Add(at.Name);
        }

        Assert.Equal(249, taggers);
        Assert.Equal(0, bound);
        Assert.Equal(8, projects.Count);
    }

    /// <summary>
    /// The state manager reads the movement-type constants and writes the result
    /// into <c>iState</c>.
    /// </summary>
    /// <remarks>
    /// Its <c>iStateVar</c> binds to <c>iState</c> -- the one place anything in the
    /// graph writes it -- while each <c>stateData:N/iStateToSetAs</c> binds to an
    /// <c>iState_&lt;MOVT&gt;</c> constant, one per state it manages. So the pairing
    /// of a state machine's state to a table key is carried by a binding here, and
    /// by a plain field in the tagging generator.
    /// </remarks>
    [CorpusFact]
    public void TheStateManagerBindsTheMovementTypeConstants()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int managers = 0, constants = 0;

        foreach (string name in new[] { "Spriggan", "VampireLord", "WerewolfBeastProject" })
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(cache.FindProjectFile(name)!)];
            var variables = new ProjectVariables(walk);

            foreach (ProjectStep step in walk)
            {
                if (step.Node is not BSIStateManagerModifier manager) continue;

                managers++;
                var bound = Bindings(manager)
                    .ToDictionary(b => b.m_memberPath, b => variables.NameOf(step.File, b.m_variableIndex));

                Assert.Equal("iState", bound["iStateVar"]);

                foreach ((string path, string? variable) in bound)
                {
                    if (path == "iStateVar") continue;

                    Assert.EndsWith("/iStateToSetAs", path);
                    Assert.StartsWith("iState_", variable);
                    constants++;
                }
            }
        }

        Assert.Equal(3, managers);
        Assert.True(constants >= 3, $"only {constants} movement-type constants bound");
    }

    /// <summary>
    /// For 30 of the 41 projects that read the table, nothing in the graph writes
    /// <c>iState</c> at all.
    /// </summary>
    /// <remarks>
    /// Neither a tagging generator nor a state manager anywhere in any file they
    /// reach -- the giant, the falmer's cousins, every quadruped. The game writes
    /// <c>iState</c> from the actor's movement type and the graph only reads it, so
    /// for these the key cannot be recovered from the behaviour at all and has to
    /// come from the movement types, which is exactly what makes them an input.
    /// </remarks>
    [CorpusFact]
    public void ThirtyProjectsNeverWriteIStateInTheirGraph()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int reading = 0, silent = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            if (!Locomotion.RootsIn(walk).Any()) continue;

            reading++;

            bool writes = walk.Any(s => s.Node is BSiStateTaggingGenerator or BSIStateManagerModifier);
            if (!writes) silent++;
        }

        Assert.Equal(41, reading);
        Assert.Equal(30, silent);
    }

    /// <summary>Bindings of a node or a modifier, which do not share a base type.</summary>
    private static IEnumerable<hkbVariableBindingSetBinding> Bindings(IHavokObject node) =>
        (node as hkbNode)?.m_variableBindingSet?.m_bindings ?? [];
}
