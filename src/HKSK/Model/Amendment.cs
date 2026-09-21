namespace HKSK.Model;

/// <summary>What amending one project did to one of the merged files.</summary>
/// <remarks>
/// An amendment gives the project exactly the entry a whole regeneration would, and
/// touches no other project's: <see cref="Removed"/> is a project the file should not
/// list -- a speed table entry for a creature with no sampler -- rather than a failure.
/// </remarks>
public enum Amendment
{
    /// <summary>The project had no entry and should have none.</summary>
    None,

    /// <summary>The project had no entry, and now has one, at the end.</summary>
    Added,

    /// <summary>The project's entry was rebuilt where it stood.</summary>
    Replaced,

    /// <summary>The project's entry already said what it should.</summary>
    Unchanged,

    /// <summary>The project had an entry it should not have, and it was taken out.</summary>
    Removed,
}

internal static class Amendments
{
    /// <summary>Puts one project's entry into a list, or takes it out, keeping every other entry where it is.</summary>
    /// <param name="entries">The file's entries.</param>
    /// <param name="isProject">Whether an entry is the project's.</param>
    /// <param name="entry">The entry it should have, or null for none.</param>
    /// <param name="text">What the file says for an entry, to tell a rebuilt one from a changed one.</param>
    public static Amendment Apply<T>(List<T> entries, Func<T, bool> isProject, T? entry, Func<T, string> text) where T : class
    {
        int at = entries.FindIndex(e => isProject(e));

        if (entry is null)
        {
            if (at < 0) return Amendment.None;
            entries.RemoveAt(at);
            return Amendment.Removed;
        }

        if (at < 0)
        {
            entries.Add(entry);
            return Amendment.Added;
        }

        if (text(entries[at]) == text(entry)) return Amendment.Unchanged;

        entries[at] = entry;
        return Amendment.Replaced;
    }

    /// <summary>The project a caller named, or a clear refusal: an amendment has to know what it is amending.</summary>
    public static CacheProject Open(SkyrimCache cache, string projectName) =>
        cache.Open(projectName)
        ?? throw new ArgumentException($"the animation data lists no project '{projectName}'", nameof(projectName));
}
