namespace HKSK.Fbx;

/// <summary>Which events go into an exported FBX.</summary>
public enum EventSource
{
    /// <summary>
    /// The animation's own annotation track.
    /// </summary>
    /// <remarks>
    /// The round-trippable choice, and the default. These events belong to the
    /// animation file, so re-importing writes them back where they came from.
    /// </remarks>
    Animation,

    /// <summary>
    /// The cache's event list for the clip.
    /// </summary>
    /// <remarks>
    /// What the game actually fires, which is the animation's annotations
    /// <em>merged with the behaviour graph's triggers</em> and resolved against
    /// the behaviour's event names. Useful to see the whole picture, but it is
    /// not the animation's own data: importing it back would write the
    /// behaviour's triggers into the animation file as annotations, and the game
    /// would then fire them twice. Export with this only to look.
    /// </remarks>
    CachedClip,

    /// <summary>No events.</summary>
    None,
}

/// <summary>What to put in an exported FBX.</summary>
public sealed record ExportOptions
{
    /// <summary>Where the events come from. Defaults to the animation's own.</summary>
    public EventSource Events { get; init; } = EventSource.Animation;

    /// <summary>
    /// Whether to attach the root motion the cache records for the slot.
    /// </summary>
    /// <remarks>
    /// On by default. The cache is where a project's root motion is kept, and it
    /// is what the game reads, so it is the version worth showing an animator.
    /// </remarks>
    public bool IncludeRootMotion { get; init; } = true;

    /// <summary>
    /// The take name written into the FBX. Defaults to the animation's file stem.
    /// </summary>
    public string? TakeName { get; init; }
}

/// <summary>What to do with an imported FBX.</summary>
public sealed record ImportOptions
{
    /// <summary>
    /// The animation to write, as Havok stores it, e.g.
    /// <c>Animations\WalkForward.hkx</c>. Defaults to <c>Animations\&lt;fbx stem&gt;.hkx</c>.
    /// </summary>
    public string? StoredName { get; init; }

    /// <summary>
    /// An existing packfile to build the new one from.
    /// </summary>
    /// <remarks>
    /// An animation file carries more than curves -- the binding, the annotation
    /// tracks, the extracted motion -- and HKFBX replaces only the compressed
    /// animation rather than inventing the rest. Replacing an animation uses
    /// itself; adding one has nothing to start from, so another animation of the
    /// same project is used, which is right because they share a skeleton and a
    /// binding. Set this to choose.
    /// </remarks>
    public string? TemplatePath { get; init; }

    /// <summary>
    /// Whether to take the FBX's root motion into the cache for the slot.
    /// </summary>
    public bool ImportRootMotion { get; init; } = true;

    /// <summary>
    /// Whether to write the FBX's events into the animation's annotation track.
    /// </summary>
    public bool ImportEvents { get; init; } = true;

    /// <summary>
    /// A clip to create over the imported animation, if it does not exist yet.
    /// </summary>
    /// <remarks>
    /// Only the cache entry is created; the behaviour graph is not touched, so
    /// the clip has no generator until a graph defines one. Leave null to import
    /// the animation without giving anything a way to play it.
    /// </remarks>
    public string? ClipName { get; init; }

    /// <summary>
    /// Whether importing may overwrite an animation packfile that already exists.
    /// </summary>
    public bool Overwrite { get; init; } = true;
}
