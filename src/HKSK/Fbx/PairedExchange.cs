using HKFBX.Codec;
using HKFBX.Fbx;
using LeanMeshIO;
using HKFBX.Hkx;
using HKSK.Havok;
using HKSK.Model;
using HkFbx = HKFBX.Model;

namespace HKSK.Fbx;

/// <summary>What happened to a paired animation, and to each project in it.</summary>
/// <param name="Name">The animation's name.</param>
/// <param name="Path">The packfile that was written, when one was.</param>
/// <param name="Succeeded">Whether it landed.</param>
/// <param name="Problem">What stopped it, when it did not.</param>
/// <param name="Parts">
/// The projects it was registered in, with the slot each took. A pairing has one
/// file and one cache entry per participant, and the indices differ between them.
/// </param>
public sealed record PairedExchangeResult(
    string Name,
    string? Path,
    bool Succeeded,
    string? Problem = null,
    IReadOnlyList<PairedPart>? Parts = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        Succeeded
            ? $"{Name} -> {Path} ({string.Join(", ", (Parts ?? []).Select(p => $"{p.Project.Name}:{p.Slot.Index}"))})"
            : $"{Name}: {Problem}";
}

public sealed partial class AnimationExchange
{
    /// <summary>
    /// Reads an FBX holding both actors back into a paired animation, and lists it
    /// in every project that plays it.
    /// </summary>
    /// <remarks>
    /// The half that ordinary import cannot do. One file is written, and then every
    /// participant is given a slot for it and, optionally, a clip: a project that
    /// plays half a killmove without listing the file has no index to reach it by,
    /// and its clip plays nothing.
    ///
    /// The template has to be a paired animation as well. A packfile carries its
    /// binding, and the binding names the skeleton -- <c>PairedRoot</c> for these
    /// and the actor's own for everything else. Built from an ordinary template the
    /// file would claim to be rigged to one actor's skeleton while carrying tracks
    /// for two, which is the same silent wrongness this exists to remove.
    ///
    /// Root motion is written to every participant, not just to one. The cache
    /// keeps a movement block per project, and in the shipped game all 164 paired
    /// animations that more than one project lists record the same travel in each
    /// -- zero, because a pairing locks the two actors together.
    ///
    /// Like the rest of the exchange this writes the packfile immediately and edits
    /// the cache in memory: <c>cache.Save()</c> and <c>SaveCharacter()</c> on each
    /// participant still have to follow.
    /// </remarks>
    /// <param name="cache">The cache the projects came from.</param>
    /// <param name="fbxPath">The FBX to read, holding a combined skeleton.</param>
    /// <param name="animationPath">Where to write the packfile.</param>
    /// <param name="participants">Every project that plays it.</param>
    public PairedExchangeResult ImportPaired(
        SkyrimCache cache,
        string fbxPath,
        string animationPath,
        IEnumerable<ActorProject> participants,
        ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(participants);

        options ??= new ImportOptions();
        string name = Path.GetFileNameWithoutExtension(fbxPath);
        var projects = participants.ToList();

        try
        {
            if (!File.Exists(fbxPath)) throw new FileNotFoundException($"no such file: {fbxPath}", fbxPath);
            if (projects.Count == 0)
                throw new ArgumentException("a paired animation needs the projects that play it", nameof(participants));

            if (File.Exists(animationPath) && !options.Overwrite)
                throw new IOException($"'{animationPath}' already exists and Overwrite is off");

            FbxDocument document;
            using (FileStream stream = File.OpenRead(fbxPath)) document = FbxDocument.Load(stream);

            HkFbx.Skeleton skeleton = FbxAnimationReader.ReadSkeleton(document);

            if (!skeleton.Bones.Any(bone => PairedAnimation.IsPartnerTrack(bone.Name)))
                throw new InvalidOperationException(
                    $"'{fbxPath}' has no {PairedAnimation.PartnerPrefix} bone, so it holds one actor rather " +
                    "than two. Import it into a project the ordinary way.");

            string template = options.TemplatePath
                ?? PairedTemplate(projects)
                ?? throw new InvalidOperationException(
                    "a paired animation has to be built from a paired template: the packfile carries the " +
                    "binding, and that names PairedRoot. None of the projects given has one to copy. " +
                    "Set ImportOptions.TemplatePath.");

            HkFbx.SampledAnimation animation = FbxAnimationReader.ReadAnimation(document, skeleton);
            HkFbx.RootMotion motion = FbxAnimationReader.ReadRootMotion(document, skeleton);

            // The travel comes off in the order the FBX is in, because that is the
            // order its skeleton describes.
            HkFbx.SampledAnimation flat = WithoutRootMotion(animation, skeleton, motion);

            SplineAnimationData spline = _codec.Compress(
                InTrackOrder(flat, skeleton, PairedAnimation.Read(template).Tracks));

            string? folder = Path.GetDirectoryName(animationPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            HkxAnimationFile.WriteAnimation(template, spline, animationPath);
            ClearExtractedMotion(animationPath);

            if (options.ImportEvents)
            {
                IReadOnlyList<HkFbx.AnnotationTrack> events = FbxAnimationReader.ReadEvents(document);

                if (events.Count > 0) HkxAnimationFile.WriteAnnotations(animationPath, events, animationPath);
            }

            IReadOnlyList<PairedPart> parts = cache.RegisterPaired(animationPath, projects, options.ClipName);

            if (options.ImportRootMotion && !motion.IsEmpty)
                foreach (PairedPart part in parts)
                    part.Project.SetRootMotion(part.Slot, motion.ToCache());

            return new PairedExchangeResult(name, animationPath, true, Parts: parts);
        }
        catch (Exception e)
        {
            return new PairedExchangeResult(name, animationPath, false, e.Message);
        }
    }

    /// <summary>
    /// Puts the FBX's tracks back into the order the binding expects.
    /// </summary>
    /// <remarks>
    /// An FBX carries a tree, and reading one back gives its bones depth first.
    /// A paired animation's tracks are in the order the pair's skeleton was
    /// authored in, which is not that: the bear killmove has the partner's jaw
    /// before its legs, and the two orders part company at bone 13.
    ///
    /// The binding maps track to bone one for one and the template supplies the
    /// binding, so the template's track order is the order that has to be written.
    /// Anything else produces a file that loads, animates, and puts every bone's
    /// motion on some other bone.
    ///
    /// A rig edited in the meantime cannot be written this way at all -- the
    /// binding is copied, not rebuilt, so a new bone has no track to live in --
    /// which is why a set that does not match is refused rather than trimmed.
    /// </remarks>
    private static HkFbx.SampledAnimation InTrackOrder(
        HkFbx.SampledAnimation animation, HkFbx.Skeleton skeleton, IReadOnlyList<string> order)
    {
        var sourceOf = new Dictionary<string, int>(skeleton.Count, StringComparer.OrdinalIgnoreCase);

        for (int bone = 0; bone < skeleton.Count; bone++)
            sourceOf.TryAdd(skeleton.Bones[bone].Name, bone);

        var missing = order.Where(name => !sourceOf.ContainsKey(name)).ToList();
        var extra = skeleton.Bones.Select(bone => bone.Name)
            .Where(name => !order.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count > 0 || extra.Count > 0)
            throw new InvalidOperationException(
                $"the FBX rig does not match the template's: {missing.Count} bone(s) the animation needs are " +
                $"not in it{(missing.Count > 0 ? $" ({string.Join(", ", missing.Take(4))})" : "")}, and " +
                $"{extra.Count} are in it that the animation has no track for" +
                $"{(extra.Count > 0 ? $" ({string.Join(", ", extra.Take(4))})" : "")}. A packfile carries its " +
                "binding from the template, so the bones have to be the ones it already names.");

        int tracks = order.Count;
        var transforms = new HkFbx.BoneTransform[animation.FrameCount * tracks];

        for (int frame = 0; frame < animation.FrameCount; frame++)
            for (int track = 0; track < tracks; track++)
                transforms[(frame * tracks) + track] =
                    animation.Transforms[(frame * animation.TrackCount) + sourceOf[order[track]]];

        return new HkFbx.SampledAnimation
        {
            FrameCount = animation.FrameCount,
            TrackCount = tracks,
            Duration = animation.Duration,
            FrameDuration = animation.FrameDuration,
            Transforms = transforms,
            Floats = animation.Floats,
            Annotations = animation.Annotations,
            RootMotion = animation.RootMotion,
            TrackToBone = Enumerable.Range(0, tracks).Select(track => (short)track).ToList(),
        };
    }

    /// <summary>
    /// An existing paired animation to copy a binding from.
    /// </summary>
    /// <remarks>
    /// Any of the participants will do -- they all play paired animations, or they
    /// would not be participants -- and the first that has one is taken.
    /// </remarks>
    private static string? PairedTemplate(IEnumerable<ActorProject> projects)
    {
        foreach (ActorProject project in projects)
            foreach (AnimationSlot slot in project.Animations)
            {
                if (slot.StoredName.Length == 0) continue;
                if (project.AnimationPath(slot) is not { } path) continue;

                try
                {
                    if (PairedAnimation.Read(path).IsCombined) return path;
                }
                catch
                {
                    // An animation that cannot be read is not a template. The next
                    // one might be.
                }
            }

        return null;
    }
}
