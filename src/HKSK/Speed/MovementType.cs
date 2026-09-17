namespace HKSK.Speed;

/// <summary>
/// A movement type's speeds, as the rebuild needs them.
/// </summary>
/// <param name="Name">The editor name, which <c>iState_&lt;MOVT&gt;</c> carries.</param>
/// <param name="ForwardWalk">Forward walking speed, game units per second.</param>
/// <param name="ForwardRun">Forward running speed.</param>
/// <param name="BackWalk">Backward walking speed.</param>
/// <param name="BackRun">Backward running speed.</param>
/// <param name="LeftWalk">Left strafing walk speed.</param>
/// <param name="LeftRun">Left strafing run speed.</param>
/// <param name="RightWalk">Right strafing walk speed.</param>
/// <param name="RightRun">Right strafing run speed.</param>
/// <remarks>
/// <para>
/// This is the third input, beside the behaviour graph and the root motion, and
/// the only one that does not live in the animation cache. It is a plugin record,
/// so the library cannot read it and should not try: a caller hands it in.
/// </para>
/// <para>
/// <strong>The blend rungs are these numbers.</strong> <c>Falmer1HMWalk</c> states a
/// forward walk of 100.44 and a forward run of 175.77, and the rungs of the ladder
/// its records are sampled from sit at 5, 100.442 and 175.774. That is §5.4 taken
/// literally, and it is what makes a gait threshold an input rather than a guess:
/// the switch is at the walk speed.
/// </para>
/// </remarks>
public readonly record struct MovementType(
    string Name,
    float ForwardWalk, float ForwardRun,
    float BackWalk, float BackRun,
    float LeftWalk, float LeftRun,
    float RightWalk, float RightRun)
{
    /// <summary>Whether this movement type walks and runs at the same speed.</summary>
    /// <remarks>
    /// Most of them do, and such a creature never changes gait: <c>GiantDefault</c>
    /// is 61.84 either way, so one ladder answers its whole range.
    /// </remarks>
    public bool OneGait =>
        Near(ForwardWalk, ForwardRun) && Near(BackWalk, BackRun)
        && Near(LeftWalk, LeftRun) && Near(RightWalk, RightRun);

    /// <summary>The speed at which this movement type starts running, or null.</summary>
    public float? RunsAbove => OneGait ? null : ForwardWalk;

    private static bool Near(float a, float b) => MathF.Abs(a - b) <= 0.01f;

    public override string ToString() =>
        $"{Name} fwd {ForwardWalk:G6}/{ForwardRun:G6} back {BackWalk:G6}/{BackRun:G6}";

    /// <summary>
    /// The walk and run speeds for a heading, where 0 is forward and a quarter turn
    /// is right. A heading between two cardinals takes the nearer one: the movement
    /// type states four numbers and the compass is finer than that.
    /// </summary>
    public (float Walk, float Run) At(float direction)
    {
        int quarter = (int)MathF.Round(direction * 4f) & 3;

        return quarter switch
        {
            0 => (ForwardWalk, ForwardRun),
            1 => (RightWalk, RightRun),
            2 => (BackWalk, BackRun),
            _ => (LeftWalk, LeftRun),
        };
    }
}
