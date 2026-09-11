using HKX2;

namespace HKSK.Havok;

/// <summary>
/// An animation that drives two skeletons at once.
/// </summary>
/// <remarks>
/// Skyrim's killmoves, mounts and executions are one file animating both actors.
/// The file binds to a skeleton called <c>PairedRoot</c> that no packfile in the
/// game declares: it holds the driver's bones under <c>NPC</c> and the partner's
/// under <c>2_</c>, every one of the partner's prefixed. The bone names exist
/// only in the animation's annotation tracks, one per track, which is why this
/// reads them from there.
///
/// Measured against the shipped game: 296 of the 302 animations a project lists
/// as paired carry a partner half, the prefix is <c>2_</c> in all 23,824 prefixed
/// tracks and never <c>3_</c> -- a pairing is two actors, never three -- and the
/// six that carry no partner half are ordinary animations named <c>Paired_*</c>
/// whose other half is a second file.
///
/// The track order is per-file and load bearing. The binding maps tracks to bones
/// one for one, so a rig built for this has to follow the order here rather than
/// either skeleton's own: <c>Human&amp;Draugr</c> lists the partner first,
/// <c>Human&amp;Dragon</c> lists it second.
/// </remarks>
public sealed class PairedAnimation
{
    /// <summary>The root both actors hang from, and the skeleton's name.</summary>
    public const string RootBone = "PairedRoot";

    /// <summary>What every bone of the second actor carries, and its subtree root.</summary>
    public const string PartnerPrefix = "2_";

    /// <summary>The subtree root the driver's own bones hang from.</summary>
    public const string DriverRootBone = "NPC";

    private PairedAnimation(string path, string? boundSkeletonName, IReadOnlyList<string> tracks)
    {
        Path = path;
        BoundSkeletonName = boundSkeletonName;
        Tracks = tracks;

        List<int> driver = [];
        List<int> partner = [];

        for (int track = 0; track < tracks.Count; track++)
        {
            string name = tracks[track];

            // The three structural bones belong to neither actor: the pair's root,
            // and the subtree root each actor hangs from. Neither rig contains its
            // own -- a human rig has no bone called NPC -- so counting them as
            // bones would mean every half looked one short of matching.
            if (name == RootBone) { RootTrack = track; continue; }
            if (name == DriverRootBone) { DriverRootTrack = track; continue; }
            if (name == PartnerPrefix) { PartnerRootTrack = track; continue; }

            (name.StartsWith(PartnerPrefix, StringComparison.Ordinal) ? partner : driver).Add(track);
        }

        DriverTracks = driver;
        PartnerTracks = partner;
    }

    /// <summary>The track holding the root both actors hang from, or -1.</summary>
    public int RootTrack { get; } = -1;

    /// <summary>The track holding the driver's subtree root, or -1.</summary>
    public int DriverRootTrack { get; } = -1;

    /// <summary>The track holding the partner's subtree root, or -1.</summary>
    public int PartnerRootTrack { get; } = -1;

    /// <summary>The animation file this was read from.</summary>
    public string Path { get; }

    /// <summary>
    /// The skeleton the binding names, which is <c>PairedRoot</c> for a paired
    /// animation and the actor's own skeleton for an ordinary one.
    /// </summary>
    public string? BoundSkeletonName { get; }

    /// <summary>Every bone the animation drives, in track order.</summary>
    public IReadOnlyList<string> Tracks { get; }

    /// <summary>The tracks belonging to the driver, as indices into <see cref="Tracks"/>.</summary>
    public IReadOnlyList<int> DriverTracks { get; }

    /// <summary>The tracks belonging to the partner, as indices into <see cref="Tracks"/>.</summary>
    public IReadOnlyList<int> PartnerTracks { get; }

    /// <summary>
    /// Whether this really animates two skeletons, rather than being an ordinary
    /// animation that happens to be named for a pairing.
    /// </summary>
    public bool IsCombined => PartnerTracks.Count > 0 || PartnerRootTrack >= 0;

    /// <summary>The driver's own bones, in track order, without the structural roots.</summary>
    public IEnumerable<string> DriverBones => DriverTracks.Select(track => Tracks[track]);

    /// <summary>The partner's own bones with the prefix taken off, in track order.</summary>
    public IEnumerable<string> PartnerBones => PartnerTracks.Select(track => WithoutPrefix(Tracks[track]));

    /// <summary>Reads the pairing out of an animation packfile.</summary>
    /// <remarks>
    /// Cheap enough to call on every animation of a project: it reads the file,
    /// but only its names. An animation with no partner half comes back with
    /// <see cref="IsCombined"/> false rather than as null, because "this is an
    /// ordinary animation" is an answer worth having.
    /// </remarks>
    public static PairedAnimation Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        HavokFile file = HavokFile.Load(path);

        return From(path, file);
    }

    /// <summary>
    /// The pairing of an animation whose track names are already in hand.
    /// </summary>
    /// <remarks>
    /// Reading an animation for export already yields its annotation tracks, and
    /// those are the bone names. This is that, without opening the file twice.
    /// </remarks>
    public static PairedAnimation FromTracks(
        string path, IEnumerable<string> trackNames, string? boundSkeletonName = null)
    {
        ArgumentNullException.ThrowIfNull(trackNames);

        return new PairedAnimation(path, boundSkeletonName, [.. trackNames]);
    }

    /// <summary>Reads the pairing out of a packfile already loaded.</summary>
    public static PairedAnimation From(string path, HavokFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var animation = file.Objects.OfType<hkaAnimation>().FirstOrDefault()
            ?? throw new InvalidOperationException($"'{path}' holds no animation");

        var binding = file.Objects.OfType<hkaAnimationBinding>().FirstOrDefault();

        var tracks = animation.m_annotationTracks
            .Select(track => track.m_trackName ?? string.Empty)
            .ToList();

        return new PairedAnimation(path, binding?.m_originalSkeletonName, tracks);
    }

    /// <summary>
    /// Whether a binding's skeleton name is the paired one.
    /// </summary>
    /// <remarks>
    /// The cheapest and most reliable test there is: the packfile says what it is
    /// rigged to, and only a paired animation says <c>PairedRoot</c>. Reading the
    /// bone names means parsing the annotation tracks, which is worth doing once
    /// this has already said yes.
    /// </remarks>
    public static bool IsPairedSkeletonName(string? boundSkeletonName) =>
        string.Equals(boundSkeletonName, RootBone, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a bone name belongs to the second actor.</summary>
    public static bool IsPartnerTrack(string? name) =>
        name is not null
        && name.StartsWith(PartnerPrefix, StringComparison.Ordinal)
        && name.Length > PartnerPrefix.Length;

    /// <summary>
    /// A partner bone's name as its own skeleton spells it.
    /// </summary>
    /// <remarks>
    /// The bare prefix is the partner's subtree root and has no name of its own,
    /// so it comes back unchanged rather than as an empty string.
    /// </remarks>
    public static string WithoutPrefix(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return IsPartnerTrack(name) ? name[PartnerPrefix.Length..] : name;
    }

    /// <inheritdoc />
    public override string ToString() =>
        IsCombined
            ? $"{System.IO.Path.GetFileName(Path)}: {DriverTracks.Count} driver, {PartnerTracks.Count} partner"
            : $"{System.IO.Path.GetFileName(Path)}: not paired";
}
