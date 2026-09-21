# The animation variables the engine reads and writes

What `SkyrimSE.exe` puts into a behaviour graph and takes out of it, by name, and
what that means for authoring a creature's or the player's behaviour. Read out of
the unwrapped executable (`docs/reverse-engineering.md` §3.10); addresses are
image-base virtual addresses.

## 1. Where the names live

The engine keeps one table of 189 `BSFixedString`s at `0x1420f6370`, filled once by
`0x14021f300` in a fixed order and reached by slot. The first 86 slots are **bone and
node names** (`NPC Head [Head]`, `WeaponSword`, `Camera3rd`, `SaddleBone` ...); the
rest, from `GraphDeleting` at `+0x2b0` to `PathTweenerEnd` at `+0x5e8`, are the
**graph variables and animation events** this document is about. A graph does not
have to declare any of them; the engine asks the graph's variable table by name and
does nothing when the name is absent. So the list below is the complete vocabulary
the engine shares with a behaviour graph, and a variable not on it is the graph's
private business.

How each name is used was read off the code that takes its slot (`call <accessor>`
then `lea 0x<slot>(%rax)`), and the call that follows classifies it:

| the call after the slot | what it is |
| --- | --- |
| holder vtable `+0x80` / `+0x88` / `+0x90` | the engine **reads** a float / int / bool out of the graph |
| `0x140bb66c0` / `0x140bb6790` / `0x140bb6600` | the engine **writes** a float / int / bool into the graph, at set-up |
| `0x14054b7d0`, `0x14054b7b0`, `0x14054b7f0`, `0x14054b800`, `0x14054b790`, `0x14054b660` | a **channel**: the actor pushes a float / int / vector / quaternion / bool / offset into the graph every frame |
| `0x14014eeb0` | an int written when the graph is bound to the actor |
| `0x140cec680` inside `0x1402187a0` | a **copy channel**: the graph's value is polled into the actor |
| `0x140cec8c0`, `0x1405381f0` | an **event name** the engine compares incoming animation events against |

## 2. Engine → graph: what the game tells the animation

Written by the engine. A graph reads these and must not write them.

**At set-up, once** (the graph is told what it is animating):

| variable | type | meaning |
| --- | --- | --- |
| `IsPlayer`, `iIsPlayer` | bool, int | this actor is the player. The humanoid graphs branch attacks and camera on it |
| `IsNPC` | bool | the opposite, kept separately |
| `IsFirstPerson`, `i1stPerson`, `fIsFirstPerson` | bool, int, float | the graph is the first-person one. `i1stPerson` also chooses branches by bound start state (the bleedout) |
| `fScale` | float | the actor's scale |
| `FemaleOffset` | float | the female body's offset |
| `bHumanoidFootIKEnable` | bool | the INI setting of that name, handed to the foot-IK modifiers |
| `bLeftHand`, `bRightHand` | bool | a hand holds something |
| `iLeftHandType`, `iRightHandType` | int | the weapon type in each hand (0 hand-to-hand, 1 sword, 2 dagger, 3 axe, 4 mace, 5 two-handed, 6 two-handed axe, 7 bow, 8 staff, 9 magic, 10 shield, 11 torch, 12 crossbow) -- also the hand variables of the set data |
| `iLeftHandEquipped`, `iRightHandEquipped` | int | equipped state per hand |
| `bIsSynced`, `bSpeedSynced`, `bDisableInterp` | bool | paired-animation synchronisation and interpolation control |
| `iSyncIdleLocomotion`, `iSyncTurnState`, `iSyncForwardState`, `iSyncStrafeState`, `iSyncSprintState` | int | the state ids the engine wants the locomotion machines to start in, consumed by machines in `START_STATE_MODE_SYNC` and written back by them -- §2.1 |

#### 2.1 The `iSync*` variables: start-state synchronisation

These deserve more than a row, because they are the one place the engine and the
graph share *state ids* rather than facts, and because the mechanism is Havok's,
not Bethesda's.

**The mechanism.** An `hkbStateMachine` has a `startStateMode`. In the default mode
it starts in `startStateId` when it activates. In `START_STATE_MODE_SYNC` it reads
its `syncVariableIndex` variable on activation and starts in the state whose id the
variable holds; when no state has that id it falls back to `startStateId` (measured
on the vampire brute, whose initial values name no state). And a synced machine
**writes the variable back** whenever it changes state, so the variable always
holds the id of the state the machine is in. That is the "sync": several machines
bound to one variable start in matching states, a machine re-entered after an
interruption resumes where its siblings are, and anyone who reads the variable --
another graph file, or the engine -- learns the graph's current state without
knowing the machine. The field is set on machines that do not use it too, so
reading `syncVariableIndex` without checking the mode picks an arbitrary state.

**How the engine takes part.** The engine writes four of them once, when the graph
is bound to the actor (`0x14014eeb0` on the slots of `iSyncIdleLocomotion`,
`iSyncTurnState`, `iSyncForwardState` and `iSyncStrafeState`): the state ids it
wants the locomotion machines to start in. It pushes two every frame through
channels -- `iSyncSprintState` (`0x14054b790`) and `iSyncStrafeState`
(`0x14054b7b0`) -- because sprinting and strafing are things the controller decides
and the graph has to follow. And it **reads back** `iSyncIdleLocomotion`,
`iSyncTurnState` and `iSyncSprintState` with the int getter (`+0x88`), which is the
sync mechanism working in the other direction: after the graph's own machines have
moved, the variable says whether the actor is standing or moving, turning or not,
sprinting or not, and the engine reads it as a fact about the animation.

**What the ids mean**, read off every machine in the shipped graphs that is in sync
mode on one of these names (a census over the 49 projects):

| variable | machines | ids | where |
| --- | --- | --- | --- |
| `iSyncIdleLocomotion` | 3 in sync mode, 11 files declare it | **0 standing, 1 moving** (`BlockIdle_StandingState`/`BlockIdle_MovingState`, `H2H_Standing_State`/`H2H_Locomotion_State`, `IdleState`/`NonCombatLocomotion`) | the humanoid's block idle, the falmer, the riekling |
| `iSyncSprintState` | 20 | **0 default, 1 sprint** (`JumpFall`/`JumpFall_Sprint`, `MT_Jump`/`MT_Jump_Sprint`, `1HM_UpperBodyDefaultState`/`1HM_UpperBodySprintState`, `MT_Torch_IdleLocomotion`/`MT_Torch_Sprint` ...) | every humanoid machine that has a sprinting variant of a state: the jumps, the falls, the landings, the torch arm, the first-person upper body |
| `iSyncTurnState` | 1 in sync mode, 9 files declare it | **1 not turning**, 0 turning in place | the humanoid's turn selectors; the rest read it through bindings |
| `iSyncForwardState` | 0 in sync mode, 1 file declares it | **0 forward** | `mt_behavior.hkx`, read by binding |
| `iSyncStrafeState` | 0 in sync mode | strafe direction | pushed per frame, read by bindings |
| `iSyncIdleState` | 3 | 0 `LocomotionDefault`, 1–5 the dialogue idle variants (`_DialogueIdle`, `_Happy`, `_Angry`, `_ResponseNegative` ...) | `MT_LocomotionIdleSelector`, `MT_TurnLeftIdleSelector`, `MT_TurnRightIdleSelector` |
| `iSyncDefaultState` | 14 | the creature's top-level mode: **0 non-combat, 1 combat** and the equip, unequip, stagger, kill-move and aggro-warning states after (`NonCombatState`/`CombatReadyState`/`WeapEquip`/`WeapUnEquip`/`StaggerState`/`KillMoveState`) | one base machine per creature: atronachs, giant, lurker, hagraven, ice wraith, netch, riekling, spriggan, vampire brute, wisp, witchlight |
| `iSyncWard` | 2 | the ward's state | the humanoid's ward |

The ids are the machine's own `stateId`s, and where two machines sync to one
variable they agree by construction: every sprint variant is state 1, every
non-combat state is 0. That is what makes the engine's defaults meaningful across
creatures without the engine knowing any graph.

**Bethesda's driver values.** Read a graph at rest the way the game binds it and
you get `iSyncIdleLocomotion = 1`, `iSyncForwardState = 0`, `iSyncTurnState = 1`:
moving, forward, not turning in place. Those are the values HKSK's evaluator sets
to put a graph into locomotion (`ActiveGenerators`, `SpeedDataGenerator.StateAt`),
and they are the ones that land the speed table's declared states. `iSyncSprintState
= 1` is what the way into a sprint state pins, since the machine chooses by it
rather than by an event.

**Authoring.** Give a machine that has a resting and a moving form, or a walking
and a sprinting form, sync mode on the shared variable and the same ids as the
shipped graphs use (0 rest/default, 1 moving/sprint); the engine will then start
it right and read it back right. Declare the four bound-at-start names in the root
graph or the engine's write is silently dropped. Do not write `iSyncSprintState` or
`iSyncStrafeState` from the graph: they are the controller's, pushed every frame,
and a graph write is overwritten. `iSyncDefaultState` is a creature's own -- the
engine neither writes nor reads it -- and is the conventional place to hold the
combat/non-combat mode so that every machine that cares starts in step.

**Every frame, through channels** (the actor's state, as the graph's inputs):

| variable | type | meaning |
| --- | --- | --- |
| `Speed` | float | the requested movement speed, in movement-type units; the speed sampler's `goalSpeed` |
| `Direction` | float | heading as a fraction of a turn: 0 ahead, 0.25 right, 0.5 behind, 0.75 left; the direction blends' parameter |
| `TurnDelta` | float | the yaw rate the controller wants, for the turn blends |
| `VelocityZ` | float | vertical velocity, for jumps and falls |
| `MovementDirection`, `TargetLocation` | vector | where the actor is going and what it is going to |
| `DistToGoal` | float | distance to the movement goal, for stopping animations |
| `TweenEntryDirection`, `TweenPosition`, `TweenRotation`, `TweenSpeed`, `HasTweenSpeed`, `bTweenUpdate` | float, vector, quaternion, float, bool | the path tweener that drives an actor onto a mark, as for furniture and paired animations |
| `staggerMagnitude`, `staggerDirection` | float | the stagger the actor took, for the stagger blends |
| `weapAdj`, `weaponSpeedMult`, `leftWeaponSpeedMult` | float | weapon adjustment and the attack-speed multipliers; clips bind `playbackSpeed` to the last two |
| `BowZoom`, `bBowZoomed` | float, bool | the bow zoom |
| `fCastStrength` | float | the spell charge |
| `iGetUpType`, `Injured`, `Land`, `bCrashLand`, `LandTypeIndex` | int, bool, ... | ragdoll recovery and landing |
| `ConstraintOffset` | offset | a constraint the actor is attached to |
| `TailPitchCurrent`, `TailPitchMax`, `TailYawCurrent`, `TailYawMax` | float | the dragon's tail |
| `AimHeadingMax`, `AimPitchMax`, `AimHeadingCurrent`, `AimPitchCurrent`, `LookAtHeadingMaxAngle` | float | aim and look-at limits; read back by the engine as well (§3) |

## 3. Graph → engine: what the animation tells the game

Read by the engine with the holder's getters, so a graph sets them and the game
acts. Every one is a report: the graph decides, the engine follows.

| variable | type | what the engine does with it |
| --- | --- | --- |
| `bAnimationDriven` | bool | polled with `bAllowRotation` at `0x140669760`; on a change the engine sends `StartAnimationDriven` (1, root motion moves the actor), `StartAllowRotation` (0 and rotation allowed) or `StartMotionDriven` (both 0). Raise it on a branch whose clip owns the movement -- a power attack, a get-up -- with a `BSIsActiveModifier` |
| `bAllowRotation` | bool | the actor may be turned by the controller while animation-driven |
| `iState` | int | the movement type: the engine turns the value into the `iState_<MNAM>` suffix declared in the root graph and applies that record (`docs/speed-data.md` §4.5, §7.4); the speed sampler keys its table on it |
| `SpeedSampled`, `HorseSpeedSampled` | float | the speed sampler's answer, copied to the actor each frame (`docs/speed-data.md` §4.5) |
| `IsBlocking`, `IsAttackReady`, `IsSprinting`, `IsBusy`, `IsShouting` | bool | the actor's animation state, read by combat and the controller: blocking is up, an attack may start, the sprint state is live, the graph is busy with something uninterruptible, a shout is playing |
| `bVoiceReady`, `bMLh_Ready`, `bMRh_Ready`, `bEquipOk` | bool | the shout, the left and right spell, and an equip may proceed -- gates the magic and equip systems wait on |
| `bAimActive`, `bForceIdleStop`, `LookAtOutOfRange` | bool | aim is engaged; the idle must stop now; the look-at target is out of the allowed angle |
| `iSyncIdleLocomotion`, `iSyncTurnState`, `iSyncSprintState`, `iGetUpType`, `iLeftHandType`, `iRightHandType` | int | read back where the engine needs the graph's current choice |
| `Speed`, `AimHeadingMax`, `AimPitchMax`, `AimHeadingCurrent`, `AimPitchCurrent`, `LookAtHeadingMaxAngle` | float | read back for the aim and look-at solvers |
| `Injured`, `HasTweenSpeed`, `bSpeedSynced` | bool | read back beside their write |
| `GraphDeleting` | bool | the graph is being torn down |

## 4. Events the engine listens for

Names compared against incoming animation events, so a clip trigger with one of
them reaches game code. `HitFrame`, `preHitFrame`, `weaponSwing`,
`weaponLeftSwing`, `AttackWinStart`, `AttackWinEnd` (combat: the hit lands, the
swing sound, the window in which the next attack may be queued -- the set data's
combat context also reads the `HitFrame` time out of the animation);
`MLh_SpellFire_Event`, `MRh_SpellFire_Event`, `Voice_SpellFire_Event`,
`arrowRelease` (the projectile leaves); `EndAnim`, `StopEffect`; `PickUp`,
`PathTweenerStart`, `PathTweenerEnd`; `ActorResponse`, `PlayerCharacterResponse`;
`SpecialIdle_Cast`, `SpecialIdle_AreaEffect`; `English`, `Russian`, `Polish`
(lip-sync language); `ObjectActivated`, `PairedKillTarget`, `TurnDynamic`,
`fFlameProjectileLength`, `Imod`, `Rimod`, `Left`, `fIdleTimer` (in the table, no
reader found in this pass). `MTState`, handled by `MTStateHandler`, is not in this
table: event handlers are registered by name in a factory.

## 5. Authoring a creature's behaviour

**Declare what the engine writes, and only read it.** A graph needs `Speed`,
`Direction` and `TurnDelta` to move at all, `iSyncIdleLocomotion`,
`iSyncForwardState` and `iSyncTurnState` for its locomotion machines to start where
the controller wants, and whichever of the rest its states use. Names must match
exactly; an undeclared name is silently not written.

**Report what the engine reads.** Set `iState` by one of the four writers
(`docs/speed-data.md` §7.4); raise `bAnimationDriven` on the branches that carry
their own root motion and nowhere else, since combat measures a moving attack by
the actor's speed only where it is clear (`docs/animation-set-data.md` §4.6); keep
`IsAttackReady`, `IsBlocking`, `bEquipOk` and the spell-ready flags true where the
game may act and false where it must wait, because the AI waits on them.

**Fire the events the game counts on.** An attack clip without `HitFrame` never
lands; a spell clip without its `SpellFire` event never casts; a bow without
`arrowRelease` never shoots. `weaponSwing` and the attack window bound what the
player can queue.

**For the player**, the same graph carries `IsPlayer` / `iIsPlayer` branches -- the
`_Player` variants of the power attacks are tagged differently for `iState` -- and
a first-person graph is told so by `IsFirstPerson`; the horse rider additionally
reads `HorseSpeedSampled`, the mount's sampled speed pushed into the rider.

Everything else a graph declares -- `SpeedDamped`, `iWantBlock`, `bWantCastLeft`,
the `iState_` constants, the hundreds of `b*Ready` and `iSync*` intermediates -- is
the graph's own, computed by its modifiers and read by its own machines and
blends. The engine never touches them, which is why a search over bindings for
who sets them finds only the graph.
