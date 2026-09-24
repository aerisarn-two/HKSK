using HKFBX.Model;

namespace HKSK.Assembly;

/// <summary>
/// The events a clip announces, worked out from what it does and what it is for.
/// </summary>
/// <remarks>
/// <para>
/// An animation announces things as it plays, and the game listens: a footstep makes a sound
/// and leaves a print, a weapon swing starts the arc, and a hit frame is the moment a blow
/// lands and the damage is applied. A creature whose clips announce nothing walks in silence
/// and swings through its enemies without touching them.
/// </para>
/// <para>
/// A bought clip announces nothing, because the engine it was made for asks its animations
/// different questions. So the events are authored, from two different kinds of knowledge.
/// </para>
/// <para>
/// The footsteps are <em>measured</em>. The same reading of the feet that gives a clip its
/// travel says when each foot came down, and that is exactly where a footstep goes. It needs
/// no convention and no guess: the clip is asked.
/// </para>
/// <para>
/// The attack events cannot be measured, because nothing in a creature's feet says when its
/// blow lands. They are placed where the game places its own, which was read off 49 shipped
/// attacks: the swing about a third of the way in, the warning a moment later, and the hit at
/// a little over a third. Those three are tightly grouped -- half of every shipped hit frame
/// falls between a third and a half of the way through -- so a placement in the middle of
/// that is right for a creature nobody has animated to a metronome.
/// </para>
/// </remarks>
public static class ClipAnnotations
{
    /// <summary>Where in an attack the game's own creatures announce each part of it.</summary>
    /// <remarks>
    /// The middle of 49 shipped attacks across the draugr, the falmer, the sabre cat and the
    /// wolf. The swing and the warning sit together and the hit follows a little after, which
    /// is the order every one of them is in.
    /// </remarks>
    public const float Swing = 0.30f, Warning = 0.32f, Hit = 0.37f;

    /// <summary>
    /// How fast a creature must be going before its feet are said to land, in units a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How high a foot lifts cannot answer this, which is worth saying because it is the
    /// obvious thing to try. Measured over a bought skeleton's clips, its idle lifts a foot
    /// 4.2 per cent of its height and its left strafe lifts one 4.3 per cent: the two are
    /// indistinguishable, and one of them is standing still.
    /// </para>
    /// <para>
    /// What separates them is that one goes somewhere. The idle's feet read nought units a
    /// second and the strafe's read twelve, so the travel already measured decides it, and
    /// anything slower than a unit a second is a creature shifting its weight.
    /// </para>
    /// </remarks>
    public const float Walking = 1f;

    /// <summary>What a foot landing is called, for the left foot and the right.</summary>
    public const string FootLeft = "FootLeft", FootRight = "FootRight";

    /// <summary>What a gait announces beside its footsteps, to keep two clips in step.</summary>
    public const string SyncLeft = "syncLeft", SyncRight = "syncRight";

    /// <summary>The events an attack announces, in the order it announces them.</summary>
    public const string WeaponSwing = "weaponSwing", PreHitFrame = "preHitFrame", HitFrame = "HitFrame";

    /// <summary>What a clip should announce, given what it does and what it is for.</summary>
    /// <param name="roles">What the clip is, as its name was read.</param>
    /// <param name="duration">How long it runs for.</param>
    /// <param name="motion">
    /// What its feet did, where they could be read. Null or empty leaves the footsteps out
    /// rather than putting them where a walk would have them.
    /// </param>
    /// <param name="left">
    /// The bones on the creature's left, by the names the travel was read under, so that a
    /// footfall can be told which foot it was.
    /// </param>
    /// <param name="sync">
    /// Whether to announce the synchronising events beside the footsteps, which a gait does
    /// and a one-shot does not: they are what holds two clips in step when the creature
    /// changes from one to the other without breaking stride.
    /// </param>
    public static IReadOnlyList<AnnotationTrack> Of(
        IReadOnlyList<AnimationRole> roles,
        float duration,
        InferredMotion? motion = null,
        IReadOnlyCollection<string>? left = null,
        bool sync = true)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var events = new List<AnimationEvent>();

        // A creature that goes nowhere takes no steps, whatever its feet do while it stands.
        var falls = motion is { Speed: >= Walking } going ? going.Falls : [];

        foreach (FootFall fall in falls)
        {
            bool onTheLeft = left is not null && left.Contains(fall.Bone, StringComparer.OrdinalIgnoreCase);
            events.Add(new AnimationEvent(fall.Time, onTheLeft ? FootLeft : FootRight));

            if (sync && Gait(roles))
                events.Add(new AnimationEvent(fall.Time, onTheLeft ? SyncLeft : SyncRight));
        }

        // An attack announces its swing, then a warning, then the blow landing. A clip too
        // short to hold the three apart still gets all three, in order, because the engine
        // wants each of them and a hit frame nobody sent is a blow that never lands.
        if (roles.Any(r => r.Kind is RoleKind.Attack or RoleKind.PowerAttack or RoleKind.Bash))
        {
            events.Add(new AnimationEvent(duration * Swing, WeaponSwing));
            events.Add(new AnimationEvent(duration * Warning, PreHitFrame));
            events.Add(new AnimationEvent(duration * Hit, HitFrame));
        }

        return events.Count == 0
            ? []
            : [new AnnotationTrack { Name = "", Events = [.. events.OrderBy(e => e.Time)] }];
    }

    private static bool Gait(IReadOnlyList<AnimationRole> roles) =>
        roles.Any(r => r.Kind is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint);
}
