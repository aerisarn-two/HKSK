using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// How the <c>iState_&lt;movement type&gt;</c> constants are used by the behaviours
/// that declare them.
/// </summary>
/// <remarks>
/// Barely at all, is the answer. They are a dictionary written for the engine and
/// for whatever produced the speed table, not wiring the graph reads.
/// </remarks>
public sealed class StateConstantUseTests
{
    private static List<(string Name, ProjectWalk Walk, BehaviorRoot Root, SkyrimCache Cache)> Load()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var projects = new List<(string, ProjectWalk, BehaviorRoot, SkyrimCache)>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            projects.Add((at.Name, walk, BehaviorRoot.Of(at.ProjectFile!)!.Value, cache));
        }

        return projects;
    }

    /// <summary>
    /// 139 of the 145 constants are never referenced by any behaviour anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight references in total, over three projects, and every one of them is a
    /// <c>BSIStateManagerModifier.stateData:N/iStateToSetAs</c> binding. Nothing
    /// else in any graph -- no condition, no selector, no blend -- names one.
    /// </para>
    /// <para>
    /// So these variables are declarative. They say which number means which
    /// movement type, for the benefit of the engine writing <c>iState</c> and of
    /// whatever wrote the speed table, and the graph itself mostly ignores them.
    /// A tool adding a movement type has to add the constant even though no node
    /// will read it.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void AlmostNoConstantIsReferencedByAnyBehaviour()
    {
        int constants = 0, referenced = 0, references = 0;
        var projects = new HashSet<string>();
        var paths = new Dictionary<string, int>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, _) in Load())
        {
            var variables = new ProjectVariables(walk.Steps);
            var declared = StateConstants.Of(walk, root);
            constants += declared.Count;

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ProjectStep step in walk.Steps)
                foreach (var binding in (step.Node as hkbNode)?.m_variableBindingSet?.m_bindings ?? [])
                {
                    string? variable = variables.NameOf(step.File, binding.m_variableIndex);
                    if (variable is null || !variable.StartsWith("iState_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    references++;
                    used.Add(variable);
                    projects.Add(name);

                    string path = $"{step.Node.GetType().Name}.{binding.m_memberPath}";
                    paths.TryGetValue(path, out int count);
                    paths[path] = count + 1;
                }

            referenced += declared.Keys.Count(k => used.Contains(k));
        }

        Assert.Equal(145, constants);
        Assert.Equal(6, referenced);
        Assert.Equal(8, references);
        Assert.Equal(["Spriggan", "VampireLord", "WerewolfBeastProject"], projects.Order());

        // and only ever through the one node type
        Assert.All(paths.Keys, p => Assert.StartsWith("BSIStateManagerModifier.stateData:", p));
        Assert.All(paths.Keys, p => Assert.EndsWith("/iStateToSetAs", p));
    }

    /// <summary>
    /// The state manager's stored keys are placeholders; the bindings carry the
    /// real ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of the eight <c>stateData</c> entries stores
    /// <c>m_iStateToSetAs = 0</c> and binds it to an <c>iState_&lt;MOVT&gt;</c>
    /// constant instead. Reading the stored field alone says every state sets
    /// <c>iState</c> to zero, which is wrong for half of them.
    /// </para>
    /// <para>
    /// The same trap as <c>BSSpeedSamplerModifier.m_state</c>, which stores -1 in
    /// all 44. A stored scalar on a node that also binds it is not a value, it is
    /// a slot.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheStoredKeysArePlaceholdersAndTheBindingsAreReal()
    {
        int entries = 0, storedZero = 0, bound = 0;

        foreach ((string name, ProjectWalk walk, _, _) in Load())
        {
            var variables = new ProjectVariables(walk.Steps);

            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is not BSIStateManagerModifier manager) continue;

                var bindings = (manager.m_variableBindingSet?.m_bindings ?? [])
                    .ToDictionary(b => b.m_memberPath, b => variables.NameOf(step.File, b.m_variableIndex));

                for (int i = 0; i < manager.m_stateData.Count; i++)
                {
                    entries++;
                    if (manager.m_stateData[i].m_iStateToSetAs == 0) storedZero++;

                    if (bindings.TryGetValue($"stateData:{i}/iStateToSetAs", out string? variable) &&
                        variable is not null && variable.StartsWith("iState_", StringComparison.OrdinalIgnoreCase))
                        bound++;
                }
            }
        }

        Assert.Equal(8, entries);
        Assert.Equal(8, storedZero);   // every stored key is a placeholder
        Assert.Equal(8, bound);        // and every one is really supplied by a binding
    }

    /// <summary>The spriggan, read end to end.</summary>
    /// <remarks>
    /// Its manager pairs two states of <c>BaseBehavior</c> with two constants, and
    /// the two values it can write, 0 and 1, are exactly the two keys its speed
    /// table ships. That is the whole mechanism: enter a state, the manager writes
    /// the key, the sampler reads it.
    /// </remarks>
    [CorpusFact]
    public void TheSprigganPairsItsStatesToItsTwoKeys()
    {
        (string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) =
            Load().First(p => p.Name == "Spriggan");

        var variables = new ProjectVariables(walk.Steps);
        var constants = StateConstants.Of(walk, root);

        Assert.Equal(0, constants["iState_SprigganDefault"]);
        Assert.Equal(1, constants["iState_SprigganCombat"]);

        ProjectStep step = walk.Steps.First(s => s.Node is BSIStateManagerModifier);
        var manager = (BSIStateManagerModifier)step.Node;

        var bindings = (manager.m_variableBindingSet!.m_bindings)
            .ToDictionary(b => b.m_memberPath, b => variables.NameOf(step.File, b.m_variableIndex));

        var pairs = new List<(string State, string? Constant)>();
        for (int i = 0; i < manager.m_stateData.Count; i++)
        {
            var data = manager.m_stateData[i];
            var machine = (hkbStateMachine)data.m_pStateMachine!;

            pairs.Add((machine.m_states.First(x => x.m_stateId == data.m_StateID).m_name,
                       bindings.GetValueOrDefault($"stateData:{i}/iStateToSetAs")));
        }

        Assert.Equal(
            [("CombatState", "iState_SprigganCombat"), ("NonCombatState", "iState_SprigganDefault")],
            pairs);

        // the two keys it can write are the two its table ships
        var keys = cache.SpeedData!.Block(name)!.Entries
            .Where(e => e.Records.Count > 0).Select(e => (int)e.Key).Order();

        Assert.Equal([0, 1], keys);
    }
}
