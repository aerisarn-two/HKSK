using HKX2;

namespace HKSK.Engine;

/// <summary>Where a raised event takes a state machine.</summary>
/// <remarks>
/// <para>
/// A machine holds transitions in two places: each state's own
/// <c>m_transitions</c>, and the machine's <c>m_wildcardTransitions</c>, which
/// apply from any state. A transition names the event that triggers it, the state
/// it goes to, and optionally a condition that has to hold.
/// </para>
/// <para>
/// Timing is not modelled. <c>m_triggerInterval</c> and <c>m_initiateInterval</c>
/// gate a transition on where the clip below has got to, and the flags say things
/// like whether a transition may interrupt itself -- both are about how a machine
/// gets somewhere rather than where it ends up, and the tables this serves
/// describe the steady state.
/// </para>
/// </remarks>
public static class Transitions
{
    /// <summary>From the runtime's own <c>TransitionFlags</c>.</summary>
    private const int FlagDisabled = 32, FlagDisableCondition = 256, FlagToNestedStateIdIsValid = 0x2000;

    /// <summary>
    /// The state a machine settles in, following transitions from its start state
    /// while the raised events keep taking it somewhere new.
    /// </summary>
    public static int Settle(
        hkbStateMachine machine, int from, Events events, Variables variables,
        Properties? properties) =>
        Settle(machine, from, events, variables, properties, out _);

    /// <summary>
    /// The state a machine settles in, and the state the last transition taken names
    /// inside it: a transition may carry <c>toNestedStateId</c>, which the machine
    /// below the target starts in instead of its own start state. That is how the
    /// humanoid's bleedout enters its third-person branch.
    /// </summary>
    public static int Settle(
        hkbStateMachine machine, int from, Events events, Variables variables,
        Properties? properties, out int? nested)
    {
        int at = from;
        nested = null;
        HashSet<int> seen = [at];

        // A cycle is possible -- two states each transitioning to the other on the
        // same event -- so stop when one repeats rather than counting passes.
        while (Step(machine, at, events, variables, properties) is { } taken && seen.Add(taken.m_toStateId))
        {
            at = taken.m_toStateId;
            nested = (taken.m_flags & FlagToNestedStateIdIsValid) != 0 ? taken.m_toNestedStateId : null;
        }

        return at;
    }

    /// <summary>The state one raised event moves a machine to, or null.</summary>
    /// <remarks>
    /// A state's own transitions are considered before the machine's wildcards, and
    /// within each, higher <c>m_priority</c> first. That ordering is inferred: the
    /// field exists to break ties and nothing else in the file says how.
    /// </remarks>
    public static int? Next(
        hkbStateMachine machine, int from, Events events, Variables variables,
        Properties? properties) =>
        Step(machine, from, events, variables, properties)?.m_toStateId;

    private static hkbStateMachineTransitionInfo? Step(
        hkbStateMachine machine, int from, Events events, Variables variables,
        Properties? properties)
    {
        foreach (hkbStateMachineTransitionInfo info in Candidates(machine, from))
        {
            if ((info.m_flags & FlagDisabled) != 0) continue;
            if (!events.Raised(variables.EventNameOf(info.m_eventId))) continue;
            if ((info.m_flags & FlagDisableCondition) == 0 && !Holds(info.m_condition, variables))
                continue;

            if (info.m_toStateId == from) continue;

            if (StateOf(machine, info.m_toStateId) is not null) return info;
        }

        return null;
    }

    private static IEnumerable<hkbStateMachineTransitionInfo> Candidates(
        hkbStateMachine machine, int from)
    {
        IList<hkbStateMachineTransitionInfo> local =
            StateOf(machine, from)?.m_transitions?.m_transitions ?? [];
        IList<hkbStateMachineTransitionInfo> wild =
            machine.m_wildcardTransitions?.m_transitions ?? [];

        return local.OrderByDescending(t => t.m_priority)
            .Concat(wild.OrderByDescending(t => t.m_priority));
    }

    /// <summary>Whether a transition's condition holds. No condition means it does.</summary>
    /// <remarks>
    /// <c>hkbExpressionCondition</c> is the only one that decides anything here:
    /// its expression is read as a boolean. <c>hkbStringCondition</c> appears nine
    /// times in five projects and carries a string the runtime hands to the game,
    /// so nothing in the files says what it means; it is treated as holding.
    /// </remarks>
    public static bool Holds(hkbCondition? condition, Variables variables)
    {
        if (condition is not hkbExpressionCondition expression) return true;

        Expression? parsed = Expression.Parse(expression.m_expression);
        if (parsed is null) return true;

        return parsed.TryEvaluate(variables, out float value) ? value != 0f : true;
    }

    private static hkbStateMachineStateInfo? StateOf(hkbStateMachine machine, int stateId)
    {
        foreach (hkbStateMachineStateInfo state in machine.m_states ?? [])
            if (state.m_stateId == stateId) return state;

        return null;
    }
}
