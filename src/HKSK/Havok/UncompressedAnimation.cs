using System.Numerics;
using HKX2;
using HkFbx = HKFBX.Model;

namespace HKSK.Havok;

/// <summary>How an imported animation is stored.</summary>
public enum AnimationCompression
{
    /// <summary>
    /// Every frame's transform for every track, as <c>hkaInterleavedUncompressedAnimation</c>:
    /// exactly what was imported, with nothing lost to quantisation, and written without
    /// Havok's codec.
    /// </summary>
    Uncompressed,

    /// <summary>
    /// Spline-compressed, as the game's own animations are: smaller, lossy, and written by
    /// Havok's codec, which runs as <c>mopper.exe</c> -- through Wine off Windows.
    /// </summary>
    Spline,
}

/// <summary>
/// An animation packfile holding its tracks uncompressed: written over a template, and read
/// back into samples.
/// </summary>
/// <remarks>
/// <para>
/// A spline-compressed animation needs Havok's codec both ways, and the codec is a 32-bit
/// Windows binary. An interleaved uncompressed one is the samples themselves -- frame-major,
/// one <c>hkQsTransform</c> per track per frame, the layout
/// <see cref="HkFbx.SampledAnimation"/> already has -- so it is written and read here with
/// nothing but the packfile serialiser. It is larger, and it is exact: what was imported is
/// what plays.
/// </para>
/// <para>
/// Written over a template, as the compressed path is, because the packfile carries more than
/// the curves: the binding that says which bone each track drives, the skeleton it was made
/// for, the annotation tracks. Only the animation object is replaced, and the binding pointed
/// at the new one.
/// </para>
/// </remarks>
public static class UncompressedAnimation
{
    /// <summary>Writes <paramref name="animation"/> uncompressed into a copy of <paramref name="template"/>.</summary>
    public static void Write(string template, HkFbx.SampledAnimation animation, string output)
    {
        ArgumentNullException.ThrowIfNull(animation);

        var root = (hkRootLevelContainer)Util.ReadHKX(template);
        hkaAnimationContainer container = ContainerOf(root, template);
        hkaAnimation old = container.m_animations.FirstOrDefault()
            ?? throw new InvalidDataException($"'{template}' holds no animation to replace");

        int floatTracks = animation.FrameCount == 0 ? 0 : animation.Floats.Length / animation.FrameCount;

        var interleaved = new hkaInterleavedUncompressedAnimation
        {
            m_type = (int)AnimationType.HK_INTERLEAVED_ANIMATION,
            m_duration = animation.Duration,
            m_numberOfTransformTracks = animation.TrackCount,
            m_numberOfFloatTracks = floatTracks,
            m_extractedMotion = null,
            m_annotationTracks = old.m_annotationTracks,
            m_transforms = [.. animation.Transforms.Select(ToQs)],
            m_floats = [.. animation.Floats],
        };

        container.m_animations = [interleaved];
        foreach (hkaAnimationBinding binding in container.m_bindings)
            if (ReferenceEquals(binding.m_animation, old)) binding.m_animation = interleaved;

        string? folder = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        using FileStream stream = File.Create(output);
        Util.WriteHKX(root, HKXHeader.SkyrimSE(), stream);
    }

    /// <summary>
    /// The samples of an uncompressed animation, or null when the file's animation is
    /// compressed -- which is for Havok's codec to read.
    /// </summary>
    public static HkFbx.SampledAnimation? Read(string path)
    {
        var root = (hkRootLevelContainer)Util.ReadHKX(path);
        hkaAnimationContainer container = ContainerOf(root, path);
        if (container.m_animations.FirstOrDefault() is not hkaInterleavedUncompressedAnimation animation) return null;

        int tracks = animation.m_numberOfTransformTracks;
        int frames = tracks == 0 ? 0 : animation.m_transforms.Count / tracks;

        return new HkFbx.SampledAnimation
        {
            FrameCount = frames,
            TrackCount = tracks,
            Duration = animation.m_duration,
            FrameDuration = frames > 1 ? animation.m_duration / (frames - 1) : 0f,
            Transforms = [.. animation.m_transforms.Select(FromQs)],
            Floats = [.. animation.m_floats],
            Annotations = Annotations(animation),
            TrackToBone = [.. BindingOf(container, animation)],
        };
    }

    /// <summary>
    /// How many transform tracks a packfile's animation has and which bone each drives, whatever
    /// its compression -- read from the packfile, with nothing decompressed.
    /// </summary>
    public static (IReadOnlyList<short> Binding, int Tracks) Shape(string path)
    {
        var root = (hkRootLevelContainer)Util.ReadHKX(path);
        hkaAnimationContainer container = ContainerOf(root, path);
        hkaAnimation? animation = container.m_animations.FirstOrDefault();

        return animation is null ? ([], 0) : ([.. BindingOf(container, animation)], animation.m_numberOfTransformTracks);
    }

    /// <summary>The skeleton an animation's binding was made for: <c>PairedRoot</c> for a paired one.</summary>
    public static string? SkeletonNameOf(string path)
    {
        var root = (hkRootLevelContainer)Util.ReadHKX(path);
        return ContainerOf(root, path).m_bindings.FirstOrDefault()?.m_originalSkeletonName;
    }

    /// <summary>An animation's duration and its annotation tracks, whatever its compression.</summary>
    public static (float Duration, IReadOnlyList<HkFbx.AnnotationTrack> Annotations) Events(string path)
    {
        var root = (hkRootLevelContainer)Util.ReadHKX(path);
        hkaAnimation animation = ContainerOf(root, path).m_animations.FirstOrDefault()
            ?? throw new InvalidDataException($"'{path}' holds no animation");

        return (animation.m_duration, Annotations(animation));
    }

    private static IReadOnlyList<HkFbx.AnnotationTrack> Annotations(hkaAnimation animation) =>
        [.. animation.m_annotationTracks.Select(t => new HkFbx.AnnotationTrack
        {
            Name = t.m_trackName ?? "",
            Events = [.. t.m_annotations.Select(a => new HkFbx.AnimationEvent(a.m_time, a.m_text ?? ""))],
        })];

    private static IEnumerable<short> BindingOf(hkaAnimationContainer container, hkaAnimation animation) =>
        container.m_bindings.FirstOrDefault(b => ReferenceEquals(b.m_animation, animation))?.m_transformTrackToBoneIndices
        ?? container.m_bindings.FirstOrDefault()?.m_transformTrackToBoneIndices
        ?? [];

    /// <summary>An <c>hkQsTransform</c> as HKX2 holds one: translation, rotation, scale, a row each.</summary>
    private static Matrix4x4 ToQs(HkFbx.BoneTransform t) => new(
        t.Translation.X, t.Translation.Y, t.Translation.Z, 0f,
        t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W,
        t.Scale.X, t.Scale.Y, t.Scale.Z, 0f,
        0f, 0f, 0f, 0f);

    private static HkFbx.BoneTransform FromQs(Matrix4x4 m) => new(
        new Vector3(m.M11, m.M12, m.M13),
        new Quaternion(m.M21, m.M22, m.M23, m.M24),
        new Vector3(m.M31, m.M32, m.M33));

    private static hkaAnimationContainer ContainerOf(hkRootLevelContainer root, string path) =>
        root.m_namedVariants.Select(v => v?.m_variant).OfType<hkaAnimationContainer>().FirstOrDefault()
        ?? throw new InvalidDataException($"'{path}' holds no hkaAnimationContainer");
}
