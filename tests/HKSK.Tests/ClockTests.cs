using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Whether the engine's missing clock can reach the selection it makes.
/// </summary>
public sealed class ClockTests
{
    /// <summary>
    /// No timer in the game moves a locomotion machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine has no clock, which has stood as an open item on the grounds that
    /// tier 1 would need one for <c>hkbTimerModifier</c> and for transition
    /// durations. The durations only matter while a transition is running and the
    /// speed tables describe a steady state, so the question is the timers -- and
    /// they cannot reach locomotion at all.
    /// </para>
    /// <para>
    /// A timer does not write a variable. <strong>Not one of the 65 in the corpus
    /// carries a binding</strong>; what it has is <c>m_alarmTimeSeconds</c> and
    /// <c>m_alarmEvent</c>, so the only way a clock reaches a selection is through
    /// that event. Between them the 65 raise six: <c>GetUpStart</c> 46 times,
    /// <c>BowRelease</c> and <c>BowReleaseFast</c> six each, <c>IdleOffsetStop</c>
    /// and <c>blockStopInstant</c> three each, and <c>HeadIdle</c> once.
    /// </para>
    /// <para>
    /// <strong>None of the six is a transition event of any locomotion machine.</strong>
    /// Getting up from a ragdoll, releasing an arrow, stopping a block and a head
    /// idle are not how a creature changes gait. So the clock is not missing from
    /// the part of the engine that is implemented, and the open item is closed by
    /// measurement rather than by building one.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void NoTimerReachesALocomotionMachine()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Dictionary<string, int> raised = [];
        int timers = 0, bound = 0, reaching = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects().Where(p => p.Found))
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            HashSet<string> alarms = [];

            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is not hkbTimerModifier timer) continue;

                timers++;
                if (timer.m_variableBindingSet?.m_bindings is { Count: > 0 }) bound++;

                string name = EventName(walk, step.File, timer.m_alarmEvent?.m_id ?? -1);
                raised[name] = raised.GetValueOrDefault(name) + 1;
                alarms.Add(name);
            }

            if (alarms.Count == 0) continue;

            // every event a locomotion state's own machine can transition on
            HashSet<string> moving = [];
            foreach (LocomotionState state in LocomotionStates.In(walk, new ProjectVariables(walk.Steps)))
            {
                foreach (hkbStateMachineStateInfo? info in state.Machine.m_states ?? [])
                    foreach (hkbStateMachineTransitionInfo t in info?.m_transitions?.m_transitions ?? [])
                        moving.Add(EventName(walk, state.File, t.m_eventId));

                foreach (hkbStateMachineTransitionInfo t in state.Machine.m_wildcardTransitions?.m_transitions ?? [])
                    moving.Add(EventName(walk, state.File, t.m_eventId));
            }

            reaching += alarms.Count(moving.Contains);
        }

        Assert.Equal(65, timers);
        Assert.Equal(0, bound);           // a timer writes no variable anywhere

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["GetUpStart"] = 46,
                ["BowRelease"] = 6,
                ["BowReleaseFast"] = 6,
                ["IdleOffsetStop"] = 3,
                ["blockStopInstant"] = 3,
                ["HeadIdle"] = 1,
            },
            raised);

        Assert.Equal(0, reaching);
    }

    private static string EventName(ProjectWalk walk, string file, int id)
    {
        if (id < 0) return $"#{id}";

        foreach (ProjectStep step in walk.Steps)
        {
            if (step.Node is not hkbBehaviorGraph graph || step.File != file) continue;
            IList<string> names = graph.m_data?.m_stringData?.m_eventNames ?? [];

            return id < names.Count ? names[id] : $"#{id}";
        }

        return $"#{id}";
    }
}
