# Assembling a behaviour from animations, a skeleton and a ragdoll

    Status:   DESIGN, being built. The facts are measured (the documents cited).
              §3, reading a set of roles and saying what can be made of them, is
              built and tested as `HKSK.Assembly.CreaturePlanner`; the rest of the
              API is proposed and not yet built.
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

- **Root motion, where the animation has none.** An animation made for an engine
  that moves the actor in code carries no travel: a skeleton bought from a
  marketplace goes 0.33 units over its whole walk cycle, which is hip sway. Since
  the motion lives in the cache rather than in the animation it can simply be
  authored, and there are two ways to arrive at the number. A caller may state the
  speed a clip should deliver, which `SyntheticMotion.Travel` turns into a
  displacement. Or it may be **read off the feet**: a foot on the ground does not
  move, the world moves past it, so the planted foot slides backwards at exactly
  the speed the creature should travel forwards (`HKFBX.Model.FootMotion`). On the
  marketplace skeleton that reads the idle at no travel and full confidence, the
  run at 160 units a second, the walks at 71 and 50 and the side-steps at 90
  degrees either way. Only travel can be read this way and never turn, since one
  planted foot cannot tell walking forward from turning about a distant centre; a
  turn is authored (§3.1).
- **The ladder rungs** are the movement type's walk and run speed per heading
  (`MovementType`, eight numbers), and **the movement type is authored from the
  clips, not the other way round** (`docs/speed-data.md` §7.2): walk on a heading
  is the delivered speed of that heading's walk clip, `|travel| / (duration /
  playbackSpeed)`, from the cache's root motion, and run is the run clip's --
  `SpeedRung.Delivered` in the library. So the eight speeds are *derived* by the
  assembler and reported, and the table written from them is the identity along
  every rung. A caller may override them, at the cost of clips playing scaled.
  The floor rung is the walk clip at 5. What the clips do not give is the record's
  three rotation rates, which are gameplay constants by family (90/180/180 for a
  biped, 180/270/360 for a bounding quadruped, §7.2) and its anim-change
  thresholds, which are `FLT_MAX` on 88 of 106 shipped records (§7.5).
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

### 1.7 Air: the flight plan

One creature flies, and `docs/flight.md` is its document; what an assembler needs
of it fits in a page. The engine's side is a fly state it learns **only from the
graph's state-entry events**, six paths it flies every flyer along, and requests
that come with targets. The graph's side, read off the dragon (66 machines, of
which flight is one machine of six states):

    ST_Ground  --TakeOff, TakeOff_Vertical-->  ST_TakeOff  --TakeOff_to_WingState-->  ST_Flight
    ST_Flight  [enter: FlightCruising]   --HoverStart-->  ST_Hover [enter: FlightHovering]
               --FlyStop*-->  ST_Land [enter: FlightLanding]  --LandEnd-->  ST_Ground
               --PerchLandEnd-->  ST_Perch [enter: FlightPerching, idleChairSitting]
    ST_Flight_Kill_Grab [enter: FlightAction, exit: FlightActionEnd]
    wildcards: FlyStartCruise -> ST_Flight, FlyStartHover -> ST_Hover

| state | what it holds | slots at the floor | slots as shipped |
| --- | --- | --- | --- |
| take-off | `BSTweenerModifier` to the engine's tween target; one clip per launch kind | 1 (`TakeOff`) | 9: 45°, vertical, one per perch type, water exit |
| flight | the cruise machine: flap / glide / feather chosen by the graph's own speed model, each a `TurnDeltaDamped` bank tri-blend (left, straight, right); climb and dive on `FlightPitchBlend`; a hurt family on `iInjured` | 1 (`Cruise`) | 28 |
| hover | tweener to the target; `TweenEntryDirection` blend of five entries (straight, ±90, ±180); hover idle; hover turns; hover stagger | 2 (`HoverEnter`, `HoverIdle`) | 20 |
| land | tweener; `TweenEntryDirection` blend of five approaches per landing kind (default, vertical, crash short, crash long, pounce), one per perch type | 1 (`Land`) | 47 |
| perch | furniture: per perch type an idle, a launch, a stagger, the shouts | 0 (module) | 22 |

The floor is five animations -- take-off, cruise, hover entry, hover idle,
landing -- with the six enter events declared and a tweener on take-off, hover
entry and landing. Without the tweeners the creature lands where its own motion
leaves it and hovers beside its target. **Flight is motion-driven**: no flight
state raises `bAnimationDriven`, the cruise clips carry no root motion and only
pose the creature while the follower's velocity moves it (`docs/flight.md` §2);
the take-off, hover-entry and landing clips do carry travel in the cache, and the
tweeners carry the actor to the engine's target. The wing model (`MaxAcc`,
`MaxDec`, `Drag`, the flap/glide/feather thresholds) is optional: with one cruise
clip the movement type's speed is what the follower plans with (`docs/flight.md`
§3.6). The speed table is not meaningful for a flyer, since the graph writes
`Speed` back itself; the block is still written. The `iState` per posture (default, flying, hovering,
perching -- the dragon's four constants) is set on entering each state.

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
              KillMoveVictim | TakeOff | Cruise | HoverEnter | HoverIdle |
              HoverTurn | Land | CrashLand | PerchIdle | PerchLaunch |
              UpperBodyCast | Custom
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

- the movement types, by role (walk, run, swim, sprint) -- but only their *names*
  and which roles share one, since the eight speeds are derived from the clips
  (§1.3) and reported for the `MOVT` the caller then writes; a caller that already
  has a `MOVT` may pass its speeds as an override, and the rotation rates come from
  the plan's family default unless given;
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
| take-off, cruise, hover entry, hover idle and land, beside a ground plan | the ground plan plus the flight module of §1.7 |

Within the plan:

- a walk with no run gives a two-rung ladder (walk at 5, walk at the walk speed);
  a run adds the third rung; a trot or sprint adds a gait state beside it;
- turn-in-place with one clip per side gives the blend form (weights 5, 90, 135);
  one clip with `Mirror` gives the loop form with a `[Mirrored]` clip; **with no
  turn clip at all one is made** -- see below;
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

### 3.1 A turn in place can be made rather than given

Root motion lives in the animation cache and nowhere else, so where the root goes
is a property of an animation *slot* and not of the animation data, and two slots
may hold the same animation and move differently.

That makes a turn in place the one part of a creature that can be authored without
an animator. The shipped ones already are rotation and nothing else: the sabre
cat's `TurnLoopingL` travels zero units and turns 87 degrees over half a second,
and the draugr's are the same shape. So a creature with no turn clip is given its
idle in a second slot and a turn, one each way. The feet do not shuffle, which is
the whole difference from an authored turn, and the creature turns at a rate
somebody chose instead of not turning at all.

The rate is the creature's size read against the shipped ones. Those divide the
requested turn by the looping clip's own rate, and the rates run from the
mammoth's 45 through the commonest 90 -- the deer, the goat, the horker, the
skeever -- to the canines' and the sabre cat's 112.5, which is the fastest
anything in the game turns.

**Built.** `SyntheticMotion.TurnInPlace(seconds, degrees)` and
`SyntheticMotion.ReasonableTurnRate(height)`.

**Built.** `CreaturePlanner.Of(animations)` returns a `CreaturePlan`: the
locomotion plan, the modules the animations are enough for, the attack events,
the headings, a note per reading, and a refusal per thing missing. It is pure and
writes nothing, so a front end can show it before a byte is committed, and the
answer to "why has my creature no combat stance" is a note in it rather than a
silence in the graph. A module is in the plan exactly when its animations are,
and out of it with a note saying which animation would have put it in.

## 4. What is built

**Being built.** `CreatureAssembler.Assemble(spec, folder)` writes the project, the
character and the behaviour, copies the skeleton and the animations, and returns
the plan and the cache row beside the list of files. What the graph holds so far:

- the engine's variables and events;
- a root machine whose live state runs a modifier list holding the **speed
  sampler**, bound `state<-iState`, `direction<-Direction`, `goalSpeed<-Speed`,
  `speedOut<-SpeedSampled` as all 38 of the shipped ones are, and the expression
  that writes `iState`;
- a situation machine with the default situation;
- inside it, a standing idle and the moving part: **one speed ladder per heading,
  and a compass over the ladders** where there is more than one heading, at the
  positions the draugr's eight arms sit at. The rungs of a ladder are the gaits in
  the order they carry the creature.

The engine driven through it reaches the idle at rest and the locomotion on
`moveStart`. Which rung it lands on cannot be asserted until the speed table is
written, since the rungs blend on what the sampler writes and the sampler reads
the request through that table.

The modules of §1.1, the set data and the speed table are next.

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

A **partial-body blend** -- casting or shouting over the locomotion, 18 creatures
-- is an ordinary blend whose second child carries an `hkbBoneWeightArray`
inline: one weight per bone, the upper body at 1 and the rest at 0. Every
single-creature graph in the corpus inlines it (the falmer 22 times, the draugr,
hagraven, dragon, storm atronach); only the shared quadruped file binds the
array to a character property (`Tail`, `Body`, `Head`), because one file serves
ten skeletons. So the module needs a bone mask, given as the list of bones the
upper body starts at, and no character property.

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

**Which roles need a root track, and why it is not optional.** The engine moves an
actor by root motion only in animation-driven mode, and then by the cache's
movement block alone: `MovementTweenerAgentAnimationDriven` samples the block at
the clip's time every frame and the Havok file's extracted motion is never read
(`docs/animation-data.md` §4.3). In motion-driven mode the controller's velocity
moves the actor and the block is ignored. So the root track an animation is
imported with decides what its clip can do:

| role | mode the state raises | root track |
| --- | --- | --- |
| walk, run, trot, sprint, swim, turn-in-place loops | motion-driven (the sampler's speed) | **required** -- not for movement but for the speed table and the ladder rungs, which are computed from it; a clip without one records a creature that cannot move |
| attack, power attack, canned turn, bash, get-up, death, stagger, recoil, aggro, kill-move victim | `bAnimationDriven` (or `bAllowRotation` where the AI may still turn the actor) | **required where the clip should move the actor**; combat measures an attack's reach from it (`docs/animation-data.md` §4.3); a clip without one plays in place |
| idle loop, idle variant, combat idle, equip, unequip, block, lay-down, feed | motion-driven at rest | none needed |
| take-off, hover entry and exit, land, crash-land, perch launch | motion-driven under a tweener | as shipped: large travels in the cache (§1.7); the tween carries the actor, so the track's use there is not settled |
| cruise, hover idle, hover turn | motion-driven by the flight follower | none: the dragon's carry none |

`Plan` reports every role in the first two rows whose animation carries no root
track, as a warning, and the cache row is still written.

**Being built.** `Assemble` returns the creature's `AnimationDataProject`: the file
list, every clip generator against the position of its animation, and a movement
block for each slot that carries the creature anywhere. A caller merges it into
the game's `animationdatasinglefile.txt`.

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
    Swim, SwimTurn, IdleVariant, Feed, LayDown, LayLoop, LayUp, KillMoveVictim,
    TakeOff, Cruise, HoverEnter, HoverIdle, HoverTurn, Land, CrashLand, PerchIdle, PerchLaunch,
    UpperBodyCast, Custom }

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

/// An animation and what it is. Path is a Havok file on disk, or an FBX to import;
/// an FBX holds one stack per clip, and Stack names the one meant. Assemble imports
/// in the order the list gives, because that order is every clip's cache index.
public sealed record RoledAnimation(string Path, IReadOnlyList<AnimationRole> Roles,
    string? Stack = null,
    IReadOnlyList<ClipEvent>? Events = null);   // HitFrame and friends, in seconds

public sealed record CreatureSpec
{
    public required string Name { get; init; }              // project stem, e.g. "MyBeast"
    public required string SkeletonPath { get; init; }
    public required string RagdollPath { get; init; }
    public required IReadOnlyList<RoledAnimation> Animations { get; init; }
    public SkeletonRoles Bones { get; init; } = new();
    /// Movement types by role: a name per role, roles that share a name share a type. The
    /// speeds are derived from the clips (§1.3) unless an override is given for a name.
    public IReadOnlyDictionary<MovementRole, string> MovementNames { get; init; } =
        new Dictionary<MovementRole, string> { [MovementRole.Walk] = "Default", [MovementRole.Run] = "Default" };
    public IReadOnlyDictionary<string, MovementType>? MovementOverrides { get; init; }
    public IReadOnlyList<string> AttackEvents { get; init; } = [];   // the race's, or intended; the Attack roles' Names when empty
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
    public IReadOnlyList<string> UpperBodyRoots { get; init; } = [];        // bones whose subtrees are "upper body"; empty = no partial-body blends
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
    public (float InPlaceWalk, float InPlaceRun, float WhileMoving)? TurnRates { get; init; }  // null = by plan: 90/180/180 biped, 180/270/360 quadruped
    public int FirstIStateId { get; init; } = 0;
    public bool IdleStopCapitalised { get; init; } = true;
}

public enum LocomotionPlan { CompassBiped, FourArm, Quadruped, ForwardOnly, Swimmer }
public enum Module { CombatStance, EquipTransitions, CannedTurns, Attacks, Recoil, Stagger,
    AnimatedDeath, GetUp, Reanimate, AggroWarning, BleedOut, Bash, Block, Swim, Sprint,
    Idles, LayDown, Feed, KillMoveVictim, Flight, Perch, UpperBody }

/// What Assemble would do, before it does it.
public sealed record AssemblyPlan(
    LocomotionPlan Plan,
    IReadOnlySet<Module> Modules,
    IReadOnlyList<SlotAssignment> Slots,          // role -> animation, or role -> reused animation, or empty
    IReadOnlyList<string> Variables,
    IReadOnlyList<string> Events,
    IReadOnlyDictionary<string, int> IStates,     // iState_<name> -> id
    IReadOnlyDictionary<string, MovementType> Movements,   // per name: the eight speeds read off the clips (or the override), the turn rates by convention -- the MOVT to write
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

`Assemble` is the composition of what exists, in an order that is not free:
`CharacterFile.Create` for an empty character and the project block, so the actor
exists; then, **in the order the animations were given**, `AnimationExchange.Import`
for each FBX stack (uncompressed, root motion and events derived) or `AddAnimation`
for a Havok file, since each append is that clip's cache index; then the template
writer for the graph over the finished list (new); then the edit surface for the
clip rows and `CacheGeneration.Amend` for the two generated files. The one new piece is the template writer, and it is a function from
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

## 7. Scope: what stays out, and what it would take

Everything the first drafts of this section listed as not covered is now
measured and in the API -- the swim plan and module (§1.6), the flight module
(§1.7), the pose-matched get-up, the look-at chain and the foot-IK legs (§2.2),
partial-body blends (§4.2). Two things stay out by decision:

- **The humanoid.** `0_master` and its sixteen sibling files hold 3,152 clips and
  1,136 machines, 222 of the clips synchronised. What makes it a different product
  is not size but shape: the stance is repeated **per hand-type pair** under 69
  manual selectors on `iRightHandType` and 66 on `iLeftHandType`, the sneak form
  of every machine is a second copy synced on `iIsInSneak` (36 machines), the
  first-person graph is chosen by `i1stPerson` (48 selectors), and the set data
  and the engine's hand-type readers assume that layout
  (`docs/animation-set-data.md` §4, `docs/animation-variables.md` §2.0). A
  humanoid assembler would take roles crossed with a hand-type pair and a sneak
  flag and replicate the compass-biped plan per cell; its floor is not seven
  animations but a few hundred. The ice wraith, wisp and chaurus flyer are not in
  this bracket: they are plan B creatures whose animations hover.
- **The plugin records.** §8 lists them; `HKSK.Records` reads them and nothing here
  writes them. The API reports every name the plugin has to create.

Character properties turned out to need no decision: no single-creature graph
uses one, and the two things they do in the shipped game -- pick a body in the
shared quadruped file, hold that file's bone masks -- do not arise when each
creature has its own file.

## 8. What the plugin has to say

The records are not this library's to write (`HKSK.Records` reads them), but the
graph is only half of each of these, and the halves have to agree:

| record | what it names | the graph's half |
| --- | --- | --- |
| `RACE` | the behaviour graph path (`Actors\<group>\<name>\<name>project.hkx`), the movement types by role, the attack data | the project file at that path; `iState_<MNAM>` per movement type; a state per attack event |
| `MOVT` | the `MNAM` name per movement type, the eight speeds **as the plan reports them** (read off the clips' travel, §1.3), the turn rates by family, `FLT_MAX` thresholds | the ladder rungs at those speeds, the speed block's entry, the `iState_<MNAM>` constant |
| `IDLE` | one per event the creature is sent: the `Action*` idles for `moveStart`, `staggerStart`, `IdleStop` and the rest are the stock ones and need nothing; the creature's own idles (`idle<Name>Start`), its attacks' `attackStart_<name>` idles with their conditions, and any paired idle (`pa_<name>`, played on the initiator) | a state entered by each event, and for a paired idle the `2_`-prefixed victim state or the `pa_`-form initiator state (`docs/paired-animations.md`) |
| the response file | nothing per creature: `actorresponse.txt` is global | the clips fire the names it maps (`HitFrame`, `attackStop`, ...) |

`AssemblyPlan` reports the movement-type names, the attack events and the idle
events it built states for, which is the list the plugin side has to create.
