using HKSK.Cache;
using HKX2;

namespace HKSK.Model;

/// <summary>
/// A clip, seen from both sides: the cache entry and the behaviour generator it
/// restates.
/// </summary>
/// <remarks>
/// <see cref="Generator"/> is null when the cache names a clip no loaded
/// behaviour defines. That is not necessarily corruption -- a project can list
/// behaviours it shares with others, and Skyrim's own cache is in places a stale
/// snapshot of graphs that have since gained clips. See
/// <c>HKSK.Validation</c>.
/// </remarks>
public sealed class Clip
{
    /// <summary>The cache's record of the clip.</summary>
    public required ClipGeneratorEntry Entry { get; init; }

    /// <summary>The behaviour graph's generator, when one was found.</summary>
    public hkbClipGenerator? Generator { get; init; }

    /// <summary>The animation slot this clip plays.</summary>
    public AnimationSlot? Slot { get; init; }

    public string Name => Entry.Name;
    public int CacheIndex => Entry.CacheIndex;
    public IReadOnlyList<ClipEvent> Events => Entry.Events;

    /// <summary>The animation the behaviour says this clip plays, if it is loaded.</summary>
    public string? BehaviorAnimation => Generator?.m_animationName;

    public override string ToString() => $"{Name} -> [{CacheIndex}] {Slot?.StoredName ?? "?"}";
}
