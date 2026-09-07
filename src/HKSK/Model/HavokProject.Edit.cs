using HKSK.Cache;

namespace HKSK.Model;

public sealed partial class HavokProject
{
    /// <summary>
    /// Adds an animation to the project and returns the slot it took.
    /// </summary>
    /// <remarks>
    /// Appended rather than inserted in sorted position, deliberately: a slot's
    /// index is its identity throughout the cache, so appending is the only
    /// change that leaves every existing clip and root motion block still
    /// pointing at what it pointed at before. The animation lists in the shipped
    /// game are mostly, but not reliably, alphabetical -- the order is not load
    /// bearing, and the numbering is.
    ///
    /// The animation is also registered with every animation set that already
    /// covers this project's animations, so the checksum blocks stay complete.
    /// </remarks>
    /// <param name="storedName">
    /// The animation as Havok stores it, e.g. <c>Animations\WalkForward.HKX</c>.
    /// </param>
    /// <param name="dataRelativeFolder">
    /// The animation's folder relative to the data folder, used for the set data
    /// checksums, e.g. <c>meshes\actors\ambient\chicken\animations</c>. When
    /// null the checksum blocks are left alone.
    /// </param>
    public AnimationSlot AddAnimation(string storedName, string? dataRelativeFolder = null)
    {
        if (string.IsNullOrWhiteSpace(storedName))
            throw new ArgumentException("an animation needs a name", nameof(storedName));

        if (Character is null)
            throw new InvalidOperationException(
                $"'{Name}' was opened without its character file, and the animation list " +
                "lives there. Open it with its project .hkx to add animations.");

        AnimationSlot? existing = Animation(storedName);
        if (existing is not null) return existing;

        Character.AnimationNames.Add(storedName);

        if (dataRelativeFolder is not null)
        {
            string stem = Path.GetFileNameWithoutExtension(storedName.Replace('\\', '/'));
            string path = $"{dataRelativeFolder.TrimEnd('\\', '/')}\\{stem}.hkx";

            foreach (ProjectAttackBlock set in Sets?.Sets.Sets ?? [])
                set.Checksums.Add(path);
        }

        Rebuild();
        return _slots[^1];
    }

    /// <summary>
    /// Removes an animation, renumbering everything that referred to a later one.
    /// </summary>
    /// <remarks>
    /// This is the edit that hand-editing gets wrong. Dropping an entry from the
    /// character's animation list shifts every slot after it down by one, which
    /// silently repoints every clip and every root motion block above the hole
    /// at the wrong animation. So the clip entries and movement blocks are
    /// renumbered here in the same step.
    ///
    /// Clips that played the removed animation are removed too -- there is no
    /// correct index to give them.
    /// </remarks>
    /// <returns>The clips that were removed along with it.</returns>
    public IReadOnlyList<string> RemoveAnimation(AnimationSlot slot)
    {
        if (Character is null)
            throw new InvalidOperationException(
                $"'{Name}' was opened without its character file, and the animation list lives there.");

        if (slot.Index < 0 || slot.Index >= Character.AnimationNames.Count)
            throw new ArgumentOutOfRangeException(nameof(slot), $"no slot {slot.Index} in '{Name}'");

        int removed = slot.Index;

        var orphaned = Data.Block.Clips
            .Where(c => c.CacheIndex == removed)
            .Select(c => c.Name)
            .ToList();

        Data.Block.Clips.RemoveAll(c => c.CacheIndex == removed);
        foreach (ClipGeneratorEntry clip in Data.Block.Clips)
            if (clip.CacheIndex > removed) clip.CacheIndex--;

        if (Data.Movements is not null)
        {
            Data.Movements.Movements.RemoveAll(m => m.CacheIndex == removed);
            foreach (ClipMovement movement in Data.Movements.Movements)
                if (movement.CacheIndex > removed) movement.CacheIndex--;
        }

        Character.AnimationNames.RemoveAt(removed);

        Rebuild();
        return orphaned;
    }

    /// <summary>
    /// Adds a clip over an existing animation slot.
    /// </summary>
    /// <remarks>
    /// Only the cache entry is created. The behaviour graph is not touched:
    /// adding a generator means placing it in a state machine, which is an
    /// authoring decision this library does not make. A clip added here will
    /// read back with no generator until the behaviour catches up.
    /// </remarks>
    public Clip AddClip(
        string name,
        AnimationSlot slot,
        float playbackSpeed = 1f,
        float cropStartTime = 0f,
        float cropEndTime = 0f,
        IEnumerable<ClipEvent>? events = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("a clip needs a name", nameof(name));

        if (Clip(name) is not null)
            throw new InvalidOperationException($"'{Name}' already has a clip called '{name}'");

        if (!ReferenceEquals(_slots.ElementAtOrDefault(slot.Index), slot))
            throw new ArgumentException($"slot {slot.Index} does not belong to '{Name}'", nameof(slot));

        Data.Block.HasAnimationCache = true;
        Data.Movements ??= new ProjectDataBlock();

        Data.Block.Clips.Add(new ClipGeneratorEntry
        {
            Name = name,
            CacheIndex = slot.Index,
            PlaybackSpeed = playbackSpeed,
            CropStartTime = cropStartTime,
            CropEndTime = cropEndTime,
            Events = [.. events ?? []],
        });

        Rebuild();
        return Clip(name)!;
    }

    /// <summary>Removes a clip. The animation it played stays.</summary>
    public bool RemoveClip(string name)
    {
        int removed = Data.Block.Clips.RemoveAll(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (removed == 0) return false;

        Rebuild();
        return true;
    }

    /// <summary>
    /// Sets the root motion of an animation slot, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// The motion belongs to the animation, so this changes what every clip over
    /// that slot reports. The blocks are kept in cache-index order, which is how
    /// the game's own files are written.
    /// </remarks>
    public void SetRootMotion(AnimationSlot slot, ClipMovement motion)
    {
        Data.Block.HasAnimationCache = true;
        Data.Movements ??= new ProjectDataBlock();

        motion.CacheIndex = slot.Index;

        Data.Movements.Movements.RemoveAll(m => m.CacheIndex == slot.Index);
        Data.Movements.Movements.Add(motion);
        Data.Movements.Movements.Sort((a, b) => a.CacheIndex.CompareTo(b.CacheIndex));

        slot.Motion = motion;
    }

    /// <summary>Removes the root motion recorded for a slot, if any.</summary>
    public bool ClearRootMotion(AnimationSlot slot)
    {
        int removed = Data.Movements?.Movements.RemoveAll(m => m.CacheIndex == slot.Index) ?? 0;
        if (removed == 0) return false;

        slot.Motion = null;
        return true;
    }

    /// <summary>Replaces a clip's events.</summary>
    public void SetEvents(Clip clip, IEnumerable<ClipEvent> events) =>
        clip.Entry.Events = [.. events];
}
