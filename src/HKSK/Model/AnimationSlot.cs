using HKSK.Cache;

namespace HKSK.Model;

/// <summary>
/// One animation of a project, at the position that is its cache index.
/// </summary>
/// <remarks>
/// The slot, not the clip, is what the cache numbers. Several clips can play the
/// same animation at different speeds -- the chicken has
/// <c>Forward_Walk</c> and <c>Forward_WalkSlow</c> over one
/// <c>WalkForward.HKX</c> -- and they share this slot and its root motion.
/// </remarks>
public sealed class AnimationSlot
{
    /// <summary>The position in the character's animation list. This is the cache index.</summary>
    public required int Index { get; init; }

    /// <summary>The animation as stored, e.g. <c>Animations\WalkForward.HKX</c>.</summary>
    public required string StoredName { get; set; }

    /// <summary>The root motion recorded for this animation, if any.</summary>
    public ClipMovement? Motion { get; set; }

    /// <summary>The bare file name without folder or extension, e.g. <c>WalkForward</c>.</summary>
    public string FileStem => Path.GetFileNameWithoutExtension(StoredName.Replace('\\', '/'));

    /// <summary>Whether the cache records this animation as going anywhere.</summary>
    public bool Travels => Motion?.HasMovement ?? false;

    public override string ToString() => $"[{Index}] {StoredName}";
}
