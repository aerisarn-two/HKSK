using HKSK.Behavior;
using HKSK.Havok;
using HKX2;

namespace HKSK.Assembly;

/// <summary>
/// Writes the packfiles a creature is: a project, a character, and a behaviour graph.
/// </summary>
/// <remarks>
/// <para>
/// The copy route starts from a creature the game has and replaces its parts. This
/// starts from nothing: the caller hands over animations with roles and a skeleton,
/// and what comes out is a project a race can name.
/// </para>
/// <para>
/// The graph is the four layers of <c>docs/behavior-assembly.md</c> §1.1, which are
/// the same four in all 46 of the game's creatures. This builds them in the order the
/// engine reads them: the variables and events it writes and listens for, a root
/// machine holding the creature's whole life, a situation machine saying what it is
/// doing, and inside the default situation the locomotion plan its animations chose.
/// </para>
/// </remarks>
public static class CreatureAssembler
{
    /// <summary>Blend durations, which are a graph convention worth keeping.</summary>
    private const float BlendDefault = 0.2f, BlendFast = 0.1f;

    /// <summary>
    /// Variables the engine writes into a graph, which a graph must declare to receive.
    /// </summary>
    /// <remarks>
    /// Five of these are declared by all 46 creatures -- <c>bAnimationDriven</c>,
    /// <c>iState</c>, <c>IsAttackReady</c>, <c>iSyncIdleLocomotion</c> and
    /// <c>Direction</c> -- and another six by 44 or more. A name the engine writes and
    /// the graph has not declared is a value the graph never sees.
    /// </remarks>
    private static readonly (string Name, VariableType Type, int Initial)[] EngineWrites =
    [
        ("Speed", VariableType.VARIABLE_TYPE_REAL, 0),
        ("Direction", VariableType.VARIABLE_TYPE_REAL, 0),
        ("TurnDelta", VariableType.VARIABLE_TYPE_REAL, 0),
        ("iSyncIdleLocomotion", VariableType.VARIABLE_TYPE_INT32, 0),
        ("iSyncTurnState", VariableType.VARIABLE_TYPE_INT32, 1),
        ("iSyncForwardState", VariableType.VARIABLE_TYPE_INT32, 0),
        ("iGetUpType", VariableType.VARIABLE_TYPE_INT32, 0),
        ("staggerMagnitude", VariableType.VARIABLE_TYPE_REAL, 0),
        ("staggerDirection", VariableType.VARIABLE_TYPE_REAL, 0),
        ("bHeadTracking", VariableType.VARIABLE_TYPE_BOOL, 0),
    ];

    /// <summary>Variables the engine reads back out of a graph.</summary>
    private static readonly (string Name, VariableType Type, int Initial)[] EngineReads =
    [
        ("iState", VariableType.VARIABLE_TYPE_INT32, 0),
        ("SpeedSampled", VariableType.VARIABLE_TYPE_REAL, 0),
        ("bAnimationDriven", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("bAllowRotation", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("IsAttackReady", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("IsStaggering", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("IsRecoiling", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("IsAttacking", VariableType.VARIABLE_TYPE_BOOL, 0),
        ("bEquipOK", VariableType.VARIABLE_TYPE_BOOL, 1),
    ];

    /// <summary>Assembles the creature into <paramref name="outputFolder"/>.</summary>
    public static AssemblyResult Assemble(CreatureSpec spec, string outputFolder)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

        CreaturePlan plan = CreaturePlanner.Of(spec.Animations);
        if (!plan.CanBuild)
            throw new InvalidOperationException(
                $"these animations do not make a creature: {string.Join("; ", plan.Refusals)}");

        var files = new List<string>();
        var notes = new List<string>(plan.Notes);

        string project = spec.Name + "Project.hkx";
        string character = Path.Combine("characters", spec.Name + ".hkx");
        string behaviour = Path.Combine("behaviors", spec.Name + "Behavior.hkx");
        string rig = Path.Combine("character assets", "skeleton.hkx");

        void Write(string relative, Action<string> write)
        {
            string path = Path.Combine(outputFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            write(path);
            files.Add(relative);
        }

        // ---- the skeleton, copied as it is: every animation is bound to it
        Write(rig, to => File.Copy(spec.SkeletonPath, to, overwrite: true));
        string ragdoll = rig;
        if (spec.RagdollPath is { Length: > 0 } source)
        {
            ragdoll = Path.Combine("character assets", spec.Name + "_ragdoll.hkx");
            Write(ragdoll, to => File.Copy(source, to, overwrite: true));
        }
        else notes.Add("no ragdoll was given, so the skeleton answers for both");

        // ---- the animations, in the order given, which is their numbering
        var stored = new List<string>();
        foreach (RoledAnimation animation in spec.Animations)
        {
            string relative = Path.Combine("animations", animation.Stem + ".hkx");
            if (stored.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            Write(relative, to => File.Copy(animation.Path, to, overwrite: true));
            stored.Add(relative);
        }

        notes.Add($"{stored.Count} animations, numbered in the order they were given");

        // ---- the behaviour
        var clips = new List<HKSK.Cache.ClipGeneratorEntry>();
        var built = BuildGraph(spec, plan, stored, notes, clips);
        Write(behaviour, built.Save);

        // ---- the character, which is what says where all of it is
        Write(character, to => Character(spec, stored, rig, ragdoll, behaviour).Save(to));

        // ---- the project, which is what a race names
        Write(project, to => Project(character).Save(to));

        // ---- the row the game reads the creature's animation out of
        HKSK.Cache.AnimationDataProject cache = CacheRow(spec, stored, clips, [project, character, behaviour, rig, ragdoll], notes);

        return new AssemblyResult(Path.Combine(outputFolder, project), plan, files, cache, notes);
    }

    /// <summary>
    /// The creature's row in the animation cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cache restates what the behaviour and the character files already say -- the
    /// files the project is made of, and every clip generator with the position of its
    /// animation in the character's list -- so that the game can read them without
    /// loading a packfile. It also holds the one thing that is nowhere else: where each
    /// animation carries the creature.
    /// </para>
    /// <para>
    /// Havok has a place for root motion in the animation and Skyrim leaves it null on
    /// every clip in the game, so the block written here is the only statement of it.
    /// A clip that should move and has no motion is a clip the engine plays in place,
    /// which is why the speed a caller gives is turned into travel here rather than
    /// hoped for from the animation.
    /// </para>
    /// </remarks>
    private static HKSK.Cache.AnimationDataProject CacheRow(
        CreatureSpec spec,
        IReadOnlyList<string> animations,
        IReadOnlyList<HKSK.Cache.ClipGeneratorEntry> clips,
        IReadOnlyList<string> files,
        List<string> notes)
    {
        var block = new HKSK.Cache.ProjectBlock
        {
            HasFiles = true,
            HasAnimationCache = true,
            Files = [.. files.Select(f => f.Replace('/', '\\'))],
            Clips = [.. clips],
        };

        var movements = new HKSK.Cache.ProjectDataBlock();
        int authored = 0, measured = 0;

        for (int index = 0; index < animations.Count; index++)
        {
            RoledAnimation? animation = spec.Animations.FirstOrDefault(a =>
                string.Equals(Path.GetFileNameWithoutExtension(animations[index]), a.Stem, StringComparison.OrdinalIgnoreCase));
            if (animation is null) continue;

            float? seconds = spec.ClipDurations?.GetValueOrDefault(animation.Stem);
            if (animation.Speed is not { } speed && animation.TurnDegrees is not { } _) continue;
            if (seconds is not { } duration || duration <= 0)
            {
                notes.Add($"'{animation.Stem}' was given a speed and no duration, so it carries the creature nowhere");
                continue;
            }

            HKSK.Cache.ClipMovement motion = animation.TurnDegrees is { } degrees && animation.Speed is null
                ? SyntheticMotion.TurnInPlace(duration, degrees, index)
                : SyntheticMotion.Travel(duration, animation.Speed ?? 0f, Bearing(animation), index);

            if (animation.TurnDegrees is { } both && animation.Speed is not null)
                motion.Rotations = SyntheticMotion.TurnInPlace(duration, both, index).Rotations;

            movements.Movements.Add(motion);
            authored++;
        }

        if (authored > 0) notes.Add($"{authored} animations were given the motion they are to carry, which no animation file holds");
        if (measured > 0) notes.Add($"{measured} animations carried their own motion");

        return new HKSK.Cache.AnimationDataProject
        {
            Name = spec.Name + "Project.txt",
            Block = block,
            Movements = movements,
        };

        static float Bearing(RoledAnimation animation) =>
            animation.Roles.Select(r => r.Heading).FirstOrDefault(h => h != Heading.None) switch
            {
                Heading.Forward => 0f,
                Heading.ForwardRight => 45f,
                Heading.Right => 90f,
                Heading.BackRight => 135f,
                Heading.Back => 180f,
                Heading.BackLeft => -135f,
                Heading.Left => -90f,
                Heading.ForwardLeft => -45f,
                _ => 0f,
            };
    }

    /// <summary>
    /// The project packfile, which holds almost nothing: it names the character file
    /// and leaves every other field empty, as all 49 of the game's do.
    /// </summary>
    private static HavokFile Project(string characterFile) => Wrap("hkbProjectData", new hkbProjectData
    {
        m_worldUpWS = new System.Numerics.Vector4(0, 0, 1, 0),
        m_stringData = new hkbProjectStringData
        {
            m_characterFilenames = [characterFile.Replace('/', '\\')],
            m_animationFilenames = [],
            m_behaviorFilenames = [],
            m_eventNames = [],
            m_animationPath = "",
            m_behaviorPath = "",
            m_characterPath = "",
            m_fullPathToSource = "",
            m_rootPath = "",
        },
        m_defaultEventMode = (sbyte)EventMode.EVENT_MODE_IGNORE_FROM_GENERATOR,
    });

    /// <summary>
    /// The character packfile. Its animation list is the cache's numbering, so it is
    /// written in the order the caller gave and never sorted.
    /// </summary>
    private static HavokFile Character(
        CreatureSpec spec, IReadOnlyList<string> animations, string rig, string ragdoll, string behaviour) =>
        Wrap("hkbCharacterData", new hkbCharacterData
        {
            m_characterPropertyInfos = [],
            m_footIkDriverInfo = null,
            m_handIkDriverInfo = null,
            m_stringData = new hkbCharacterStringData
            {
                m_name = spec.Name,
                m_rigName = rig.Replace('/', '\\'),
                m_ragdollName = ragdoll.Replace('/', '\\'),
                m_behaviorFilename = behaviour.Replace('/', '\\'),
                m_animationNames = [.. animations.Select(a => a.Replace('/', '\\'))],
                m_deformableSkinNames = [],
                m_rigidSkinNames = [],
                m_animationFilenames = [],
                m_mirroredSyncPointSubstringsA = [],
                m_mirroredSyncPointSubstringsB = [],
                m_characterPropertyNames = [],
                m_retargetingSkeletonMapperFilenames = [],
                m_lodNames = [],
            },

            // The same capsule in all 46: the game scales it by the race.
            m_characterControllerInfo = new hkbCharacterDataCharacterControllerInfo
            {
                m_capsuleHeight = 1.7f,
                m_capsuleRadius = 0.4f,
                m_collisionFilterInfo = 1,
                m_characterControllerCinfo = null,
            },
            m_modelUpMS = new System.Numerics.Vector4(0, 0, 1, 0),
            m_modelForwardMS = new System.Numerics.Vector4(1, 0, 0, 0),
            m_modelRightMS = new System.Numerics.Vector4(0, -1, 0, 0),
            m_numBonesPerLod = [],
            m_scale = 1f,
        });

    /// <summary>
    /// The graph: the names the engine speaks through, then the four layers.
    /// </summary>
    private static HavokFile BuildGraph(
        CreatureSpec spec, CreaturePlan plan, IReadOnlyList<string> animations, List<string> notes,
        List<HKSK.Cache.ClipGeneratorEntry> clips)
    {
        var strings = new hkbBehaviorGraphStringData
        {
            m_eventNames = [],
            m_attributeNames = [],
            m_variableNames = [],
            m_characterPropertyNames = [],
        };

        var data = new hkbBehaviorGraphData
        {
            m_attributeDefaults = [],
            m_variableInfos = [],
            m_characterPropertyInfos = [],
            m_eventInfos = [],
            m_wordMinVariableValues = [],
            m_wordMaxVariableValues = [],
            m_variableInitialValues = new hkbVariableValueSet { m_wordVariableValues = [], m_variantVariableValues = [], m_quadVariableValues = [] },
            m_stringData = strings,
        };

        var graph = new hkbBehaviorGraph
        {
            m_name = spec.Name + "Behavior",
            m_variableMode = (sbyte)VariableMode.VARIABLE_MODE_DISCARD_WHEN_INACTIVE,
            m_data = data,
            m_rootGenerator = new hkbManualSelectorGenerator { m_name = "Placeholder" },
        };

        HavokFile file = Wrap("hkbBehaviorGraph", graph);
        var editor = new GraphEditor(file);

        // ---- the names the engine speaks through
        foreach ((string name, VariableType type, int initial) in EngineWrites.Concat(EngineReads))
            editor.Variable(name, type, type == VariableType.VARIABLE_TYPE_REAL
                ? BitConverter.SingleToInt32Bits(initial) : initial);

        editor.RealVariable("blendDefault", BlendDefault);
        editor.RealVariable("blendFast", BlendFast);
        editor.IntVariable($"iState_{spec.MovementTypeName}", 0);

        foreach (string attack in plan.Attacks) editor.Event(attack);
        foreach (string core in new[]
        {
            "moveStart", "moveStop", "turnLeft", "turnRight", "turnStop",
            "attackStop", "staggerStart", "staggerStop", "recoilStart", "recoilStop",
            "DeathAnimation", "Ragdoll", "RagdollInstant", "GetUpStart", "GetUpEnd",
            "IdleStop", "returnToDefault",
        })
            editor.Event(core);

        notes.Add($"{editor.Strings.m_variableNames.Count} variables and {editor.Strings.m_eventNames.Count} events declared");

        // ---- the fourth layer first, because the ones above it hold it
        hkbGenerator locomotion = Locomotion(editor, spec, plan, animations, clips);

        // ---- the situation machine: what the creature is doing
        hkbStateMachine situations = editor.StateMachine(spec.Name + "SituationBehavior");
        var standing = editor.State(situations, "DefaultState", locomotion);
        editor.Graph.m_rootGenerator = Root(editor, spec, plan, situations, standing);

        return file;
    }

    /// <summary>
    /// The root machine, which holds the creature's whole life: what it is doing, and
    /// the end of it.
    /// </summary>
    private static hkbGenerator Root(
        GraphEditor editor, CreatureSpec spec, CreaturePlan plan,
        hkbStateMachine situations, hkbStateMachineStateInfo standing)
    {
        hkbStateMachine root = editor.StateMachine(spec.Name + "RootBehavior");
        var alive = editor.State(root, "AliveState", situations);

        // iState is what the engine reads to choose the movement type, and the graph
        // is what writes it (docs/speed-data.md §4.5).
        alive.m_generator = editor.Modified($"{spec.Name}State_MG",
            editor.Expressions($"{spec.Name}State_EEM", $"iState = iState_{spec.MovementTypeName}"),
            situations);

        return root;
    }

    /// <summary>
    /// The locomotion plan the animations chose: what the creature does while standing
    /// still, and what it does while moving.
    /// </summary>
    private static hkbGenerator Locomotion(
        GraphEditor editor, CreatureSpec spec, CreaturePlan plan,
        IReadOnlyList<string> animations, List<HKSK.Cache.ClipGeneratorEntry> clips)
    {
        // A clip's cache index is the position of its animation in the character's
        // list, and the cache restates every generator by name against it.
        hkbClipGenerator Clip(string name, RoledAnimation animation, ClipMode mode)
        {
            string relative = System.IO.Path.Combine("animations", animation.Stem + ".hkx");
            int index = animations.ToList().FindIndex(a => string.Equals(a, relative, StringComparison.OrdinalIgnoreCase));
            clips.Add(new HKSK.Cache.ClipGeneratorEntry { Name = name, CacheIndex = index });
            return editor.Clip(name, System.IO.Path.Combine("Animations", animation.Stem + ".hkx"), mode);
        }

        var byRole = spec.Animations
            .SelectMany(a => a.Roles.Select(r => (Animation: a, Role: r)))
            .ToList();

        RoledAnimation idle = byRole.First(r => r.Role.Kind == RoleKind.Idle).Animation;
        hkbGenerator standing = Clip("Idle", idle, ClipMode.Looping);

        var forward = byRole
            .Where(r => r.Role.Kind is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint)
            .Where(r => r.Role.Heading == Heading.Forward)
            .ToList();

        // The ladder's floor is the walk at a creep, which is one clip at a low rate,
        // and its rungs are the gaits in the order they carry the creature.
        var rungs = new List<(hkbGenerator Child, float Weight)>();
        foreach (var (animation, role) in forward.OrderBy(r => Rank(r.Role.Kind)))
            rungs.Add((Clip($"{role.Kind}Forward", animation, ClipMode.Looping), Rank(role.Kind)));

        hkbGenerator moving = rungs.Count == 1
            ? rungs[0].Child
            : editor.Blend($"{spec.Name}ForwardBlend", "SpeedSampled", [.. rungs]);

        hkbStateMachine machine = editor.StateMachine(spec.Name + "LocomotionBehavior");
        var still = editor.State(machine, "StandingState", standing);
        var going = editor.State(machine, "MovingState", moving);

        editor.Transition(still, "moveStart", going, editor.Effect("MoveStart", BlendDefault));
        editor.Transition(going, "moveStop", still, editor.Effect("MoveStop", BlendDefault));

        return machine;

        static float Rank(RoleKind kind) => kind switch
        {
            RoleKind.Walk => 1f,
            RoleKind.Trot => 2f,
            RoleKind.Run => 3f,
            RoleKind.Sprint => 4f,
            _ => 0f,
        };
    }

    private static HavokFile Wrap(string className, hkReferencedObject variant) =>
        HavokFile.Of(new hkRootLevelContainer
        {
            m_namedVariants =
            [
                new hkRootLevelContainerNamedVariant { m_name = className, m_className = className, m_variant = (hkReferencedObject)variant },
            ],
        });
}
