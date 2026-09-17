using HKSK.Behavior;
using HKX2;
using HKSK.Model;

namespace HKSK.Speed;

/// <summary>
/// Which locomotion state serves a speed-table key.
/// </summary>
/// <remarks>
/// <para>
/// Three signals, tried in order of how much they prove:
/// </para>
/// <list type="number">
/// <item><description>
/// a <c>BSiStateTaggingGenerator</c> above the state sets <c>iState</c> to the key.
/// This is the graph stating the answer, and it is available for 8 projects;
/// </description></item>
/// <item><description>
/// an expression says so. <c>iState = iState_DeerDefault + iMovementSpeed</c>, with
/// the state machine selecting on that same <c>iMovementSpeed</c>, means state
/// <c>#n</c> serves key <c>base + n</c> -- the graph stating the pairing outright;
/// </description></item>
/// <item><description>
/// the movement type's speeds appear as rungs of the state's arms. A ladder's rungs
/// are placed at the speeds the movement type asks for, so the eight numbers of a
/// <c>MOVT</c> record identify the state that was built for it. Scored over the four
/// cardinal headings, this decides 17 of the 42 pairings that matter and has not
/// once chosen a state that fails to reproduce the shipped block;
/// </description></item>
/// <item><description>
/// there is one declared movement type and one locomotion state, so there is
/// nothing to choose between.
/// </description></item>
/// </list>
/// <para>
/// A tie or a total absence of matching rungs leaves it undecided rather than
/// guessing, which is what keeps the second rule exact.
/// </para>
/// </remarks>
public static class Pairing
{
    /// <summary>How a state was paired with its key.</summary>
    public enum By { None, Tag, Expression, MovementSpeeds, OnlyOne }

    /// <summary>
    /// The state serving a key, and which signal found it.
    /// </summary>
    /// <param name="states">The project's locomotion states.</param>
    /// <param name="key">The <c>iState</c> value the block is filed under.</param>
    /// <param name="movement">
    /// The movement type the key names, when the masters carry it.
    /// </param>
    /// <param name="declared">How many movement types the project declares.</param>
    public static (LocomotionState? State, By How) For(
        ProjectWalk walk,
        IReadOnlyList<(LocomotionState State, List<(float Direction, SpeedLadder Ladder)> Arms)> states,
        int key,
        MovementType? movement,
        int declared,
        IReadOnlyList<StateAssignment>? expressions = null,
        IReadOnlyDictionary<string, int>? constants = null,
        IReadOnlyList<(StateAssignment Assignment, IHavokObject Node)>? placed = null)
    {
        ArgumentNullException.ThrowIfNull(states);

        foreach ((LocomotionState state, _) in states)
            if (state.Key == key) return (state, By.Tag);

        // iState = <constant>, read where it sits: the state below the nearest
        // ancestor it shares with exactly one of them
        if (placed is not null && constants is not null)
        {
            List<IHavokObject> infos = [.. states.Select(s => (IHavokObject)s.State.State)];

            foreach ((StateAssignment assignment, IHavokObject node) in placed)
            {
                if (assignment.Offset is not null || assignment.Base is null) continue;
                if (!constants.TryGetValue(assignment.Base, out int value) || value != key) continue;

                if (StateExpressions.GovernedBy(walk, node, infos) is not { } governed) continue;

                foreach ((LocomotionState state, _) in states)
                    if (ReferenceEquals(state.State, governed)) return (state, By.Expression);
            }
        }

        // iState = <base> + <selector>, where the machine selects on that variable
        if (expressions is not null && constants is not null)
            foreach (StateAssignment assignment in expressions)
            {
                if (assignment.Offset is null || assignment.Base is null) continue;
                if (!constants.TryGetValue(assignment.Base, out int origin)) continue;

                foreach ((LocomotionState state, _) in states)
                {
                    if (state.Selection != SelectedBy.StartStateBinding) continue;
                    if (!string.Equals(state.Variable, assignment.Offset, StringComparison.Ordinal)) continue;
                    if (origin + state.State.m_stateId != key) continue;

                    return (state, By.Expression);
                }
            }

        if (movement is { } wanted)
        {
            int best = 0, at = -1;
            bool tied = false;

            for (int i = 0; i < states.Count; i++)
            {
                int score = Score(states[i].Arms, wanted);
                if (score == 0) continue;

                if (score > best) { best = score; at = i; tied = false; }
                else if (score == best) tied = true;
            }

            if (at >= 0 && !tied) return (states[at].State, By.MovementSpeeds);
        }

        if (declared == 1 && states.Count == 1) return (states[0].State, By.OnlyOne);

        return (null, By.None);
    }

    /// <summary>
    /// How many of a movement type's eight speeds are rungs of the arm at their own
    /// heading.
    /// </summary>
    private static int Score(
        List<(float Direction, SpeedLadder Ladder)> arms, MovementType movement)
    {
        int score = 0;

        for (int quarter = 0; quarter < 4; quarter++)
        {
            float heading = quarter * 0.25f;

            SpeedLadder? ladder = null;
            foreach ((float at, SpeedLadder arm) in arms)
                if (MathF.Abs(at - heading) < 1e-3f) ladder = arm;

            if (ladder is null) continue;

            (float walking, float running) = movement.At(heading);
            foreach (SpeedRung rung in ladder.Rungs)
            {
                if (MathF.Abs(rung.Weight - walking) <= 0.05f) score++;
                if (MathF.Abs(rung.Weight - running) <= 0.05f) score++;
            }
        }

        return score;
    }
}
