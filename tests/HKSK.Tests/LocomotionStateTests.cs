using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The mechanism that picks a creature's locomotion, over all 41 projects that
/// read the speed table.
/// </summary>
/// <remarks>
/// <para>
/// A creature's locomotion is not one blend but a handful of groups -- walking,
/// running, sneaking, mounted, weapon drawn -- and each group is the contents of
/// one state of one state machine. What picks between those states is a variable,
/// and it is named by the machine in one of two ways.
/// </para>
/// <para>
/// None of this is read off a name. The blends are found by following the speed
/// sampler's output variable, the state holding them by ancestry, and the
/// selection by the machine's own fields.
/// </para>
/// </remarks>
public sealed class LocomotionStateTests
{
    private static (SkyrimCache Cache, List<(string Name, ProjectWalk Walk, ProjectVariables Variables)> Projects) Load()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var projects = new List<(string, ProjectWalk, ProjectVariables)>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            projects.Add((at.Name, walk, new ProjectVariables(walk.Steps)));
        }

        return (cache, projects);
    }

    /// <summary>
    /// Every speed-driven blend in the game belongs to exactly one state of one
    /// state machine.
    /// </summary>
    /// <remarks>
    /// 1,037 blends over the 41 projects, grouping into 177 states with none left
    /// over. That grouping is the unit the speed table's keys are keys to, and it
    /// falls out of the structure rather than out of the names.
    /// </remarks>
    [CorpusFact]
    public void EveryBlendBelongsToExactlyOneLocomotionState()
    {
        (_, var projects) = Load();
        int states = 0, blends = 0;

        foreach ((string name, ProjectWalk walk, ProjectVariables variables) in projects)
        {
            List<LocomotionState> found = [.. LocomotionStates.In(walk, variables)];

            // nothing lost and nothing counted twice
            Assert.Equal(
                Locomotion.ConsumersIn(walk.Steps).Count(),
                found.Sum(f => f.Blends.Count));

            Assert.Equal(
                found.Select(f => (f.Machine, f.State)).Distinct().Count(),
                found.Count);

            states += found.Count;
            blends += found.Sum(f => f.Blends.Count);
        }

        Assert.Equal(41, projects.Count);
        Assert.Equal(177, states);
        Assert.Equal(1037, blends);
    }

    /// <summary>
    /// A machine names its selector in one of two ways, and one of them is not a
    /// binding.
    /// </summary>
    /// <remarks>
    /// 118 of the 177 states are chosen by a <c>startStateId</c> binding and 34 by
    /// the <c>m_syncVariableIndex</c> field. The second is a plain integer, so
    /// anything inspecting only binding sets misses it -- including the case that
    /// matters most, the benthic lurker. The remaining 25 name nothing and are left
    /// to ordinary event transitions.
    /// </remarks>
    [CorpusFact]
    public void AMachineNamesItsSelectorByABindingOrByAnIndexField()
    {
        (_, var projects) = Load();
        var how = new Dictionary<SelectedBy, int>();

        foreach ((_, ProjectWalk walk, ProjectVariables variables) in projects)
            foreach (LocomotionState state in LocomotionStates.In(walk, variables))
            {
                how.TryGetValue(state.Selection, out int count);
                how[state.Selection] = count + 1;

                // the two mechanisms are exclusive: a bound machine never also syncs
                bool bound = (state.Machine.m_variableBindingSet?.m_bindings ?? [])
                    .Any(b => b.m_memberPath == "startStateId");

                if (state.Selection == SelectedBy.SyncVariable) Assert.False(bound);
                if (state.Selection == SelectedBy.StartStateBinding) Assert.True(bound);
                if (state.Selection == SelectedBy.Transitions) Assert.Null(state.Variable);
            }

        Assert.Equal(118, how[SelectedBy.StartStateBinding]);
        Assert.Equal(34, how[SelectedBy.SyncVariable]);
        Assert.Equal(25, how[SelectedBy.Transitions]);
    }

    /// <summary>The whole vocabulary of locomotion selectors in the shipped game.</summary>
    /// <remarks>
    /// Eleven variables, and not one of them is chosen by convention: the same role
    /// is played by <c>iMovementSpeed</c> for a quadruped, <c>iStateRunWalk</c> for
    /// the falmer and the giant, and <c>iState</c> for the benthic lurker.
    /// </remarks>
    [CorpusFact]
    public void ElevenVariablesSelectLocomotionAcrossTheCorpus()
    {
        (_, var projects) = Load();
        var variables = new Dictionary<string, int>();

        foreach ((_, ProjectWalk walk, ProjectVariables names) in projects)
            foreach (LocomotionState state in LocomotionStates.In(walk, names))
            {
                if (state.Variable is null) continue;

                variables.TryGetValue(state.Variable, out int count);
                variables[state.Variable] = count + 1;
            }

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["iSyncIdleLocomotion"] = 42,
                ["iIsInSneak"] = 24,
                ["iSyncSprintState"] = 22,
                ["iRightHandType"] = 19,
                ["i1stPerson"] = 16,
                ["iMovementSpeed"] = 14,
                ["iStateRunWalk"] = 4,
                ["iSyncIdleState"] = 3,
                ["bPerkQuickShot"] = 3,
                ["iState"] = 3,
                ["IntDirection"] = 2,
            },
            variables);
    }

    /// <summary>
    /// A gait change is two states of one machine, driven by a variable, and three
    /// different variables do it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what section 9 could not recover from names or from movement-type
    /// speeds. The pairs are structural and the driver is stated by the machine:
    /// </para>
    /// <code>
    /// iMovementSpeed   Bear, Dog, Wolf, Deer, Boar, Scrib, SabreCat
    ///                  ForwardLocomotionBehavior  #0 ForwardWalkState / #1 ForwardRunState
    /// iStateRunWalk    Falmer   1HM_Locomotion_Behavior   #0 Run  / #1 Walk
    ///                  Giant    CombatLocomotionBehavior  #0 WALK / #1 RUN
    /// iState           BenthicLurker CombatLocomotionBehavior #0 WALK / #1 RUN
    /// </code>
    /// <para>
    /// The state ids are <strong>inverted between the falmer and the giant</strong>,
    /// so the variable's value maps to a state id and the id to a gait per machine.
    /// Nothing about the numbering is conventional, which is why reading it off the
    /// structure is the only way.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void AGaitPairIsTwoStatesOfOneMachineDrivenByAVariable()
    {
        (_, var projects) = Load();

        var pairs = new List<(string Project, string Machine, string? Variable, string[] States)>();

        foreach ((string name, ProjectWalk walk, ProjectVariables variables) in projects)
            foreach (var group in LocomotionStates.In(walk, variables)
                         .GroupBy(s => s.Machine, ReferenceEqualityComparer.Instance))
            {
                if (group.Count() < 2) continue;

                pairs.Add((name, ((hkbStateMachine)group.Key).m_name, group.First().Variable,
                           [.. group.Select(s => s.State.m_name)]));
            }

        Assert.Equal(26, pairs.Count);

        // the quadrupeds, on the raw movement speed
        var quadrupeds = pairs.Where(p => p.Variable == "iMovementSpeed").ToList();
        Assert.Equal(7, quadrupeds.Count);
        Assert.All(quadrupeds, p => Assert.Equal("ForwardLocomotionBehavior", p.Machine));

        // the falmer and the giant, on a variable of their own
        var runWalk = pairs.Where(p => p.Variable == "iStateRunWalk").ToList();
        Assert.Equal(["FalmerProject", "GiantProject"], runWalk.Select(p => p.Project).Order());

        // and the state ids mean opposite things in the two
        Assert.Equal(["1HM_DirectionalState_Run", "1HM_DirectionalState_Walk"],
                     runWalk.First(p => p.Project == "FalmerProject").States);
        Assert.Equal(["CombatDirectionalState_WALK", "CombatDirectionalState_RUN"],
                     runWalk.First(p => p.Project == "GiantProject").States);
    }

    /// <summary>
    /// The benthic lurker is the only creature whose locomotion is selected by
    /// <c>iState</c> itself.
    /// </summary>
    /// <remarks>
    /// And it does it through <c>m_syncVariableIndex</c> rather than a binding,
    /// which is why a search over binding sets says <c>iState</c> selects nothing.
    /// Its speed table is one of only two with a record spanning a gait change, so
    /// the mechanism and the anomaly are the same creature.
    /// </remarks>
    [CorpusFact]
    public void OnlyTheBenthicLurkerIsSelectedByIStateItself()
    {
        (_, var projects) = Load();
        var byIState = new List<(string Project, string Machine, SelectedBy How)>();

        foreach ((string name, ProjectWalk walk, ProjectVariables variables) in projects)
            foreach (LocomotionState state in LocomotionStates.In(walk, variables))
                if (state.Variable == "iState")
                    byIState.Add((name, state.Machine.m_name, state.Selection));

        Assert.Equal(3, byIState.Count);
        Assert.All(byIState, x => Assert.Equal("BenthicLurkerProject", x.Project));
        Assert.All(byIState, x => Assert.Equal(SelectedBy.SyncVariable, x.How));

        Assert.Equal(
            ["CombatLocomotionBehavior", "CombatLocomotionBehavior", "NonCombatLocomotionStateMachine"],
            byIState.Select(x => x.Machine).Order());
    }

    /// <summary>
    /// A locomotion state carries the table key when the graph declares it, which
    /// it does for a little under half of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 84 of the 177 states know their <c>iState</c> from the graph alone, by any of
    /// the four routes it offers: a <c>BSiStateTaggingGenerator</c> above them, a
    /// <c>BSIStateManagerModifier</c> naming their (machine, state) pair, a tagging
    /// generator <em>below</em> them guarding the blends the state holds, or a
    /// machine that chooses on <c>iState</c> itself -- where it does, the state it
    /// sits in <em>is</em> the value, so the key is the state id. Only the benthic
    /// lurker's three states come that last way.
    /// </para>
    /// <para>
    /// <strong>The third route is worth 25 states and all of them are the player's.</strong>
    /// Bethesda arranged <c>1hm_locomotion</c> the other way up from everyone
    /// else's graph: one state per weapon whose contents begin with
    /// <c>1HM_iStateGen</c>, <c>2HM_iStateGen</c>, <c>Bow_iStateGen</c>. Asking the
    /// state reaches nothing and keys 6, 7 and 8 look undeclared when the graph
    /// declares them plainly.
    /// </para>
    /// <para>
    /// The other 93 declare nothing, and for those the key has to come from the
    /// movement types -- which is why those are a required input rather than a
    /// convenience.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void EightyFourOfTheStatesCarryTheirKeyInTheGraph()
    {
        (_, var projects) = Load();
        int tagged = 0, untagged = 0;

        foreach ((_, ProjectWalk walk, ProjectVariables variables) in projects)
            foreach (LocomotionState state in LocomotionStates.In(walk, variables))
                if (state.Key is not null) tagged++; else untagged++;

        Assert.Equal(84, tagged);
        Assert.Equal(93, untagged);
    }
}
