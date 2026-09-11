using HKSK.Fbx;
using HKSK.Havok;
using HKSK.Model;
using HKFBX.Hkx;
using HkFbx = HKFBX.Model;

namespace HKSK.Validation;

/// <summary>
/// Checks that the projects sharing a paired animation agree about it.
/// </summary>
/// <remarks>
/// A paired animation is one file and several cache entries, and nothing in the
/// Havok data ties them together: the file lists are the only record of who is in
/// a pairing, and each project numbers its own slot. That leaves two ways for it
/// to be half-edited, and both load.
///
/// A project that lists one and gives it no clip has an animation nothing can
/// play. A project whose rig matches neither half is listed for an animation that
/// does not animate it -- usually a file copied between pairings.
///
/// What is <em>not</em> checked is that both actors list the file. That looks like
/// the obvious rule and the game contradicts it: 139 of the 294 paired animations
/// a project lists are listed by one project alone, nearly all of them the
/// first-person killmoves, where the partner's half is applied to an actor whose
/// own project never names the file.
///
/// This is separate from <see cref="ConsistencyReport"/> because it spans
/// projects: one project's cache can be perfectly consistent and still be missing
/// its partner.
/// </remarks>
public static class PairingReport
{
    /// <summary>
    /// Checks every pairing the given projects take part in.
    /// </summary>
    /// <remarks>
    /// Reading every animation of every actor to find the paired ones would parse
    /// ten thousand packfiles, so only the candidates are opened: an animation more
    /// than one project lists, or one whose name says <c>Paired</c>. That covers
    /// the shipped game exactly -- all 296 of its combined animations are one or
    /// the other -- and an oddly named pairing listed by a single project is the
    /// one case this will not see.
    /// </remarks>
    public static IReadOnlyList<Finding> Check(SkyrimCache cache, IEnumerable<ActorProject>? among = null)
    {
        ArgumentNullException.ThrowIfNull(cache);

        var projects = (among ?? cache.Actors()).ToList();
        var listedBy = new Dictionary<string, List<ActorProject>>(StringComparer.OrdinalIgnoreCase);

        foreach (ActorProject project in projects)
            foreach (AnimationSlot slot in project.Animations)
            {
                if (slot.StoredName.Length == 0) continue;
                if (project.AnimationPath(slot) is not { } path) continue;

                string key = Path.GetFullPath(path);

                if (!listedBy.TryGetValue(key, out var sharers)) listedBy[key] = sharers = [];
                if (!sharers.Contains(project)) sharers.Add(project);
            }

        List<Finding> findings = [];

        foreach ((string path, List<ActorProject> sharers) in listedBy)
        {
            bool candidate = sharers.Count > 1
                || Path.GetFileName(path).Contains("paired", StringComparison.OrdinalIgnoreCase);

            if (!candidate) continue;

            PairedAnimation paired;

            try
            {
                paired = PairedAnimation.Read(path);
            }
            catch (Exception e)
            {
                // Vanilla has one of these: special_childdollplay2.hkx is listed by
                // two projects and holds no animation at all. Worth saying, not
                // worth calling an error.
                findings.Add(new Finding(Severity.Drift, "paired-unreadable",
                    $"'{Path.GetFileName(path)}' is listed by {sharers.Count} project(s) and cannot be read: {e.Message}"));
                continue;
            }

            if (!paired.IsCombined) continue;

            findings.AddRange(CheckOne(paired, path, sharers, projects));
        }

        return findings;
    }

    private static IEnumerable<Finding> CheckOne(
        PairedAnimation paired, string path, List<ActorProject> sharers, List<ActorProject> projects)
    {
        string name = Path.GetFileName(path);

        // Every one of the game's 294 gives each project that lists it a clip. An
        // animation with a slot and no clip is one nothing can reach.
        foreach (ActorProject project in sharers)
        {
            AnimationSlot? slot = project.Animations
                .FirstOrDefault(candidate => project.AnimationPath(candidate) is { } p
                    && Path.GetFullPath(p).Equals(path, StringComparison.OrdinalIgnoreCase));

            if (slot is not null && !project.ClipsOf(slot).Any())
            {
                yield return new Finding(Severity.Error, "paired-no-clip",
                    $"{project.Name} lists '{name}' as slot {slot.Index} and has no clip over it, so nothing " +
                    "in the cache can play it.");
            }
        }

        if (!paired.Tracks.Contains(PairedAnimation.RootBone, StringComparer.OrdinalIgnoreCase))
        {
            yield return new Finding(Severity.Drift, "paired-no-root",
                $"'{name}' has a {PairedAnimation.PartnerPrefix} half but no {PairedAnimation.RootBone} track, " +
                "so the two actors hang from nothing in common.");
        }

        foreach (ActorProject project in sharers)
        {
            if (Covered(paired, project) is { } covered && covered < 0.5)
            {
                yield return new Finding(Severity.Drift, "paired-rig-mismatch",
                    $"'{name}' is listed by {project.Name}, whose rig accounts for {covered:P0} of the better " +
                    "matching half. The animation drives bones that skeleton does not have.");
            }
        }
    }

    /// <summary>
    /// How much of one half a project's rig accounts for, or null when the rig
    /// cannot be read.
    /// </summary>
    private static double? Covered(PairedAnimation paired, ActorProject project)
    {
        if (project.SkeletonPath is not { } skeleton || !File.Exists(skeleton)) return null;

        HkFbx.Skeleton rig;

        try
        {
            rig = HkxAnimationFile.ReadSkeleton(skeleton);
        }
        catch
        {
            return null;
        }

        var bones = rig.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        double driver = Share(paired.DriverBones, bones);
        double partner = Share(paired.PartnerBones, bones);

        return Math.Max(driver, partner);

        static double Share(IEnumerable<string> half, HashSet<string> bones)
        {
            var names = half.ToList();

            return names.Count == 0 ? 0 : (double)names.Count(bones.Contains) / names.Count;
        }
    }
}
