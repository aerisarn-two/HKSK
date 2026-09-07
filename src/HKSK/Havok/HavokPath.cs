namespace HKSK.Havok;

/// <summary>
/// Resolves the paths Havok data stores into paths on this machine.
/// </summary>
/// <remarks>
/// Every path inside a project is Windows-shaped and written in whatever case
/// the author used: <c>Characters\ChickenCharater.hkx</c> alongside
/// <c>Character Assets\skeleton.HKX</c>. On Windows that resolves regardless;
/// on a case-sensitive filesystem almost none of it does, and the extracted game
/// data is usually all lowercase.
///
/// So a lookup tries the literal path first and falls back to walking the
/// directory case-insensitively, which is what the game effectively does.
/// </remarks>
public static class HavokPath
{
    /// <summary>Turns a stored path into a relative path for this platform.</summary>
    public static string Normalise(string stored) =>
        stored.Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>Turns a relative path back into the form Havok stores.</summary>
    public static string Store(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '\\').Replace('/', '\\');

    /// <summary>
    /// Finds <paramref name="stored"/> relative to <paramref name="baseFolder"/>,
    /// ignoring case at every segment. Returns null when nothing matches.
    /// </summary>
    public static string? Resolve(string baseFolder, string stored)
    {
        string direct = Path.Combine(baseFolder, Normalise(stored));
        if (File.Exists(direct)) return direct;

        string current = baseFolder;
        string[] segments = stored.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == ".") continue;

            if (segments[i] == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            bool last = i == segments.Length - 1;
            string? match = last ? FindFile(current, segments[i]) : FindDirectory(current, segments[i]);
            if (match is null) return null;

            current = match;
        }

        return File.Exists(current) ? current : null;
    }

    private static string? FindFile(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
            : null;

    private static string? FindDirectory(string folder, string name) =>
        Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).FirstOrDefault(d =>
                string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase))
            : null;
}
