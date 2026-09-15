using HKX2;

namespace HKSK.Behavior;

/// <summary>How a state machine's active state is chosen.</summary>
public enum SelectedBy
{
    /// <summary>Nothing names it: ordinary transitions on events decide.</summary>
    Transitions,

    /// <summary>A binding on <c>startStateId</c>.</summary>
    StartStateBinding,

    /// <summary>The <c>m_syncVariableIndex</c> field, which is an index and not a binding.</summary>
    SyncVariable,
}

/// <summary>
/// A state holding a group of speed-driven blends: one entry of a creature's
/// locomotion.
/// </summary>
/// <param name="Machine">The state machine the state belongs to.</param>
/// <param name="State">The state itself.</param>
/// <param name="Blends">The blends under it that read a sampler's output.</param>
/// <param name="Selection">How the machine's state is chosen.</param>
/// <param name="Variable">
/// The variable doing the choosing, when there is one, resolved in the machine's
/// own file.
/// </param>
/// <param name="Key">
/// The <c>iState</c> the state is tagged with, from the nearest
/// <c>BSiStateTaggingGenerator</c> above it, or null when nothing tags it.
/// </param>
/// <param name="File">The packfile the machine lives in.</param>
public readonly record struct LocomotionState(
    hkbStateMachine Machine,
    hkbStateMachineStateInfo State,
    IReadOnlyList<SpeedConsumer> Blends,
    SelectedBy Selection,
    string? Variable,
    int? Key,
    string File)
{
    /// <summary>The packfile's own name, without folder or extension.</summary>
    public string FileName => Path.GetFileNameWithoutExtension(File);

    public override string ToString() =>
        $"{Machine.m_name}#{State.m_stateId} '{State.m_name}' x{Blends.Count}" +
        $" <- {Selection}{(Variable is null ? "" : $" {Variable}")}" +
        (Key is null ? "" : $" [iState={Key}]");
}

public static partial class LocomotionStates
{
    /// <summary>
    /// Groups a project's speed-driven blends into the states that hold them, and
    /// says how each of those states is chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A creature's locomotion is not one blend but a handful of groups -- walking,
    /// running, sneaking, mounted, weapon drawn -- and each group is the contents of
    /// one state of one state machine. That grouping is what the speed table's keys
    /// are keys <em>to</em>, and it is structural: no names are read.
    /// </para>
    /// <para>
    /// How the machine picks between its states is recorded rather than assumed,
    /// because there are two mechanisms and they are easy to confuse. Most machines
    /// bind <c>startStateId</c> to a variable. A minority instead set
    /// <c>m_syncVariableIndex</c>, which is a plain index field and not a binding --
    /// so anything looking only at binding sets misses it, including the
    /// benthic lurker, the one creature whose locomotion really is selected by
    /// <c>iState</c> itself.
    /// </para>
    /// </remarks>
    public static IEnumerable<LocomotionState> In(ProjectWalk walk, ProjectVariables variables)
    {
        var groups = new Dictionary<(hkbStateMachine, hkbStateMachineStateInfo), List<SpeedConsumer>>();
        var order = new List<(hkbStateMachine Machine, hkbStateMachineStateInfo State)>();

        foreach (SpeedConsumer consumer in Locomotion.ConsumersIn(walk.Steps))
        {
            hkbStateMachineStateInfo? state = walk.Nearest<hkbStateMachineStateInfo>(consumer.Node);
            hkbStateMachine? machine = state is null ? null : walk.Nearest<hkbStateMachine>(state);
            if (state is null || machine is null) continue;

            var key = (machine, state);
            if (!groups.TryGetValue(key, out List<SpeedConsumer>? blends))
            {
                groups[key] = blends = [];
                order.Add(key);
            }

            blends.Add(consumer);
        }

        foreach ((hkbStateMachine machine, hkbStateMachineStateInfo state) in order)
        {
            ProjectStep at = walk.StepOf(machine)!.Value;
            (SelectedBy selection, string? variable) = Selection(machine, at, variables);

            List<SpeedConsumer> blends = groups[(machine, state)];

            yield return new LocomotionState(
                machine, state, blends,
                selection, variable, walk.KeyOf(state) ?? KeyUnder(walk, blends), at.File);
        }
    }

    /// <summary>
    /// The key the state's own blends are tagged with, when the state is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BSiStateTaggingGenerator</c> tags the subtree below it, and where that
    /// subtree is a whole locomotion state the tag sits above the state and
    /// <see cref="ProjectWalk.KeyOf"/> finds it. The player's graph is arranged the
    /// other way round: <c>1hm_locomotion</c> holds one state per weapon whose
    /// <em>contents</em> begin with <c>1HM_iStateGen</c>, <c>2HM_iStateGen</c>,
    /// <c>Bow_iStateGen</c>, so asking the state reaches nothing and keys 6, 7 and 8
    /// look undeclared when the graph declares them plainly.
    /// </para>
    /// <para>
    /// So where the state carries no tag, the blends under it are asked instead --
    /// and only answered when they agree, since a state whose blends are tagged
    /// differently is not one key's worth of locomotion.
    /// </para>
    /// </remarks>
    private static int? KeyUnder(ProjectWalk walk, List<SpeedConsumer> blends)
    {
        int? only = null;

        foreach (SpeedConsumer consumer in blends)
        {
            if (walk.KeyOf(consumer.Node) is not { } key) return null;
            if (only is null) only = key;
            else if (only != key) return null;
        }

        return only;
    }

    /// <summary>How a machine's state is chosen, and by what.</summary>
    private static (SelectedBy, string?) Selection(
        hkbStateMachine machine, ProjectStep at, ProjectVariables variables)
    {
        foreach (hkbVariableBindingSetBinding binding in machine.m_variableBindingSet?.m_bindings ?? [])
            if (binding.m_memberPath == "startStateId")
                return (SelectedBy.StartStateBinding, variables.NameOf(at.File, binding.m_variableIndex));

        if (machine.m_syncVariableIndex >= 0)
            return (SelectedBy.SyncVariable, variables.NameOf(at.File, machine.m_syncVariableIndex));

        return (SelectedBy.Transitions, null);
    }
}
