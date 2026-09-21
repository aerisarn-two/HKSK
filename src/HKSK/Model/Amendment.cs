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

    /// <summary>The project had an entry it should not have, and it was taken out.</summary>
    Removed,
}

internal static class Amendments
{
    /// <summary>Puts one project's entry into a list, or takes it out, keeping every other entry where it is.</summary>
    public static Amendment Apply<T>(List<T> entries, Func<T, bool> isProject, T? entry) where T : class
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

        entries[at] = entry;
        return Amendment.Replaced;
    }

    /// <summary>The project a caller named, or a clear refusal: an amendment has to know what it is amending.</summary>
    public static CacheProject Open(SkyrimCache cache, string projectName) =>
        cache.Open(projectName)
        ?? throw new ArgumentException($"the animation data lists no project '{projectName}'", nameof(projectName));
}
