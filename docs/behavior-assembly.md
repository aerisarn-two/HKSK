# Assembling a behaviour from animations, a skeleton and a ragdoll

    Status:   DESIGN -- the facts are measured (the documents cited); the API is
              proposed and not yet built
    Inputs:   docs/creature-patterns-research.md (what every creature is made of),
              docs/animation-variables.md and docs/animation-events.md (the engine
              contract), docs/speed-data.md, docs/animation-set-data.md,
              docs/animation-data.md (the caches), docs/paired-animations.md
              (kill-moves and mounts), docs/flight.md and docs/swimming.md (the two
              plans this design does not cover)
    Consumer: another component hands over animations with roles, a skeleton and a
              ragdoll, and receives a project the game can load

## 0. The task

Given a set of animations that already exist -- imported from FBX through
`AnimationExchange`, or Havok files from elsewhere -- a skeleton and a ragdoll,
produce everything a Skyrim creature needs: the project packfile, the character
file, a behaviour graph, and the creature's rows in the three cache files. The
caller states what each animation *is* and does not touch a Havok node. This
document writes down what is known, so the API can be built against facts rather
than against the shipped files' idiosyncrasies, and proposes the API.

## 1. What is known, in the order it is used

### 1.1 A creature is a four-layer object, and the layers are fixed

Measured over the 46 creatures (`docs/creature-patterns-research.md`):

1. **A root machine** with one live state and the ragdoll shell: animate-to-ragdoll
   (36 creatures; plays the death clip, then `Ragdoll`), fully-ragdoll (45; powered
   ragdoll under two event-driven modifiers and a timer), get-up (44; `hkbGetUpModifier`,
   a selector on `iGetUpType` between a reanimate and a get-up
   `hkbPoseMatchingGenerator` of one to three clips, all 97 of them set alike:
   root and pelvis bone 0, any two other bones, `GetUpStart` to play, `Ragdoll` to
   match). Wildcards `Ragdoll` (45), `RagdollInstant` (37),
   `DeathAnimation` (34), `staggerStart` (25). The shell's node lists are identical
   across projects and are written once.
2. **A root modifier list**: keyframe bones and ragdoll drive (34 at the root, 45 in
   the project), the speed sampler bound `state<-iState, direction<-Direction,
   goalSpeed<-Speed, speedOut<-SpeedSampled` (38), look-at (32), get-up (44), and the
   draw/sheathe expressions (`weaponDraw if (iCombatStance == 1)` and siblings).
3. **A situation machine** whose states are what the creature does: default (46),
   stagger (44), recoil (39), attacking (42), combat stance (41), equip transitions
   (21), and the modules -- paired kill-move (23), aggro warning (24), bleed-out
   (11), animated death (10), swim (12), sprint (4), a place to wait in (29),
   entrance (10), furniture (7), casting (18), bash (14), block (6). Every situation
   state is one shape: a nested machine with one state per animation, a
   `BSIsActiveModifier` saying what the engine may do meanwhile, the clip played
   once, exit on the clip's own `*Stop` event which the state also raises on exit.
4. **A locomotion plan** inside the default situation, one of five:

   | plan | creatures | moving generator | standing |
   | --- | ---: | --- | --- |
   | A compass biped | 14 | `BSCyclicBlendTransitionGenerator` over an 8-arm cyclic parametric blend, each arm a speed ladder | idle + `TurnLeft`/`TurnRight` blends on `TurnDelta` |
   | B four-arm compass | 12 | the same over 4 arms | idle + looping turn clips |
   | C quadruped | 13 | a speed ladder of left/centre/right tri-blends on `TurnDeltaDamped`; backward a separate state | idle + looping turn clips |
   | D forward only | 4 | one ladder or one clip | idle + looping turn clip |
   | E swimmer / flyer | 3 | `docs/swimming.md`, `docs/flight.md` | -- |

   plus canned turns (27) beside standing and locomotion, and a combat stance (33)
   doubling the standing and moving parts under a readied state.

### 1.2 The engine contract is small and named

- **Variables the engine writes** and a graph must declare to receive: `Speed`,
  `Direction`, `TurnDelta`, `iSyncIdleLocomotion`, `iSyncTurnState`,
  `iSyncForwardState`, `iSyncStrafeState`, `iSyncSprintState`, `iGetUpType`,
  `staggerMagnitude`, `staggerDirection`, `TargetLocation`, `bHeadTracking`, and the
  set-up constants (`docs/animation-variables.md` §2). **Variables the engine reads**:
  `iState`, `SpeedSampled`, `bAnimationDriven`, `bAllowRotation`, `IsAttackReady`,
  `IsStaggering`, `IsRecoiling`, `IsAttacking`, `IsBashing`, `IsBleedingOut`,
  `bEquipOK`, `bForceIdleStop` (§3). Five are declared by all 46 creatures
  (`bAnimationDriven`, `iState`, `IsAttackReady`, `iSyncIdleLocomotion`, `Direction`),
  another six by 44 or more; 385 of the 495 names in the corpus are private.
- **Events the engine sends** are almost all the idle records' events, not literals:
  `moveStart`, `moveStop`, `turnLeft`, `turnRight`, `turnStop`, `staggerStart`,
  `recoilStart`, `bleedOutStart`, `DeathAnimation`, `IdleStop`, the `cannedTurn*`
  family, `weaponDraw` / `weaponSheathe`, and `attackStart_<name>` from the race's
  attack data. The executable holds none of the movement names as strings
  (`docs/reverse-engineering.md` §4). **Events the engine listens for** are the 93
  handlers behind `actorresponse.txt` (`docs/animation-events.md`): `HitFrame`,
  `preHitFrame`, `weaponSwing`, `attackStop`, `staggerStop`, `recoilStop`,
  `GetUpStart`/`End`, `AddCharacterControllerToWorld`, `Ragdoll`, `KillActor`...
- **Names match by spelling, not by case.** A name sent to a graph is interned
  through the case-insensitive string pool and matched by pointer
  (`docs/animation-events.md` §1). `IdleStop` and `idleStop` are one event.
- **Blend durations** are a graph convention worth keeping: `blendDefault` 0.2,
  `blendFast` 0.1, `blendSlow` 0.5.

### 1.3 What is not in the graph and has to be supplied

- **The ladder rungs**: the movement type's walk and run speed per heading
  (`MovementType`, eight numbers, `docs/speed-data.md`). The floor rung is the walk
  clip at 5.
- **The turn authority per gait** on the quadruped plan: walk ±75, trot ±112.5, run
  ±270 on the bear and boar; the horse ±135 throughout.
- **The gait split thresholds** where a creature has two compasses (`runStart if
  (SpeedSampled > 100); walkStart if (SpeedSampled < 130)` on the falmer).
- **The `iState` ids**: one per movement type, declared as `iState_<MNAM>` in the
  root graph, written by an expression in the default and readied states (or a
  tagging generator); the speed table is keyed on them (`docs/speed-data.md` §4.5).
- **The attack events**: the race record's attack data names them; the graph's
  states are entered by the same names, and the set data derives the attacks from
  the states (`docs/animation-set-data.md` §5.6).

### 1.4 The caches follow from the graph and the character file

- **`animationdatasinglefile.txt`**: every clip with its animation's cache index --
  which is the animation's position in the character file's list (`README.md`) --
  its speed, crops, and the events derived from the animation's annotations and
  the clip's triggers (`docs/animation-data.md`); root motion per animation.
- **`animationsetdatasinglefile.txt`**: for a creature that never chooses by hand
  type, one set listing the character file's animations and the attacks
  (`docs/animation-set-data.md` §3.1); `SetDataGenerator` writes it from the graph
  and `GameEvents`.
- **`speeddatasinglefile.txt`**: a block per project, one entry per `iState` key,
  swept from the ladders and the movement type; `SpeedDataGenerator` writes it.
  Every shipped creature has a block, the eight without a sampler included.

### 1.5 Paired animations, if the creature is to be kill-moved or mounted

The victim half is one state per shared animation: a `BSSynchronizedClipGenerator`
with prefix `2_`, `lead = true`, `reorient = true`, the clip's triggers all
`2_`-prefixed (`2_KillMoveStart`, `2_DeathEmote`, `2_KillActor`, `2_KillMoveEnd`,
`2_PairEnd`), entered by the kill-move's name without `pa_`
(`docs/creature-patterns-research.md` §3.4). The engine derives the partner's name by
toggling the prefix and starts the pair only if each graph declares its form; the
side an idle is played on is the action's source actor -- the killer for kill-moves,
the mount for mounts (`docs/paired-animations.md`).

### 1.6 Water: a plan for the fish, a module for everyone else

Twelve creatures swim, in two shapes (`docs/swimming.md` for the engine's side):

- **the slaughterfish is never out of water**, so its default situation *is* a
  swim: standing is an idle-swim with looping turn clips, moving is a **6-arm
  parametric blend bound to `Direction` directly** (flags 48, no cyclic wrapper,
  arms at F, FR45, BR45, B, BL45, FL45) whose arms are ladders on `Speed`; canned
  swim turns beside it; the combat stance swaps the idle. That is plan E;
- **everyone else has a swim state** beside the default situation, entered by
  `swimStart` and left by `swimStop`, holding a `BSIsActiveModifier` that raises
  `isSwimming` (or `bInSwimState`) and drops `bFootIKEnable`, setting `iState` to
  the swim movement type (an expression on the horker, a tag on the horse), and
  inside it one of three: a single looped swim-forward clip (bear, falmer, horse,
  the minimum), a 4-arm swim compass with a tread-water idle (werewolf), or a whole
  water sub-plan with idle, loop turns, canned turns, locomotion and a swim attack
  (horker). The module's floor is one clip.

## 2. Inputs

### 2.1 Animations, each with a role

The shipped names are inconsistent (`MTForward`, `WalkForward`, `Forward_Walk`,
`RunF`, `forwardWalk`) and the graph binds by path while the cache binds by list
position, so the API takes **roles**, not names. A role is a tuple:

    Kind      Idle | Walk | Run | Trot | Sprint | TurnInPlace | CannedTurn |
              CombatIdle | Equip | Unequip | Attack | PowerAttack | Recoil |
              Stagger | Death | GetUp | Reanimate | AggroWarning | BleedOutEnter |
              BleedOutLoop | BleedOutExit | Bash | Block | BlockHit | Swim |
              SwimTurn | IdleVariant | Feed | LayDown | LayLoop | LayUp |
              KillMoveVictim | Custom
    Heading   Forward | ForwardRight | Right | BackRight | Back | BackLeft |
              Left | ForwardLeft | None          (walk, run, trot, sprint, swim)
    Side      Left | Right | None                 (turns, side attacks, recoils)
    Angle     90 | 180 | None                     (canned turns)
    Weight    Small | Medium | Large | None       (staggers, recoils)
    Stance    Default | Combat                    (idles, locomotion, turns)
    Name      the attack's event name for attacks; the kill-move's name for
              victims; free for custom
    Mirror    true when the animation is to be used mirrored for the other side

One animation may carry several roles (the chicken's `WalkForward` is both the walk
and the floor of its ladder; the quadruped's straight clip fills all three turn
slots when no left and right exist), and a role may be filled by the same file as
another role: the API reuses, it never copies.

### 2.2 The skeleton and the ragdoll

- the skeleton packfile (`hkaSkeleton` in an `hkaAnimationContainer`), the file
  every animation was authored against and the one `AnimationExchange.Import` rigs
  to; its path is the character file's `rigName`;
- the ragdoll packfile (`hkaRagdollInstance`, its `hkaSkeleton`, and the two
  `hkaSkeletonMapper`s between rig and ragdoll); its path is `ragdollName`. The
  shell's ragdoll modifiers, the keyframe-bones modifier and the get-up modifier
  need nothing more than that the ragdoll loads;
- **bones, by name**, for the three modules that name bones. Measured over the
  46 (`tests/HKSK.Tests/ZzNotCovered.cs`):

  | module | what it names | shipped values |
  | --- | --- | --- |
  | get-up pose matcher (all 46) | root and pelvis: bone 0; `otherBone`, `anotherBone`: any two -- the shipped ones are template leftovers at indices 10 and 11 (a canine's shoulder blades, an atronach's fingers) | `blendSpeed` 1, `minSpeedToSwitch` 0.2, `minSwitchTimeNoError` 0.2, `minSwitchTimeFullError` 0, `mode` 0 |
  | look-at (32) | the spine-to-head chain, three to five bones, each with a limit | `limitAngleDegrees` 60–65 (45 on the sabre cat, 90 on the canines), threshold 0 or 45, `onGain` 0.05–0.075, `offGain` 0.05, per-bone limit 180, no eye bones |
  | foot IK (15: two legs on the frost atronach, benthic lurker, giant, riekling, falmer; four on bear, wolf, cow, deer, horse, mammoth, sabre cat, skeever, chaurus) | per leg: hip, knee, ankle bone; `kneeAxisLS` ±X or ±Z; `footPlantedAnkleHeightMS`, `footRaisedAnkleHeightMS`, `min`/`maxAnkleHeightMS`, knee angle range | the driver lives in **`hkbCharacterData.m_footIkDriverInfo`**, not the graph; `isQuadrupedNarrow` on the four-legged; raycast 32–192 up and down; the controls modifier at the root carries the gains (`onOff` 0.2, the rest 1, feedback and align 0) |

  The ankle heights are the creature's rest pose -- the ankle's height above the
  ground when planted and when raised -- so they are computable from the
  skeleton's reference pose rather than asked for. Everything else is a bone
  name plus the shipped constant;
- **the character controller** is the same in all 46: capsule height 1.7, radius
  0.4, filter 1 (the game scales it by the race). `modelForwardMS` is +X on 31
  creatures and +Y on 15 with no relation to plan or rig -- the male and female
  humanoid differ on the same skeleton -- so the engine does not depend on it;
  +X is written.

### 2.3 The game side

What the generators already take as `IGameRecords` (`HKSK.Records`):

- the movement types the race uses, by role (walk, run, swim, sprint), each with
  its eight speeds -- or, for a creature not yet in a plugin, the eight numbers
  directly, from which the API writes `iState_<name>` constants and the caller adds
  the `MOVT` afterwards;
- the race's attack events, or the list the caller intends to put in the race; the
  graph's attack states are entered by exactly these names;
- the idle events the creature will be sent beyond the core (feeding, laying,
  sleeping, the custom idles) -- each becomes a state with an `idle*Start` entry
  and `IdleStop` exit.

## 3. From roles to a plan

The plan is chosen, not asked for, and the choice is a fixed reading of the roles:

| roles present | plan |
| --- | --- |
| walk (and run) at all eight headings | A, compass biped |
| walk at F, R, B, L only | B, four-arm |
| forward walk with left and right variants, or a trot | C, quadruped |
| forward walk only | D, forward only |
| swim at any heading, no walk | E, swimmer (`docs/swimming.md` §3) |

Within the plan:

- a walk with no run gives a two-rung ladder (walk at 5, walk at the walk speed);
  a run adds the third rung; a trot or sprint adds a gait state beside it;
- turn-in-place with one clip per side gives the blend form (weights 5, 90, 135);
  one clip with `Mirror` gives the loop form with a `[Mirrored]` clip;
- canned turns at 90 and 180 per side, or with mirrors, give the canned-turn state
  and its `cannedTurn*` transitions; without them the state is omitted and the
  engine's `cannedTurn*` events fall on the floor, which is what the witchlight
  does;
- a combat idle, and optionally a combat walk and run, give the combat stance --
  the stance is the standing and moving parts repeated under a readied state,
  entered by `weapEquip` / `combatStanceStart`; equip and unequip clips give the
  transition states, otherwise the stance is entered directly;
- each attack gives a state in the attacking machine, entered by
  `attackStart_<Name>` (or `attackPowerStart_<Name>`), with `bAllowRotation`
  raised, exit on `attackStop`, `recoilStart` to the recoil state;
- staggers give a parametric blend on `staggerMagnitude`: two clips at 0 and 1,
  three at 0, 0.5, 1; a `Side` stagger gives the directional variant;
- a death clip gives `AnimateToRagdoll`; none gives `Ragdoll` straight into the
  ragdoll, as nine creatures do;
- get-up clips give the get-up blend (one to three), a reanimate clip the
  reanimate blend, else the get-up blend serves both;
- `KillMoveVictim` roles give the paired states of §1.5, one per animation, named
  by the kill-move name.

The floor is the witchlight: idle, walk, attack, recoil, staggers -- seven
animations. Below it the API refuses: an idle and a forward walk are the minimum.

## 4. What is built

### 4.1 Files

    <project>/<name>project.hkx                    hkbProjectData naming the character file
    <project>/characters/<name>.hkx                hkbCharacterData: name, rigName, ragdollName,
                                                   behaviorFilename, animationNames in slot order
    <project>/behaviors/<name>behavior.hkx         one hkbBehaviorGraph
    <project>/character assets/skeleton.hkx        the skeleton, copied
    <project>/character assets/<name>_ragdoll.hkx  the ragdoll, copied
    <project>/animations/*.hkx                     the animations, copied or imported

The character file's animation list is written **in the order the roles were
given, appended never inserted**, because that order is the cache index of every
clip (`README.md`). No character properties are declared: only the shared quadruped
file needs them.

### 4.2 The graph

One file, one `hkbBehaviorGraph`. Declared, in this order: the engine-written variables of §1.2 with the shipped
initial values (`bEquipOK` 1, `iSyncTurnState` 1, `IsAttackReady` 1 for a
combatant, else 0), the engine-read variables, `blendDefault` / `blendFast` /
`blendSlow`, the `iState_<name>` constants, the sync variable of the base machine
(`iSyncDefaultState`, initial 0), and the private intermediates the plan needs
(`TurnDeltaDamped`, `walkBackSpeedMult`, `turnSpeedMult`). Events: the core of
`docs/creature-patterns-research.md` §2.3 plus one per module and per attack.

Then the four layers of §1.1, from templates that are the shipped nodes with the
clips substituted:

- the root machine and the shell, verbatim from the chicken (no death) or the dog
  (with death), the get-up selector on `iGetUpType` over two pose matchers set
  as §2.2 says, with the get-up clips as their children;
- the root modifier list: keyframe bones, ragdoll drive, the speed sampler with
  its four bindings, look-at over the given chain with the shipped gains if a
  chain is given, the foot-IK controls modifier with the shipped gains if legs
  are given (and the driver written into the character file), get-up, the
  draw/sheathe expressions if there is a stance;
- the situation machine with `DefaultState` first and the modules after, wildcards
  `staggerStart`, `recoilStart`, `recoilLargeStart`, `bleedOutStart`,
  `aggroWarningStart`, `returnToDefault` as the modules require;
- the plan of §3 under `DefaultState`: standing machine, locomotion generator,
  canned turns; the stance repeats the first two under `CombatReadyState`;
- `iState` written by an expression modifier in the modifier list of the default
  state and of the readied state (`iState = iState_<Name>Default`, `iState =
  iState_<Name>Combat`); with a gait split, `iState = cond(iStateCurrent == 0,
  iState_<Name>Walk, iState_<Name>Run)` beside the `runStart` / `walkStart`
  expression.

Clip generators are named by role in the shipped style (`WalkForward`,
`RunForwardRight`, `TurnLeft90`, `Attack_<Name>`, `StaggerSmall`, `GetUpLeft`),
`mode` loop for idles and locomotion, once for everything else, `playbackSpeed` 1
unless the role says otherwise. Triggers by role:

| role | triggers written |
| --- | --- |
| attack | `attackStop` at the end; `preHitFrame`, `HitFrame`, `weaponSwing` at the times the caller gives (or from the animation's annotations, if present) |
| recoil, stagger, bash, block-hit | `recoilStop` / `staggerStop` / `bashStop` / `blockHitStop` at the end |
| equip, unequip | `weapEquipOut` / `weapUnequipOut` at the end |
| death | `Ragdoll` at the end |
| get-up, reanimate | `GetUpEnd`, `AddCharacterControllerToWorld` at the end; `Reanimated` on the reanimate clip |
| canned turn | `cannedTurnStop` (or `TurnInPlaceEnd`) at the end |
| idle variant | `IdleStop` at the end |
| kill-move victim | the `2_` set of §1.5 at the animation's own times |
| locomotion, idle loop | none (`FootLeft` / `FootRight` from the annotations if present) |

### 4.3 The cache rows

Through the library's existing edit surface: a project block added to the
animation data, then `SkyrimCache.PromoteToActor` with the character file (it
takes the prop the block makes and creates the set-data entry with it),
`AddAnimation` per slot in order,
`AddClip` per generator, `SetRootMotion` from the animation's root track
(`AnimationExchange.Import` already derives it), then `CacheGeneration.Amend` for
the project, which runs the set-data and speed generators with the records.

## 5. The API

Namespace `HKSK.Assembly`. Everything is pure until `Assemble`, and `Plan` is the
thing a front end shows the user before anything is written.

```csharp
public enum RoleKind { Idle, Walk, Run, Trot, Sprint, TurnInPlace, CannedTurn, CombatIdle,
    Equip, Unequip, Attack, PowerAttack, Recoil, Stagger, Death, GetUp, Reanimate,
    AggroWarning, BleedOutEnter, BleedOutLoop, BleedOutExit, Bash, Block, BlockHit,
    Swim, SwimTurn, IdleVariant, Feed, LayDown, LayLoop, LayUp, KillMoveVictim, Custom }

public enum Heading { None, Forward, ForwardRight, Right, BackRight, Back, BackLeft, Left, ForwardLeft }
public enum Side { None, Left, Right }
public enum Stance { Default, Combat }
public enum Magnitude { None, Small, Medium, Large }

/// One thing an animation is. An animation may be given several.
public sealed record AnimationRole(
    RoleKind Kind,
    Heading Heading = Heading.None,
    Side Side = Side.None,
    int Angle = 0,
    Magnitude Magnitude = Magnitude.None,
    Stance Stance = Stance.Default,
    string? Name = null,          // attack event, kill-move name, custom idle event
    bool Mirror = false);

/// An animation and what it is. Path is a Havok file on disk, or an FBX to import.
public sealed record RoledAnimation(string Path, IReadOnlyList<AnimationRole> Roles,
    IReadOnlyList<ClipEvent>? Events = null);   // HitFrame and friends, in seconds

public sealed record CreatureSpec
{
    public required string Name { get; init; }              // project stem, e.g. "MyBeast"
    public required string SkeletonPath { get; init; }
    public required string RagdollPath { get; init; }
    public required IReadOnlyList<RoledAnimation> Animations { get; init; }
    public SkeletonRoles Bones { get; init; } = new();
    public IReadOnlyDictionary<MovementRole, MovementType>? Movements { get; init; }
    public IReadOnlyList<string> AttackEvents { get; init; } = [];   // the race's, or intended
    public IGameRecords? Records { get; init; }             // used where the two above are null
    public AssemblyConventions Conventions { get; init; } = AssemblyConventions.Shipped;
}

/// The bones the modules name, by name. Every member is optional; an absent one
/// leaves its module out. Ankle heights are read from the skeleton's rest pose.
public sealed record SkeletonRoles
{
    public (string Other, string Another)? PoseMatchBones { get; init; }   // default: two hip-side bones
    public IReadOnlyList<string> LookAtChain { get; init; } = [];           // spine .. head; empty = no look-at
    public IReadOnlyList<Leg> Legs { get; init; } = [];                     // empty = no foot IK
}

public sealed record Leg(string Hip, string Knee, string Ankle, Vector3 KneeAxis);

/// The numbers the graph does not contain, with the shipped defaults.
public sealed record AssemblyConventions
{
    public static AssemblyConventions Shipped { get; } = new();
    public float BlendDefault { get; init; } = 0.2f;
    public float BlendFast { get; init; } = 0.1f;
    public float BlendSlow { get; init; } = 0.5f;
    public float LadderFloor { get; init; } = 5f;
    public (float Walk, float Trot, float Run) TurnAuthority { get; init; } = (75f, 112.5f, 270f);
    public (float RunAbove, float WalkBelow)? GaitSplit { get; init; }   // null = one compass
    public int FirstIStateId { get; init; } = 0;
    public bool IdleStopCapitalised { get; init; } = true;
}

public enum LocomotionPlan { CompassBiped, FourArm, Quadruped, ForwardOnly, Swimmer }
public enum Module { CombatStance, EquipTransitions, CannedTurns, Attacks, Recoil, Stagger,
    AnimatedDeath, GetUp, Reanimate, AggroWarning, BleedOut, Bash, Block, Swim, Sprint,
    Idles, LayDown, Feed, KillMoveVictim }

/// What Assemble would do, before it does it.
public sealed record AssemblyPlan(
    LocomotionPlan Plan,
    IReadOnlySet<Module> Modules,
    IReadOnlyList<SlotAssignment> Slots,          // role -> animation, or role -> reused animation, or empty
    IReadOnlyList<string> Variables,
    IReadOnlyList<string> Events,
    IReadOnlyDictionary<string, int> IStates,     // iState_<name> -> id
    IReadOnlyList<string> Warnings,               // a slot filled by reuse, a module short of a clip
    IReadOnlyList<string> Errors);                // below the floor, an attack with no event

public sealed record SlotAssignment(AnimationRole Slot, string? Animation, bool Reused);

public sealed record AssembledProject(
    ActorProject Project,
    IReadOnlyList<string> FilesWritten,
    CacheAmendment Cache,
    AssemblyPlan Plan);

public static class BehaviorAssembler
{
    /// Reads the roles and says what will be built. No file is touched.
    public static AssemblyPlan Plan(CreatureSpec spec);

    /// Builds the files under meshesFolder/actors/<group>/<name>/, adds the project to the
    /// cache, regenerates its rows, and returns the opened project. Throws on Plan errors.
    public static AssembledProject Assemble(SkyrimCache cache, CreatureSpec spec,
        string group, AssemblyOptions? options = null);

    /// Guesses roles from file names with the token rules of the census (idle, forward,
    /// left, right, back, walk, run, turn, attack, power, stagger, recoil, getup, death),
    /// for a front end to show and correct. Never used by Assemble on its own.
    public static IReadOnlyList<RoledAnimation> GuessRoles(IEnumerable<string> paths);
}

public sealed record AssemblyOptions
{
    public string? BehaviorFileName { get; init; }     // default <name>behavior.hkx
    public AnimationCompression Compression { get; init; } = AnimationCompression.Uncompressed;
    public bool CopySkeletonAndRagdoll { get; init; } = true;
    public CacheGenerationOptions Cache { get; init; } = new();
}
```

`Assemble` is the composition of what exists: `AnimationExchange.Import` for each
FBX (uncompressed, root motion and events derived), `CharacterFile.Create` for the
character, a template writer for the graph (new), a project block plus
`SkyrimCache.PromoteToActor` and the edit surface for the cache rows,
`CacheGeneration.Amend` for the two generated files. The one new piece is the template writer, and it is a function from
`AssemblyPlan` to an `hkbBehaviorGraph`.

## 6. Verification

The corpus is the oracle, and the tests are the ones this repository already runs:

- **rebuild a shipped creature from its own animations** -- the witchlight, the
  chicken, the hare, the mudcrab, the troll -- with roles read off their names, and
  compare the result with the shipped graph through `ProjectWalk`: same shell,
  same situation states, same plan, same clip-to-animation binding, same cache
  rows (`ConsistencyReport`), same speed block within tolerance;
- **the census as a gate**: `ZzCreatureCensus`'s reading of the assembled project
  must classify it as its plan, with the modules it was given and no other;
- **the engine evaluator** (`HKSK.Engine`, `docs/behavior-engine.md`): driven with
  `moveStart` and a `Direction`, the assembled graph must land in its locomotion
  state and sample the speeds the movement type asks for; driven with
  `attackStart_<name>`, the attack state and back on `attackStop`;
- **the contract check**: every variable of §1.2 the engine writes is declared,
  every event the plan's transitions use is either in the core, a module's, or an
  attack's, and no name is declared twice in two spellings;
- **the paired check**, when victims are given: each state's entry name is the
  kill-move's without `pa_`, the generator's prefix is `2_`, and the humanoid's
  graph declares the `pa_` form.

## 7. Not covered, and why

- **Flight.** The dragon's graph is its own vocabulary (`ST_Flight`, `ST_Hover`,
  `ST_Perch`, the `Flight*` handlers, the tail and bank channels) and
  `docs/flight.md` §3 is its authoring guide; the plan enum reserves nothing for it
  and this API does not build it. The ice wraith, wisp and chaurus flyer are not
  flyers to the engine: they are plan B creatures whose animations hover.
- **The humanoid** (`0_master`, 1136 machines) is not a creature and is not a target.
- **Character properties** are not generated; nothing but the shared quadruped
  file uses them.
- **The plugin side** is the caller's (§8); the API writes the graph's half and
  reports the names it used.

Everything the first draft of this section left out beside these -- the swim
plan and module, the pose-matched get-up, the look-at chain, the foot-IK legs --
is measured above and in the API.

## 8. What the plugin has to say

The records are not this library's to write (`HKSK.Records` reads them), but the
graph is only half of each of these, and the halves have to agree:

| record | what it names | the graph's half |
| --- | --- | --- |
| `RACE` | the behaviour graph path (`Actors\<group>\<name>\<name>project.hkx`), the movement types by role, the attack data | the project file at that path; `iState_<MNAM>` per movement type; a state per attack event |
| `MOVT` | the eight speeds and the `MNAM` name per movement type | the ladder rungs, the speed block's entry, the `iState_<MNAM>` constant |
| `IDLE` | one per event the creature is sent: the `Action*` idles for `moveStart`, `staggerStart`, `IdleStop` and the rest are the stock ones and need nothing; the creature's own idles (`idle<Name>Start`), its attacks' `attackStart_<name>` idles with their conditions, and any paired idle (`pa_<name>`, played on the initiator) | a state entered by each event, and for a paired idle the `2_`-prefixed victim state or the `pa_`-form initiator state (`docs/paired-animations.md`) |
| the response file | nothing per creature: `actorresponse.txt` is global | the clips fire the names it maps (`HitFrame`, `attackStop`, ...) |

`AssemblyPlan` reports the movement-type names, the attack events and the idle
events it built states for, which is the list the plugin side has to create.
