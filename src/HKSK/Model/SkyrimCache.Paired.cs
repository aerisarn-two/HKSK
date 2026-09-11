using HKSK.Havok;

namespace HKSK.Model;

/// <summary>One project's part in a paired animation.</summary>
/// <param name="Project">The project that plays it.</param>
/// <param name="Slot">The animation slot it took, which is its cache index.</param>
/// <param name="StoredName">The path as that project stores it, relative to its own folder.</param>
public sealed record PairedPart(ActorProject Project, AnimationSlot Slot, string StoredName);

public sealed partial class SkyrimCache
{
    /// <summary>
    /// The projects that list an animation, which for a paired one are the actors
    /// it animates.
    /// </summary>
    /// <remarks>
    /// Nothing else says who is in a pairing. No IDLE record in any master names a
    /// paired animation -- all 4,343 were checked -- and no Havok file outside the
    /// animations themselves mentions one. The file lists are the record: 164 of
    /// the 283 paired animations the game ships are named by two projects or more.
    ///
    /// More than two is normal, and does not mean more than two actors. The bear's
    /// killmoves are listed by four projects -- the bear, the male, the female and
    /// the first-person rig -- because three of those are views of the same half.
    ///
    /// Opening every actor to answer this parses 49 projects and their Havok
    /// files. Pass <paramref name="among"/> when the candidates are already open.
    /// </remarks>
    public IReadOnlyList<PairedPart> ParticipantsOf(
        string animationPath, IEnumerable<ActorProject>? among = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationPath);

        string wanted = Normalise(animationPath);
        List<PairedPart> parts = [];

        foreach (ActorProject project in among ?? Actors())
            foreach (AnimationSlot slot in project.Animations)
            {
                if (slot.StoredName.Length == 0) continue;
                if (project.AnimationPath(slot) is not { } path) continue;
                if (!Normalise(path).Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;

                parts.Add(new PairedPart(project, slot, slot.StoredName));
                break;
            }

        return parts;
    }

    /// <summary>
    /// Lists an animation in every project that plays it, taking a slot in each.
    /// </summary>
    /// <remarks>
    /// A paired animation is one file and several cache entries, and it is only
    /// consistent when every participant has one. A project that plays half of a
    /// killmove and does not list the file has no index to reach it by, and the
    /// clip that names it plays nothing.
    ///
    /// Each project stores the path its own way -- relative to its own folder, so
    /// the bear's copy reads <c>..\SharedKillMoves\Human&amp;Bear\...</c> and the
    /// human's <c>Animations\...</c> -- and each takes its own slot number, since
    /// an index only means anything inside one project.
    ///
    /// A project that already lists the animation keeps the slot it has: the index
    /// is its identity, and re-registering must not renumber a cache.
    /// </remarks>
    /// <param name="animationPath">The animation packfile, as a path on disk.</param>
    /// <param name="participants">Every project that plays it.</param>
    /// <param name="clipName">A clip to create over it in each project, or null.</param>
    public IReadOnlyList<PairedPart> RegisterPaired(
        string animationPath, IEnumerable<ActorProject> participants, string? clipName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationPath);
        ArgumentNullException.ThrowIfNull(participants);

        List<PairedPart> parts = [];

        foreach (ActorProject project in participants)
        {
            string stored = StoredNameFor(project, animationPath);

            AnimationSlot slot = project.Animation(stored)
                ?? project.AddAnimation(stored, DataRelativeFolderOf(project, stored));

            if (clipName is { Length: > 0 } && project.Clip(clipName) is null)
                project.AddClip(clipName, slot);

            parts.Add(new PairedPart(project, slot, stored));
        }

        return parts;
    }

    /// <summary>
    /// Takes an animation out of every project that lists it.
    /// </summary>
    /// <remarks>
    /// Removing it from one and not the others is the half-edit this exists to
    /// prevent: the file goes, and the projects that still list it keep a slot
    /// pointing at nothing while every later slot of those projects renumbers.
    /// Each project renumbers its own clips and movement blocks as it goes.
    /// </remarks>
    /// <returns>The clips that were removed with it, per project.</returns>
    public IReadOnlyList<(ActorProject Project, IReadOnlyList<string> Clips)> UnregisterPaired(
        string animationPath, IEnumerable<ActorProject>? among = null)
    {
        List<(ActorProject, IReadOnlyList<string>)> removed = [];

        foreach (PairedPart part in ParticipantsOf(animationPath, among))
            removed.Add((part.Project, part.Project.RemoveAnimation(part.Slot)));

        return removed;
    }

    /// <summary>
    /// How a project would store a path that lies outside its own folder.
    /// </summary>
    /// <remarks>
    /// Shared killmoves live in a folder of their own and every participant
    /// reaches sideways into it, which is why the stored name carries <c>..\</c>
    /// segments rather than being a name.
    /// </remarks>
    public static string StoredNameFor(ActorProject project, string animationPath)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Folder is not { } folder)
            return $"Animations\\{Path.GetFileName(animationPath)}";

        return Path.GetRelativePath(folder, animationPath).Replace('/', '\\');
    }

    private static string Normalise(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    // The set data names an animation by a checksum over its path below the data
    // folder, so the folder has to be expressed that way rather than absolutely.
    private static string? DataRelativeFolderOf(ActorProject project, string stored)
    {
        if (project.Folder is not { } folder) return null;

        int meshes = folder.LastIndexOf(
            $"{Path.DirectorySeparatorChar}meshes{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

        string root = meshes < 0
            ? $"meshes\\{Path.GetFileName(folder)}"
            : folder[(meshes + 1)..].Replace('/', '\\');

        string relative = stored.Replace('/', '\\');
        int slash = relative.LastIndexOf('\\');

        // A stored name reaching out of the project folder has to be flattened the
        // same way the game's own set data spells it.
        return slash < 0 ? root : Flatten($"{root}\\{relative[..slash]}");
    }

    private static string Flatten(string path)
    {
        List<string> parts = [];

        foreach (string part in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            if (part != ".") parts.Add(part);
        }

        return string.Join('\\', parts);
    }
}
