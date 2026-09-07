using System.Numerics;
using HKSK.Cache;
using HkFbx = HKFBX.Model;

namespace HKSK.Fbx;

/// <summary>
/// Translates between the cache's root motion and events and HKFBX's.
/// </summary>
/// <remarks>
/// The two libraries describe the same things and deliberately do not share a
/// model. HKFBX converts one animation and knows nothing about projects; HKSK
/// knows where an animation's motion and events are kept and nothing about FBX.
/// This is the seam.
///
/// Nothing is reinterpreted here -- times, translations and rotations mean the
/// same thing on both sides -- so these are shape changes only.
/// </remarks>
public static class Conversions
{
    /// <summary>The cache's root motion as HKFBX wants it.</summary>
    public static HkFbx.RootMotion ToFbx(this ClipMovement? movement) =>
        movement is null
            ? HkFbx.RootMotion.None
            : new HkFbx.RootMotion
            {
                Duration = movement.Duration,
                Translations = [.. movement.Translations.Select(t => new HkFbx.TranslationKey(t.Time, t.Value))],
                Rotations = [.. movement.Rotations.Select(r => new HkFbx.RotationKey(r.Time, r.Value))],
            };

    /// <summary>HKFBX's root motion as a cache movement block.</summary>
    /// <remarks>
    /// The cache index is not set here: it belongs to the slot the motion is
    /// attached to, which <c>HavokProject.SetRootMotion</c> fills in.
    /// </remarks>
    public static ClipMovement ToCache(this HkFbx.RootMotion motion) => new()
    {
        Duration = motion.Duration,
        Translations = [.. motion.Translations.Select(t => new TranslationKey(t.Time, t.Value))],
        Rotations = [.. motion.Rotations.Select(r => new RotationKey(r.Time, r.Value))],
    };

    /// <summary>
    /// A flat event list as a single annotation track.
    /// </summary>
    /// <remarks>
    /// The cache keeps one list per clip where Havok keeps one track per
    /// transform track, so everything goes into one track. HKFBX puts a nameless
    /// track into the animation's first, which is where Skyrim's own events live.
    /// </remarks>
    public static IReadOnlyList<HkFbx.AnnotationTrack> ToFbx(this IEnumerable<ClipEvent> events) =>
    [
        new HkFbx.AnnotationTrack
        {
            Name = string.Empty,
            Events = [.. events.Select(e => new HkFbx.AnimationEvent(e.Time, e.Name))],
        },
    ];

    /// <summary>Every event of every track, in time order.</summary>
    public static List<ClipEvent> ToCache(this IEnumerable<HkFbx.AnnotationTrack> tracks) =>
        [.. tracks
            .SelectMany(t => t.Events)
            .OrderBy(e => e.Time)
            .Select(e => new ClipEvent(e.Text, e.Time))];

    /// <summary>Whether a motion says the root goes anywhere.</summary>
    internal static bool Moves(this HkFbx.RootMotion motion) =>
        motion.Translations.Any(t => t.Value != Vector3.Zero) ||
        motion.Rotations.Any(r => r.Value != Quaternion.Identity);
}
