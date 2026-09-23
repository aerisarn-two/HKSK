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

        // ---- and what its movement type has to say, which the clips decide
        HKSK.Speed.MovementType movement = Movement(spec, notes);

        return new AssemblyResult(Path.Combine(outputFolder, project), plan, files, cache, movement, notes);
    }

    /// <summary>
    /// The situations beside standing about: striking, being struck, and dying.
    /// </summary>
    /// <remarks>
    /// Every one of them is the same shape in the game's 46 creatures, and it is the
    /// shape built here: a clip played once, entered by the event the engine sends,
    /// raising the flag that says what the engine may do meanwhile, and left when the
    /// clip itself says it is finished. The clip raises that event at its own end, so a
    /// creature never has to be told to stop attacking -- the animation says so.
    /// </remarks>
    private static void Situations(
        GraphEditor editor, CreatureSpec spec, hkbStateMachine situations, hkbStateMachineStateInfo idle,
        Func<string, RoledAnimation, ClipMode, hkbClipGenerator> Clip, List<string> notes)
    {
        var byRole = spec.Animations.SelectMany(a => a.Roles.Select(r => (Animation: a, Role: r))).ToList();

        hkbStateMachineStateInfo Once(string name, RoledAnimation animation, string enter, string leaves, string? raises)
        {
            hkbClipGenerator clip = Clip(name, animation, ClipMode.SinglePlay);
            editor.AddTrigger(clip, leaves);

            hkbGenerator generator = clip;
            if (raises is { Length: > 0 })
                generator = editor.Modified($"{name}_MG", editor.Expressions($"{name}_EEM", $"{raises} = 1"), clip);

            var state = editor.State(situations, name, generator);
            editor.Wildcard(situations, enter, state, editor.Effect($"To{name}", BlendFast));
            editor.Transition(state, leaves, idle, editor.Effect($"From{name}", BlendDefault));
            return state;
        }

        // ---- striking. Each attack is entered by the name the race's attack data
        // gives it, which is also the name the set data derives the attack from.
        foreach (var (animation, role) in byRole.Where(r => r.Role.Kind is RoleKind.Attack or RoleKind.PowerAttack))
        {
            string attack = role.Name ?? animation.Stem;
            Once($"Attack_{Bare(attack)}", animation, attack, "attackStop", "IsAttacking");
        }

        // ---- being struck. A recoil is a flinch and a stagger is a stumble, and the
        // engine tells them apart by which event it sends.
        foreach (var (animation, role) in byRole.Where(r => r.Role.Kind == RoleKind.Recoil).Take(1))
            Once("Recoil", animation, "recoilStart", "recoilStop", "IsRecoiling");

        foreach (var (animation, role) in byRole.Where(r => r.Role.Kind == RoleKind.Stagger).Take(1))
            Once("Stagger", animation, "staggerStart", "staggerStop", "IsStaggering");

        // ---- getting back up. A knocked-down creature is lying in whatever pose its
        // ragdoll settled in, and the get-up clips each start from a different one, so
        // the choice between them is not made by a variable: a pose matcher compares the
        // ragdoll's pose against each clip's first frame and plays the nearest. All 97
        // of the shipped ones are set alike, and so is this.
        var getUps = byRole.Where(r => r.Role.Kind == RoleKind.GetUp).ToList();
        var reanimates = byRole.Where(r => r.Role.Kind == RoleKind.Reanimate).ToList();

        if (getUps.Count > 0 || reanimates.Count > 0)
        {
            hkbGenerator GetUp(string name, IReadOnlyList<(RoledAnimation Animation, AnimationRole Role)> from)
            {
                var poses = from
                    .Select((r, i) => Clip($"{name}{i + 1}", r.Animation, ClipMode.SinglePlay))
                    .ToList();

                foreach (hkbClipGenerator pose in poses)
                {
                    editor.AddTrigger(pose, "GetUpEnd");
                    editor.AddTrigger(pose, "AddCharacterControllerToWorld");
                }

                return Matching(editor, name, poses, spec);
            }

            // Two ways up, and the engine says which by iGetUpType: a creature that was
            // knocked down gets up, and one that was raised by a spell is reanimated.
            hkbGenerator up = getUps.Count > 0 ? GetUp("GetUp", getUps)
                : GetUp("GetUp", reanimates);
            hkbGenerator back = reanimates.Count > 0 ? GetUp("Reanimate", reanimates) : up;

            var selector = new hkbManualSelectorGenerator
            {
                m_name = "GetUpSelector",
                m_generators = [up, back],
                m_selectedGeneratorIndex = 0,
                m_currentGeneratorIndex = 0,
            };

            editor.Bind(selector, "selectedGeneratorIndex", "iGetUpType");

            var state = editor.State(situations, "GetUpState", selector);
            editor.Wildcard(situations, "GetUpStart", state, editor.Effect("ToGetUp", BlendFast));
            editor.Transition(state, "GetUpEnd", idle, editor.Effect("FromGetUp", BlendDefault));

            notes.Add(getUps.Count > 0
                ? $"getting up, from {getUps.Count} {(getUps.Count == 1 ? "pose" : "poses")} matched against the ragdoll's"
                : "no get-up of its own, so the reanimate answers for both");
        }

        // ---- dying. The clip plays once and hands the creature to its ragdoll, which
        // is what 36 of the game's creatures do; the nine without a death clip go
        // straight to the ragdoll and this state is simply absent.
        foreach (var (animation, role) in byRole.Where(r => r.Role.Kind == RoleKind.Death).Take(1))
        {
            hkbClipGenerator clip = Clip("Death", animation, ClipMode.SinglePlay);
            editor.AddTrigger(clip, "Ragdoll");
            var state = editor.State(situations, "Death", clip);
            editor.Wildcard(situations, "DeathAnimation", state, editor.Effect("ToDeath", BlendFast));
            notes.Add("a death animation, so the creature animates to its ragdoll");
        }

        static string Bare(string attack) =>
            attack.StartsWith("attackStart_", StringComparison.OrdinalIgnoreCase) ? attack["attackStart_".Length..] : attack;
    }

    /// <summary>
    /// A generator that plays whichever of its clips starts nearest the pose the
    /// creature is already in.
    /// </summary>
    /// <remarks>
    /// All 97 of the shipped ones are set the same way, and the settings are not
    /// interesting: what matters is which bones it compares. The root and the pelvis are
    /// bone 0 in every one of them, and the other two are whatever the template it was
    /// copied from happened to name -- a canine's shoulder blades on one creature, an
    /// atronach's fingers on another -- so they are a choice rather than a requirement.
    /// It starts playing on <c>GetUpStart</c> and starts matching on <c>Ragdoll</c>,
    /// which is the event that put the creature on the floor in the first place.
    /// </remarks>
    private static hkbPoseMatchingGenerator Matching(
        GraphEditor editor, string name, IReadOnlyList<hkbClipGenerator> poses, CreatureSpec spec)
    {
        (short other, short another) = spec.PoseMatchBones;

        return new hkbPoseMatchingGenerator
        {
            m_name = name,
            m_children = [.. poses.Select(p => new hkbBlenderGeneratorChild
            {
                m_generator = p,
                m_weight = 1f,
                m_worldFromModelWeight = 1f,
            })],
            m_worldFromModelRotation = System.Numerics.Quaternion.Identity,
            m_blendSpeed = 1f,
            m_minSpeedToSwitch = 0.2f,
            m_minSwitchTimeNoError = 0.2f,
            m_minSwitchTimeFullError = 0f,
            m_startPlayingEventId = editor.Event("GetUpStart"),
            m_startMatchingEventId = editor.Event("Ragdoll"),
            m_rootBoneIndex = 0,
            m_pelvisIndex = 0,
            m_otherBoneIndex = other,
            m_anotherBoneIndex = another,
            m_mode = 0,
            m_flags = 0,
            m_indexOfSyncMasterChild = -1,
        };
    }

    /// <summary>
    /// The movement type the creature's clips describe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The record is authored from the clips and not the other way round
    /// (<c>docs/speed-data.md</c> §7.2): a heading's walk speed is that heading's walk
    /// clip's delivered speed, and its run speed the run clip's. The falmer states a
    /// forward walk of 100.44 and a forward run of 175.77, and the rungs of the ladder
    /// its records are sampled from sit at 5, 100.442 and 175.774 -- the record is the
    /// rungs, written down.
    /// </para>
    /// <para>
    /// A heading with no clip of its own takes the forward speed, and a gait with no
    /// clip takes the other gait's, which is what the 88 shipped records that walk and
    /// run at one speed look like. What the clips cannot give is the record's three
    /// rotation rates, which are gameplay constants by family, and its anim-change
    /// thresholds, which are FLT_MAX on 88 of the 106 shipped records.
    /// </para>
    /// </remarks>
    private static HKSK.Speed.MovementType Movement(CreatureSpec spec, List<string> notes)
    {
        float Delivered(Heading heading, params RoleKind[] gaits)
        {
            foreach (RoleKind gait in gaits)
                foreach (RoledAnimation animation in spec.Animations)
                    if (animation.Speed is { } speed && speed > 0
                        && animation.Roles.Any(r => r.Kind == gait && r.Heading == heading))
                        return speed;

            return 0f;
        }

        float forwardWalk = Delivered(Heading.Forward, RoleKind.Walk, RoleKind.Trot);
        float forwardRun = Delivered(Heading.Forward, RoleKind.Run, RoleKind.Sprint);
        if (forwardRun <= 0) forwardRun = forwardWalk;
        if (forwardWalk <= 0) forwardWalk = forwardRun;

        // A heading with a clip of its own is that clip. A heading that walks and cannot
        // run runs at the speed it walks, which is what the 88 shipped records that walk
        // and run at one speed are. A heading with nothing at all takes the forward one.
        float Either(Heading heading, bool run)
        {
            float walk = Delivered(heading, RoleKind.Walk, RoleKind.Trot);
            float ran = Delivered(heading, RoleKind.Run, RoleKind.Sprint);

            return run
                ? ran > 0 ? ran : walk > 0 ? walk : forwardRun
                : walk > 0 ? walk : ran > 0 ? ran : forwardWalk;
        }

        var type = new HKSK.Speed.MovementType(
            spec.MovementTypeName,
            forwardWalk, forwardRun,
            Either(Heading.Back, false), Either(Heading.Back, true),
            Either(Heading.Left, false), Either(Heading.Left, true),
            Either(Heading.Right, false), Either(Heading.Right, true));

        notes.Add(forwardWalk > 0
            ? $"the movement type the clips describe: {type}"
            : "no clip says how fast it carries the creature, so the movement type is all zeros");

        return type;
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

        // A clip's cache index is the position of its animation in the character's list,
        // and the cache restates every generator by name against it.
        hkbClipGenerator Clip(string name, RoledAnimation animation, ClipMode mode)
        {
            string relative = System.IO.Path.Combine("animations", animation.Stem + ".hkx");
            int index = animations.ToList().FindIndex(a => string.Equals(a, relative, StringComparison.OrdinalIgnoreCase));
            clips.Add(new HKSK.Cache.ClipGeneratorEntry { Name = name, CacheIndex = index });
            return editor.Clip(name, System.IO.Path.Combine("Animations", animation.Stem + ".hkx"), mode);
        }

        // ---- the fourth layer first, because the ones above it hold it
        hkbGenerator locomotion = Locomotion(editor, spec, plan, Clip);

        // ---- the situation machine: what the creature is doing
        hkbStateMachine situations = editor.StateMachine(spec.Name + "SituationBehavior");
        var idling = editor.State(situations, "DefaultState", locomotion);
        Situations(editor, spec, situations, idling, Clip, notes);
        editor.Graph.m_rootGenerator = Root(editor, spec, situations);

        return file;
    }

    /// <summary>
    /// The root machine, which holds the creature's whole life: what it is doing, and
    /// the end of it.
    /// </summary>
    private static hkbGenerator Root(GraphEditor editor, CreatureSpec spec, hkbStateMachine situations)
    {
        hkbStateMachine root = editor.StateMachine(spec.Name + "RootBehavior");
        var alive = editor.State(root, "AliveState", situations);

        // Everything the creature does runs under this list, which is where the speed
        // sampler sits, and where iState is written -- the engine reads it back to
        // choose the movement type (docs/speed-data.md §4.5).
        alive.m_generator = editor.Modified($"{spec.Name}Root_MG",
            editor.ModifierList($"{spec.Name}Root_ML",
                SpeedSampler(editor),
                editor.Expressions($"{spec.Name}State_EEM", $"iState = iState_{spec.MovementTypeName}")),
            situations);

        return root;
    }

    /// <summary>
    /// The modifier that turns the speed the engine asks for into the speed the ladders
    /// are read at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A speed ladder blends its rungs on a variable, and nothing writes that variable
    /// but this. The engine writes <c>Speed</c>, the speed it wants; the sampler reads
    /// the creature's movement type through <c>iState</c>, looks the request up in the
    /// speed table, and writes back what the animations will actually deliver. A graph
    /// whose ladders read a variable nobody writes never leaves its first rung, which is
    /// a creature that slides along at a standstill.
    /// </para>
    /// <para>
    /// All four bindings are the same in every creature that has one, 38 of the 46, and
    /// so are the stored values: <c>state</c> is -1 in all 44 of them, because a field a
    /// node also binds is a slot rather than a value.
    /// </para>
    /// </remarks>
    private static BSSpeedSamplerModifier SpeedSampler(GraphEditor editor)
    {
        var sampler = new BSSpeedSamplerModifier
        {
            m_name = "BSSpeedSamplerModifier",
            m_enable = true,
            m_state = -1,
            m_direction = 0f,
            m_goalSpeed = 0f,
            m_speedOut = 0f,
        };

        editor.Bind(sampler, "state", "iState");
        editor.Bind(sampler, "direction", "Direction");
        editor.Bind(sampler, "goalSpeed", "Speed");
        editor.Bind(sampler, "speedOut", "SpeedSampled");

        return sampler;
    }

    /// <summary>
    /// The locomotion plan the animations chose: what the creature does while standing
    /// still, and what it does while moving.
    /// </summary>
    private static hkbGenerator Locomotion(
        GraphEditor editor, CreatureSpec spec, CreaturePlan plan,
        Func<string, RoledAnimation, ClipMode, hkbClipGenerator> Clip)
    {

        var byRole = spec.Animations
            .SelectMany(a => a.Roles.Select(r => (Animation: a, Role: r)))
            .ToList();

        RoledAnimation idle = byRole.First(r => r.Role.Kind == RoleKind.Idle).Animation;
        hkbGenerator standing = Clip("Idle", idle, ClipMode.Looping);

        var gaits = byRole
            .Where(r => r.Role.Kind is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint)
            .ToList();

        // One ladder per heading, then a compass over the ladders, which is how the
        // game's bipeds are built: the draugr's eight arms each hold a three-rung ladder
        // of a creep, a walk and a run.
        var headings = gaits.Select(g => g.Role.Heading).Where(h => h != Heading.None).Distinct()
            .OrderBy(Around).ToList();

        var arms = new List<(hkbGenerator Child, float Weight)>();
        foreach (Heading heading in headings)
        {
            var rungs = gaits.Where(g => g.Role.Heading == heading)
                .OrderBy(g => Rank(g.Role.Kind))
                .Select(g => ((hkbGenerator)Clip($"{g.Role.Kind}{heading}", g.Animation, ClipMode.Looping), Rank(g.Role.Kind)))
                .ToList();

            arms.Add((rungs.Count == 1
                ? rungs[0].Item1
                : editor.Blend($"{spec.Name}{heading}Blend", "SpeedSampled", [.. rungs]), Around(heading)));
        }

        hkbGenerator moving = arms.Count == 1
            ? arms[0].Child
            : editor.Blend($"{spec.Name}DirectionBlend", "Direction", [.. arms]);

        hkbStateMachine machine = editor.StateMachine(spec.Name + "LocomotionBehavior");
        var still = editor.State(machine, "StandingState", standing);
        var going = editor.State(machine, "MovingState", moving);

        editor.Transition(still, "moveStart", going, editor.Effect("MoveStart", BlendDefault));
        editor.Transition(going, "moveStop", still, editor.Effect("MoveStop", BlendDefault));

        return machine;

        // Where a heading sits on the compass the engine writes, which runs from nothing
        // at straight ahead once round to one. The draugr's eight arms are at 0, 0.1,
        // 0.2, 0.4, 0.5, 0.6, 0.8 and 0.9, so the quarters are the tenths they surround.
        static float Around(Heading heading) => heading switch
        {
            Heading.Forward => 0f,
            Heading.ForwardRight => 0.1f,
            Heading.Right => 0.2f,
            Heading.BackRight => 0.4f,
            Heading.Back => 0.5f,
            Heading.BackLeft => 0.6f,
            Heading.Left => 0.8f,
            Heading.ForwardLeft => 0.9f,
            _ => 0f,
        };

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
