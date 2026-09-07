using System.Numerics;
using HKFBX.Codec;
using HKFBX.Fbx;
using HKFBX.Hkx;
using HKSK.Cache;
using HKSK.Model;
using LeanMeshIO;
using HkFbx = HKFBX.Model;

namespace HKSK.Fbx;

/// <summary>What happened to one animation.</summary>
public sealed record ExchangeResult(
    string Name,
    string? Path,
    bool Succeeded,
    string? Problem = null,
    int? CacheIndex = null)
{
    public override string ToString() =>
        Succeeded ? $"{Name} -> {Path}" : $"{Name}: {Problem}";
}

/// <summary>
/// Moves animations between a project and FBX files, carrying their events and
/// root motion with them.
/// </summary>
/// <remarks>
/// HKFBX converts a single animation and knows nothing about projects: it asks
/// the caller for the root motion, because Havok keeps that apart from the
/// skeleton and it can come from anywhere. In Skyrim it comes from the cache,
/// which is what this knows.
///
/// So the two halves fit: HKFBX does hkx-to-FBX, and this supplies what HKFBX
/// cannot know -- which skeleton the animation is rigged to, where its file
/// lives, what root motion the game records for it, and which clip's events
/// belong to it -- and puts the results back where the game will read them.
///
/// Every operation is reported rather than thrown: a batch of eighty animations
/// where three fail should still import seventy-seven, and say which three.
/// </remarks>
public sealed class AnimationExchange
{
    private readonly IAnimationCodec _codec;

    /// <summary>
    /// Uses Havok's own spline codec, through mopper.
    /// </summary>
    /// <remarks>
    /// Havok's spline encoder is proprietary and reproducing its choices well
    /// enough for a game to behave is a research project, so the encoder is
    /// Havok's own. mopper.exe is a Win32 binary that runs under Wine, which is
    /// what makes this work off Windows.
    /// </remarks>
    public AnimationExchange() : this(new MopperAnimationCodec()) { }

    public AnimationExchange(IAnimationCodec codec) =>
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));

    // ---------------------------------------------------------------- export

    /// <summary>Writes one animation slot to an FBX file.</summary>
    public ExchangeResult Export(
        HavokProject project, AnimationSlot slot, string fbxPath, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(slot);

        options ??= new ExportOptions();
        string name = slot.FileStem;

        try
        {
            string skeletonPath = project.SkeletonPath
                ?? throw new InvalidOperationException(
                    $"'{project.Name}' has no skeleton to rig the animation to");

            string animationPath = project.AnimationPath(slot)
                ?? throw new FileNotFoundException(
                    $"'{slot.StoredName}' was not found under {project.Folder}");

            (SplineAnimationData spline, IReadOnlyList<short> trackToBone, _,
             IReadOnlyList<HkFbx.AnnotationTrack> annotations) =
                HkxAnimationFile.ReadAnimationWithEvents(animationPath);

            HkFbx.Skeleton skeleton = HkxAnimationFile.ReadSkeleton(skeletonPath);
            HkFbx.SampledAnimation sampled = _codec.Decompress(spline);

            var animation = new HkFbx.SampledAnimation
            {
                FrameCount = sampled.FrameCount,
                TrackCount = sampled.TrackCount,
                Duration = sampled.Duration,
                FrameDuration = sampled.FrameDuration,
                Transforms = sampled.Transforms,
                Floats = sampled.Floats,
                TrackToBone = trackToBone,
                Annotations = EventsFor(project, slot, annotations, options.Events),
                RootMotion = options.IncludeRootMotion
                    ? slot.Motion.ToFbx()
                    : HkFbx.RootMotion.None,
            };

            FbxDocument document = FbxAnimationWriter.Build(
                skeleton, animation, options.TakeName ?? name);

            string? folder = Path.GetDirectoryName(fbxPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            using (FileStream stream = File.Create(fbxPath)) document.Save(stream);

            return new ExchangeResult(name, fbxPath, true, CacheIndex: slot.Index);
        }
        catch (Exception e)
        {
            return new ExchangeResult(name, fbxPath, false, e.Message, slot.Index);
        }
    }

    /// <summary>
    /// Writes the animation a clip plays, taking the clip's events.
    /// </summary>
    /// <remarks>
    /// Several clips can play one animation, so the file is named after the clip
    /// rather than the animation to keep a batch from overwriting itself.
    /// </remarks>
    public ExchangeResult Export(
        HavokProject project, Clip clip, string fbxPath, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clip);

        return clip.Slot is null
            ? new ExchangeResult(clip.Name, fbxPath, false,
                $"clip '{clip.Name}' points at slot {clip.CacheIndex}, which the project does not have")
            : Export(project, clip.Slot, fbxPath, options ?? new ExportOptions { Events = EventSource.CachedClip });
    }

    /// <summary>
    /// Writes every animation of a project into a folder, one FBX each.
    /// </summary>
    /// <remarks>
    /// Slots the project numbers but has no file for are skipped rather than
    /// reported as failures -- Skyrim's own projects have those.
    /// </remarks>
    public IReadOnlyList<ExchangeResult> ExportAll(
        HavokProject project, string folder, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        Directory.CreateDirectory(folder);

        var results = new List<ExchangeResult>();

        foreach (AnimationSlot slot in project.Animations)
        {
            if (slot.StoredName.Length == 0 || project.AnimationPath(slot) is null) continue;

            results.Add(Export(project, slot, Path.Combine(folder, $"{slot.FileStem}.fbx"), options));
        }

        return results;
    }

    private static IReadOnlyList<HkFbx.AnnotationTrack> EventsFor(
        HavokProject project,
        AnimationSlot slot,
        IReadOnlyList<HkFbx.AnnotationTrack> fromAnimation,
        EventSource source) => source switch
        {
            EventSource.Animation => fromAnimation,
            EventSource.None => [],
            EventSource.CachedClip => (project.ClipsOf(slot).FirstOrDefault()?.Events ?? []).ToFbx(),
            _ => fromAnimation,
        };

    // ---------------------------------------------------------------- import

    /// <summary>
    /// Reads an FBX into the project, adding or replacing an animation.
    /// </summary>
    /// <remarks>
    /// Replacing keeps the slot, so every clip and every root motion reference
    /// already pointing at it stays correct. Adding appends a slot, which is the
    /// only change that leaves the rest of the numbering alone.
    ///
    /// This writes the animation packfile immediately, and edits the cache and
    /// the character's animation list in memory. Both of those still have to be
    /// saved -- <c>cache.Save()</c> and, when an animation was added or removed,
    /// <c>project.SaveCharacter()</c>. Saving one without the other leaves the
    /// cache pointing at slots the character file does not have, which
    /// <see cref="Validation.ConsistencyReport"/> reports.
    /// </remarks>
    public ExchangeResult Import(HavokProject project, string fbxPath, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        options ??= new ImportOptions();
        string name = Path.GetFileNameWithoutExtension(fbxPath);

        try
        {
            if (!File.Exists(fbxPath)) throw new FileNotFoundException($"no such file: {fbxPath}", fbxPath);

            if (project.Folder is null)
                throw new InvalidOperationException(
                    $"'{project.Name}' was opened without its Havok files, so there is nowhere to " +
                    "write the animation. Open it through a cache that can find its project .hkx.");

            string stored = options.StoredName ?? $"Animations\\{name}.hkx";

            AnimationSlot slot = project.Animation(stored)
                ?? project.AddAnimation(stored, DataRelativeAnimationFolder(project, stored));

            string target = project.AnimationTarget(slot)!;

            if (File.Exists(target) && !options.Overwrite)
                throw new IOException($"'{target}' already exists and Overwrite is off");

            string template = options.TemplatePath
                ?? (File.Exists(target) ? target : null)
                ?? TemplateFrom(project, slot)
                ?? throw new InvalidOperationException(
                    $"'{project.Name}' has no existing animation to use as a template, and an " +
                    "animation packfile carries a binding and a reference frame that cannot be " +
                    "invented. Set ImportOptions.TemplatePath.");

            FbxDocument document;
            using (FileStream stream = File.OpenRead(fbxPath)) document = FbxDocument.Load(stream);

            HkFbx.Skeleton skeleton = FbxAnimationReader.ReadSkeleton(document);
            HkFbx.SampledAnimation animation = FbxAnimationReader.ReadAnimation(document, skeleton);
            HkFbx.RootMotion motion = FbxAnimationReader.ReadRootMotion(document, skeleton);

            // The travel belongs to the cache, not to the animation, so it comes
            // off the root bone before the animation is compressed. Always --
            // ImportRootMotion decides whether the cache is updated, not whether
            // the animation is left carrying motion it should not have.
            SplineAnimationData spline = _codec.Compress(WithoutRootMotion(animation, skeleton, motion));

            string? folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            HkxAnimationFile.WriteAnimation(template, spline, target);
            ClearExtractedMotion(target);

            if (options.ImportEvents)
            {
                IReadOnlyList<HkFbx.AnnotationTrack> events = FbxAnimationReader.ReadEvents(document);

                // Rewrites the whole file from itself, so it has to follow the
                // animation write rather than share a template with it.
                if (events.Count > 0) HkxAnimationFile.WriteAnnotations(target, events, target);
            }

            if (options.ImportRootMotion && !motion.IsEmpty)
                project.SetRootMotion(slot, motion.ToCache());

            if (options.ClipName is { Length: > 0 } clipName && project.Clip(clipName) is null)
                project.AddClip(clipName, slot);

            return new ExchangeResult(name, target, true, CacheIndex: slot.Index);
        }
        catch (Exception e)
        {
            return new ExchangeResult(name, fbxPath, false, e.Message);
        }
    }

    /// <summary>Imports every FBX in a folder.</summary>
    public IReadOnlyList<ExchangeResult> ImportAll(
        HavokProject project, string folder, ImportOptions? options = null) =>
        ImportAll(project,
            Directory.EnumerateFiles(folder, "*.fbx").OrderBy(f => f, StringComparer.Ordinal),
            options);

    /// <summary>
    /// Imports a batch of FBX files.
    /// </summary>
    /// <remarks>
    /// One failure does not stop the rest: each file is reported on its own, so
    /// a batch that mostly works mostly works. The cache is edited in memory
    /// throughout and saved once by the caller, which keeps a half-finished
    /// batch from leaving a half-written cache on disk.
    ///
    /// <see cref="ImportOptions.StoredName"/> and
    /// <see cref="ImportOptions.ClipName"/> name a single animation, so they are
    /// rejected here rather than quietly applied to one file or to all of them.
    /// Use the overload taking a function to name each file.
    /// </remarks>
    public IReadOnlyList<ExchangeResult> ImportAll(
        HavokProject project, IEnumerable<string> fbxPaths, ImportOptions? options = null)
    {
        if (options?.StoredName is not null)
            throw new ArgumentException(
                "StoredName names one animation and cannot apply to a batch; " +
                "use ImportAll(project, paths, path => options) to name each file.",
                nameof(options));

        if (options?.ClipName is not null)
            throw new ArgumentException(
                "ClipName names one clip and cannot apply to a batch, which would make every " +
                "file collide on it; use ImportAll(project, paths, path => options) instead.",
                nameof(options));

        return ImportAll(project, fbxPaths, _ => options);
    }

    /// <summary>
    /// Imports a batch, choosing the options for each file.
    /// </summary>
    /// <remarks>
    /// The way to import a folder into named animations and clips:
    /// <code>
    /// exchange.ImportAll(project, paths, path => new ImportOptions
    /// {
    ///     StoredName = $@"Animations\{Path.GetFileNameWithoutExtension(path)}.hkx",
    ///     ClipName = Path.GetFileNameWithoutExtension(path),
    /// });
    /// </code>
    /// </remarks>
    public IReadOnlyList<ExchangeResult> ImportAll(
        HavokProject project, IEnumerable<string> fbxPaths, Func<string, ImportOptions?> options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<ExchangeResult>();
        foreach (string path in fbxPaths) results.Add(Import(project, path, options(path)));

        return results;
    }

    /// <summary>
    /// Drops any reference frame the template brought with it.
    /// </summary>
    /// <remarks>
    /// A new animation is built from an existing one, which carries more than
    /// curves. Almost no Skyrim animation has an extracted motion -- 1,196 of
    /// 1,200 sampled from the game have none, because the travel is in the cache
    /// -- but if the template is one of the few that does, the imported
    /// animation would inherit a reference frame belonging to a different
    /// animation and move by it.
    /// </remarks>
    private static void ClearExtractedMotion(string path)
    {
        if (HKX2.Util.ReadHKX(path) is not HKX2.hkRootLevelContainer root) return;

        HKX2.hkaAnimation? animation = root.m_namedVariants
            .Select(v => v?.m_variant)
            .OfType<HKX2.hkaAnimationContainer>()
            .SelectMany(c => c.m_animations)
            .FirstOrDefault();

        if (animation?.m_extractedMotion is null) return;

        animation.m_extractedMotion = null;

        using FileStream stream = File.Create(path);
        HKX2.Util.WriteHKX(root, HKX2.HKXHeader.SkyrimSE(), stream);
    }

    /// <summary>
    /// Takes the root motion back out of the root bone's track.
    /// </summary>
    /// <remarks>
    /// In Skyrim an animation's root bone does not move: of 1,200 animations
    /// sampled from the game, 1,196 carry no extracted motion at all, and the
    /// root track of a run that travels 251 units sits at the origin for every
    /// frame. The travel lives in the cache, and the game applies it.
    ///
    /// FBX has nowhere to put that, so the exporter drives the root bone with it
    /// -- an animator needs to see the travel. Reading it back therefore finds it
    /// twice: once as the root bone's animation and once as root motion. Left
    /// alone, the imported animation carries the travel <em>and</em> the cache
    /// records it, and the actor moves twice as far.
    ///
    /// So it is subtracted here, which returns the root to where Skyrim keeps
    /// it. For an animation that came from the game this yields exactly the
    /// original static root; for one an animator moved by hand it moves all the
    /// travel into the root motion, which is where it belongs.
    /// </remarks>
    private static HkFbx.SampledAnimation WithoutRootMotion(
        HkFbx.SampledAnimation animation, HkFbx.Skeleton skeleton, HkFbx.RootMotion motion)
    {
        if (motion.IsEmpty) return animation;

        int root = skeleton.Roots().FirstOrDefault(-1);
        if (root < 0) return animation;

        // The animation is indexed by track; the root is a bone.
        int track = -1;
        for (int i = 0; i < animation.TrackCount; i++)
            if (animation.BoneForTrack(i) == root) { track = i; break; }

        if (track < 0) return animation;

        var transforms = (HkFbx.BoneTransform[])animation.Transforms.Clone();

        for (int frame = 0; frame < animation.FrameCount; frame++)
        {
            int at = frame * animation.TrackCount + track;
            float time = animation.TimeOf(frame);

            HkFbx.BoneTransform current = transforms[at];

            transforms[at] = new HkFbx.BoneTransform(
                current.Translation - motion.TranslationAt(time),
                Quaternion.Normalize(
                    Quaternion.Inverse(motion.RotationAt(time)) * current.Rotation),
                current.Scale);
        }

        return animation with { Transforms = transforms };
    }

    // An existing animation of the same project shares its skeleton and binding,
    // which is exactly what a template has to supply.
    private static string? TemplateFrom(HavokProject project, AnimationSlot exclude) =>
        project.Animations
            .Where(s => s.Index != exclude.Index && s.StoredName.Length > 0)
            .Select(project.AnimationPath)
            .FirstOrDefault(p => p is not null);

    // The set data names animations by a checksum over their path below the data
    // folder, so the folder has to be expressed that way and not as an absolute.
    private static string? DataRelativeAnimationFolder(HavokProject project, string stored)
    {
        if (project.Folder is null) return null;

        int meshes = project.Folder.LastIndexOf(
            $"{Path.DirectorySeparatorChar}meshes{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

        string root = meshes < 0
            ? $"meshes\\{Path.GetFileName(project.Folder)}"
            : HavokPath_Store(project.Folder[(meshes + 1)..]);

        string relative = stored.Replace('/', '\\');
        int slash = relative.LastIndexOf('\\');

        return slash < 0 ? root : $"{root}\\{relative[..slash]}";
    }

    private static string HavokPath_Store(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '\\').Replace('/', '\\');
}
