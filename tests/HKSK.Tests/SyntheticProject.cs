using System.Numerics;
using HKFBX.Codec;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using HkFbx = HKFBX.Model;

namespace HKSK.Tests;

/// <summary>
/// A complete Havok project built from nothing, on disk, in a temporary folder.
/// </summary>
/// <remarks>
/// Almost everything interesting about this library needs a real project: a
/// character file whose animation list defines the numbering, behaviour graphs
/// defining clips over it, animations to convert, and a cache tying them
/// together. Until now that meant the corpus, which is extracted game data and
/// cannot reach a runner -- so the tests that mattered most were exactly the
/// ones CI never ran.
///
/// This builds the same shape from scratch:
///
/// <code>
///   meshes/animationdatasinglefile.txt
///   meshes/animationsetdatasinglefile.txt
///   meshes/actors/synth/testactor/testactorproject.hkx
///                                /characters/testactor.hkx
///                                /behaviors/testactorbehavior.hkx
///                                /character assets/skeleton.hkx
///                                /animations/*.hkx
/// </code>
///
/// The animations are real spline-compressed Havok data, because they are
/// compressed with Havok's own encoder -- which is the one thing here that
/// cannot be faked, and the reason a fixture needs mopper. Everything else is
/// constructed with HKX2 directly.
///
/// Deliberately small and deliberately awkward: three bones, three animations,
/// four clips, two of which share an animation, and one animation no clip plays.
/// That last one reproduces the chaurus -- a numbered slot with a gap in the
/// clips over it -- which is the case a naive renumbering gets wrong.
/// </remarks>
internal sealed class SyntheticProject : IDisposable
{
    public const string ProjectName = "TestActorProject";
    public const string SkeletonName = "SynthSkeleton";

    /// <summary>The bones every animation drives, root first as Havok requires.</summary>
    public static readonly string[] Bones =
        ["NPC Root [Root]", "NPC Pelvis [Pelv]", "NPC Spine [Spn0]"];

    /// <summary>
    /// The animation list, which is what the cache numbers.
    /// </summary>
    /// <remarks>
    /// Slot 2 is played by no clip, so index 2 is numbered but unused -- the
    /// gap that proves the numbering is positional rather than dense.
    /// </remarks>
    public static readonly string[] Animations =
        [@"Animations\Walk.hkx", @"Animations\Run.hkx", @"Animations\Unused.hkx"];

    private SyntheticProject(string folder)
    {
        Folder = folder;
        Meshes = Path.Combine(folder, "meshes");
        ProjectFolder = Path.Combine(Meshes, "actors", "synth", "testactor");
    }

    public string Folder { get; }
    public string Meshes { get; }
    public string ProjectFolder { get; }

    /// <summary>Opens the project through a freshly read cache.</summary>
    public HavokProject Open() =>
        SkyrimCache.Load(Meshes).Open(ProjectName)
        ?? throw new InvalidOperationException($"the fixture has no '{ProjectName}'");

    /// <summary>Reads the cache without opening a project.</summary>
    public SkyrimCache Cache() => SkyrimCache.Load(Meshes);

    /// <summary>Builds the fixture. Needs mopper: the animations are really compressed.</summary>
    public static SyntheticProject Build()
    {
        var fixture = new SyntheticProject(
            Path.Combine(Path.GetTempPath(), $"hksk-synth-{Guid.NewGuid():N}"));

        Directory.CreateDirectory(Path.Combine(fixture.ProjectFolder, "animations"));
        Directory.CreateDirectory(Path.Combine(fixture.ProjectFolder, "behaviors"));
        Directory.CreateDirectory(Path.Combine(fixture.ProjectFolder, "characters"));
        Directory.CreateDirectory(Path.Combine(fixture.ProjectFolder, "character assets"));

        fixture.WriteSkeleton();
        fixture.WriteAnimations();
        fixture.WriteCharacter();
        fixture.WriteBehavior();
        fixture.WriteProjectFile();
        fixture.WriteCache();

        return fixture;
    }

    // ------------------------------------------------------------ the Havok side

    private void WriteSkeleton()
    {
        var skeleton = new hkaSkeleton
        {
            m_name = SkeletonName,
            m_parentIndices = [-1, 0, 1],
            m_bones = [.. Bones.Select(b => new hkaBone { m_name = b })],
            m_referencePose =
                [.. Enumerable.Range(0, Bones.Length).Select(i => Matrix4x4.CreateTranslation(0, 0, i * 10f))],
        };

        Save(Container(new hkaAnimationContainer { m_skeletons = [skeleton] }),
             Path.Combine(ProjectFolder, "character assets", "skeleton.hkx"));
    }

    private void WriteAnimations()
    {
        var codec = new MopperAnimationCodec();

        // Walk travels and turns; Run travels further; Unused does neither, so a
        // test can tell them apart by their motion alone.
        Write(codec, "Walk.hkx", travel: 40f, turn: MathF.PI / 2f,
              events: [(0.2f, "FootLeft"), (0.5f, "FootRight")]);
        Write(codec, "Run.hkx", travel: 120f, turn: 0f, events: [(0.25f, "FootLeft")]);
        Write(codec, "Unused.hkx", travel: 0f, turn: 0f, events: []);
    }

    private void Write(
        IAnimationCodec codec, string file, float travel, float turn, (float Time, string Text)[] events)
    {
        const int frames = 24;
        const float frameDuration = 1f / 30f;

        var transforms = new HkFbx.BoneTransform[frames * Bones.Length];

        for (int frame = 0; frame < frames; frame++)
        {
            float time = frame * frameDuration;
            float progress = frame / (float)(frames - 1);

            for (int bone = 0; bone < Bones.Length; bone++)
            {
                // The root carries the motion; the others just wave, so the
                // animation is smooth enough for curve fitting to be accurate.
                bool root = bone == 0;

                transforms[frame * Bones.Length + bone] = new HkFbx.BoneTransform(
                    new Vector3(root ? progress * travel : 0f, MathF.Sin(time * MathF.Tau) * 2f, bone * 10f),
                    Quaternion.CreateFromAxisAngle(
                        Vector3.UnitZ, root ? progress * turn : MathF.Sin(time * MathF.Tau) * 0.2f),
                    Vector3.One);
            }
        }

        var sampled = new HkFbx.SampledAnimation
        {
            FrameCount = frames,
            TrackCount = Bones.Length,
            Duration = (frames - 1) * frameDuration,
            FrameDuration = frameDuration,
            Transforms = transforms,
        };

        SplineAnimationData spline = codec.Compress(sampled);

        var animation = new hkaSplineCompressedAnimation
        {
            m_type = (int)AnimationType.HK_SPLINE_COMPRESSED_ANIMATION,
            m_duration = spline.Duration,
            m_numberOfTransformTracks = spline.TransformTrackCount,
            m_numberOfFloatTracks = spline.FloatTrackCount,
            m_annotationTracks =
                [.. Bones.Select(b => new hkaAnnotationTrack { m_trackName = b, m_annotations = [] })],
            m_numFrames = spline.NumFrames,
            m_numBlocks = spline.NumBlocks,
            m_maxFramesPerBlock = spline.MaxFramesPerBlock,
            m_maskAndQuantizationSize = spline.MaskAndQuantizationSize,
            m_blockDuration = spline.BlockDuration,
            m_blockInverseDuration = spline.BlockInverseDuration,
            m_frameDuration = spline.FrameDuration,
            m_blockOffsets = [.. spline.BlockOffsets],
            m_floatBlockOffsets = [.. spline.FloatBlockOffsets],
            m_transformOffsets = [.. spline.TransformOffsets],
            m_floatOffsets = [.. spline.FloatOffsets],
            m_data = [.. spline.Data],
            m_endian = 0,
        };

        // Havok gives an animation one annotation track per transform track and
        // Skyrim puts the events in the first, which is what HKFBX reads.
        animation.m_annotationTracks[0].m_annotations =
            [.. events.Select(e => new hkaAnnotationTrackAnnotation { m_time = e.Time, m_text = e.Text })];

        var binding = new hkaAnimationBinding
        {
            m_originalSkeletonName = SkeletonName,
            m_animation = animation,
            m_transformTrackToBoneIndices = [0, 1, 2],
        };

        Save(Container(new hkaAnimationContainer { m_animations = [animation], m_bindings = [binding] }),
             Path.Combine(ProjectFolder, "animations", file));
    }

    private void WriteCharacter()
    {
        var character = new hkbCharacterData
        {
            m_stringData = new hkbCharacterStringData
            {
                m_name = "TestActor",
                m_rigName = @"Character Assets\skeleton.hkx",
                m_ragdollName = @"Character Assets\skeleton.hkx",
                m_behaviorFilename = @"Behaviors\TestActorBehavior.hkx",
                m_animationNames = [.. Animations],
            },
        };

        Save(Root("hkbCharacterData", character),
             Path.Combine(ProjectFolder, "characters", "testactor.hkx"));
    }

    private void WriteBehavior()
    {
        // Two clips over Walk, so they share its slot and its root motion, and
        // one over Run. Nothing plays Unused.
        hkbClipGenerator[] clips =
        [
            Clip("WalkForward", @"Animations\Walk.hkx", 1f, [(0f, 0, true)]),
            Clip("WalkForwardSlow", @"Animations\Walk.hkx", 0.5f, []),
            Clip("RunForward", @"Animations\Run.hkx", 1.5f, [(-0.01f, 1, true)]),
        ];

        var graph = new hkbBehaviorGraph
        {
            m_name = "TestActorBehavior",
            m_rootGenerator = new hkbManualSelectorGenerator
            {
                m_name = "Root",
                m_generators = [.. clips],
            },
            m_data = new hkbBehaviorGraphData
            {
                m_stringData = new hkbBehaviorGraphStringData
                {
                    m_eventNames = ["clipEnd", "runStop", "FootLeft", "FootRight"],
                },
            },
        };

        Save(Root("hkbBehaviorGraph", graph),
             Path.Combine(ProjectFolder, "behaviors", "testactorbehavior.hkx"));
    }

    private static hkbClipGenerator Clip(
        string name, string animation, float speed, (float Time, int EventId, bool RelativeToEnd)[] triggers) =>
        new()
        {
            m_name = name,
            m_animationName = animation,
            m_playbackSpeed = speed,
            m_animationBindingIndex = -1,
            m_triggers = triggers.Length == 0
                ? null
                : new hkbClipTriggerArray
                {
                    m_triggers =
                    [
                        .. triggers.Select(t => new hkbClipTrigger
                        {
                            m_localTime = t.Time,
                            m_relativeToEndOfClip = t.RelativeToEnd,
                            m_event = new hkbEventProperty { m_id = t.EventId },
                        }),
                    ],
                },
        };

    private void WriteProjectFile() =>
        Save(Root("hkbProjectData", new hkbProjectData
             {
                 m_worldUpWS = new Vector4(0, 0, 1, 0),
                 m_stringData = new hkbProjectStringData
                 {
                     m_characterFilenames = [@"Characters\TestActor.hkx"],
                 },
             }),
             Path.Combine(ProjectFolder, "testactorproject.hkx"));

    // ------------------------------------------------------------ the cache side

    private void WriteCache()
    {
        var block = new ProjectBlock
        {
            HasFiles = true,
            Files =
            [
                @"Behaviors\TestActorBehavior.hkx",
                @"Characters\TestActor.hkx",
                @"Character Assets\skeleton.hkx",
            ],
            HasAnimationCache = true,
            Clips =
            [
                CacheClip("WalkForward", 0, 1f, [new ClipEvent("FootLeft", 0.2f), new ClipEvent("FootRight", 0.5f)]),
                CacheClip("WalkForwardSlow", 0, 0.5f, []),
                CacheClip("RunForward", 1, 1.5f, [new ClipEvent("FootLeft", 0.25f)]),
            ],
        };

        // Motion for the two animations that move. The curve is a displacement
        // that is implicitly zero at t=0, and Skyrim usually stores just the
        // endpoint, so that is what this stores.
        var movements = new ProjectDataBlock
        {
            Movements =
            [
                Motion(0, 0.766667f, new Vector3(40f, 0f, 0f), MathF.PI / 2f),
                Motion(1, 0.766667f, new Vector3(120f, 0f, 0f), 0f),
            ],
        };

        var data = new AnimationDataFile();
        data.Projects.Add(new AnimationDataProject
        {
            Name = $"{ProjectName}.txt",
            Block = block,
            Movements = movements,
        });

        // A creature entry too, so the set data and its checksums are covered.
        var sets = new AnimationSetDataFile();
        var attack = new ProjectAttackBlock
        {
            SwapEvents = ["FootLeft"],
            HandVariables = new HandVariableData
            {
                Variables = [new HandVariable("iRightHandType", 0, 11)],
            },
            Attacks = new ClipAttackBlock
            {
                Attacks = [new AttackData { EventName = "attackStart", Mirrored = 0, Clips = ["RunForward"] }],
            },
        };

        foreach (string animation in Animations)
            attack.Checksums.Add(
                $@"meshes\actors\synth\testactor\animations\{Path.GetFileName(animation.Replace('\\', '/'))}");

        sets.Projects.Add(new AnimationSetDataProject
        {
            Name = $@"{ProjectName}Data\{ProjectName}.txt",
            Sets = new ProjectAttackListBlock { SetFiles = ["FullBody.txt"], Sets = [attack] },
        });

        Directory.CreateDirectory(Meshes);
        data.Save(Path.Combine(Meshes, SkyrimCache.AnimationDataFileName));
        sets.Save(Path.Combine(Meshes, SkyrimCache.AnimationSetDataFileName));
    }

    private static ClipGeneratorEntry CacheClip(string name, int index, float speed, ClipEvent[] events) =>
        new() { Name = name, CacheIndex = index, PlaybackSpeed = speed, Events = [.. events] };

    private static ClipMovement Motion(int index, float duration, Vector3 travel, float turn) => new()
    {
        CacheIndex = index,
        Duration = duration,
        Translations = [new TranslationKey(duration, travel)],
        Rotations = [new RotationKey(duration, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, turn))],
    };

    // ------------------------------------------------------------ plumbing

    private static hkRootLevelContainer Container(hkaAnimationContainer container) =>
        Root("hkaAnimationContainer", container, "Merged Animation Container");

    private static hkRootLevelContainer Root(string className, hkReferencedObject variant, string? name = null) =>
        new()
        {
            m_namedVariants =
            [
                new hkRootLevelContainerNamedVariant
                {
                    m_name = name ?? className,
                    m_className = className,
                    m_variant = variant,
                },
            ],
        };

    private static void Save(hkRootLevelContainer root, string path)
    {
        using FileStream stream = File.Create(path);
        Util.WriteHKX(root, HKXHeader.SkyrimSE(), stream);
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}
