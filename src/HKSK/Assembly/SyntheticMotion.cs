using System.Numerics;
using HKSK.Cache;

namespace HKSK.Assembly;

/// <summary>
/// Root motion made rather than measured, for a clip a creature has not got.
/// </summary>
/// <remarks>
/// <para>
/// Root motion lives in the animation cache and nowhere else: Havok has a place
/// for it in the animation and Skyrim leaves it null on every clip in the game.
/// So where the root goes is a property of an animation <em>slot</em>, not of the
/// animation data, and two slots may hold the same animation and move differently.
/// </para>
/// <para>
/// That is what makes a turn in place cheap to author. The game's own are already
/// rotation and nothing else -- the sabre cat's <c>TurnLoopingL</c> travels zero
/// units and turns 87 degrees over half a second, and the draugr's are the same
/// shape -- so a creature with no turn clip can be given one by putting its idle
/// in a second slot and handing that slot a turn. The feet do not shuffle, which
/// is the difference between this and an authored turn, and the creature turns at
/// a rate somebody chose rather than not turning at all.
/// </para>
/// </remarks>
public static class SyntheticMotion
{
    /// <summary>A turn about the creature's vertical, going nowhere.</summary>
    /// <param name="seconds">How long the clip runs for.</param>
    /// <param name="degrees">
    /// How far it turns, signed: positive is the left turn, negative the right, on
    /// the same reading as the shipped clips.
    /// </param>
    /// <param name="cacheIndex">The slot the motion belongs to.</param>
    public static ClipMovement TurnInPlace(float seconds, float degrees, int cacheIndex = 0)
    {
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "a clip runs for some time");

        float radians = degrees * MathF.PI / 180f;

        return new ClipMovement
        {
            CacheIndex = cacheIndex,
            Duration = seconds,

            // Going nowhere is an empty list rather than a zero key: no block in the
            // shipped game carries a key at time zero, and a turn in place has no
            // displacement to state at all.
            Translations = [],
            Rotations = [new RotationKey(seconds, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians))],
        };
    }

    /// <summary>A straight line forward, going at a speed somebody chose.</summary>
    /// <param name="seconds">How long the clip runs for.</param>
    /// <param name="unitsPerSecond">How fast it carries the creature.</param>
    /// <param name="heading">
    /// Which way, in degrees clockwise from straight ahead: 0 forward, 180 back,
    /// -90 and 90 the two sides.
    /// </param>
    /// <param name="cacheIndex">The slot the motion belongs to.</param>
    /// <remarks>
    /// An animation made for an engine that moves the actor itself carries no travel:
    /// a skeleton bought from a marketplace walks 0.33 units over its whole walk cycle
    /// and runs 10 over its run, which is hip sway and not locomotion. Skyrim moves an
    /// actor from this block, so a clip without one is a creature that slides. The
    /// travel is therefore authored the same way the turn is, and for the same reason:
    /// it lives in the cache rather than in the animation, so it can be.
    /// </remarks>
    public static ClipMovement Travel(float seconds, float unitsPerSecond, float heading = 0f, int cacheIndex = 0)
    {
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "a clip runs for some time");

        float radians = heading * MathF.PI / 180f;
        float distance = unitsPerSecond * seconds;

        // The creature faces +Y, so forward is +Y and a heading turns away from it.
        var displacement = new Vector3(distance * MathF.Sin(radians), distance * MathF.Cos(radians), 0f);

        return new ClipMovement
        {
            CacheIndex = cacheIndex,
            Duration = seconds,
            Translations = [new TranslationKey(seconds, displacement)],
            Rotations = [],
        };
    }

    /// <summary>
    /// A turn rate a creature of this size can be asked for, in degrees a second.
    /// </summary>
    /// <remarks>
    /// The shipped multipliers divide the requested turn by the looping clip's own
    /// rate, and those rates run from the mammoth's 45 to the canines' and the sabre
    /// cat's 112.5, with 90 the commonest by a distance -- the deer's, the goat's,
    /// the horker's and the skeever's. Nothing in the game turns faster than 112.5.
    /// So a creature that has to be given a turn is given 90 unless its size argues
    /// otherwise, and a big one is slowed towards the mammoth's.
    /// </remarks>
    public static float ReasonableTurnRate(float heightInUnits) => heightInUnits switch
    {
        <= 0f => 90f,
        > 300f => 45f,     // mammoth-sized
        > 180f => 60f,     // giant-sized
        _ => 90f,
    };
}
