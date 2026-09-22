# What the 49 projects share: the shape of a creature behaviour, measured

    Status:   census CONFIRMED -- every number below is counted over the shipped
              graphs, not taken from a list or a tutorial
    Source:   the 49 actor projects under meshes/actors, walked with
              HKSK.Behavior.ProjectWalk; dumped per project by
              tests/HKSK.Tests/ZzCreatureCensus.cs (files, variables, events,
              character properties, the state tree with transitions, every clip
              with its state path and triggers); the paired generators and the
              speed blocks by tests/HKSK.Tests/ZzOpenPoints.cs
    Scope:    the 46 creatures. DefaultMale, DefaultFemale and FirstPerson share
              0_master (17 files, 1136 state machines) and are a different animal;
              they are cited only where the shell is the same
    Goal:     author a behaviour for a creature whose animations already exist as
              FBX -- so the question is what every shipped creature has, what only
              a family has, and which animation each part consumes

## 0. The answer in one paragraph

Every creature is the same four-layer object. A **root machine** that holds one
live state and the ragdoll shell (animate-to-ragdoll, fully ragdoll, get up) and
catches `Ragdoll`, `RagdollInstant` and `DeathAnimation` with wildcards. A **root
modifier list** that runs whatever must run in every state: keyframed bones and
ragdoll drive, the speed sampler, look-at, the draw/sheathe bookkeeping. Below the
live state, a **behaviour machine** whose states are the creature's *situations*
(default, stagger, recoil, attacking, bleed-out, the optional modules), reached by
the engine's events. And inside the default situation, one of **five locomotion
plans** built from the same three bricks -- a standing machine (idle plus turning),
a moving generator (a compass, a ladder, or a clip) and canned turns -- with a
combat stance doubling the standing and moving parts under a readied state. The
variation between creatures is which plan, which optional modules, and how many
animations fill each slot. Nothing else varies in kind.

## 1. Size and files

| | min | median | max |
| --- | ---: | ---: | ---: |
| animations in the character file | 7 (witchlight) | 42.5 | 216 (draugr) |
| clip generators | 11 | 68 | 414 |
| state machines | 6 | 24 | 79 |
| variables in the root file | 18 (ice wraith) | 61 | 155 (hagraven) |
| events in the root file | 17 (witchlight) | 121.5 | 482 (dragon) |

36 of 46 creatures are one behaviour file. The ten quadrupeds (bear, deer, dog,
goat, highland cow, horker, mammoth, sabre cat, skeever, wolf) are four: their own
root plus `quadrupedbehavior.hkx`, `forwardlocomotion.hkx` and
`noncombatidle.hkx` shared by name (`docs/behavior-engine.md` §4.2 for how one
file serves ten). The boar and the scrib are the quadruped graph copied into one
file and edited. The draugr and the draugr skeleton are one graph with two
character files.

## 2. The shell every creature has

### 2.1 The root machine

    Root (hkbStateMachine, start = the live state)
      *on Ragdoll          -> Fully Ragdoll          45 of 46
      *on RagdollInstant   -> Fully Ragdoll          37
      *on DeathAnimation   -> AnimateToRagdoll       34
      *on staggerStart     -> Stagger_State          25 (here or one level down)
      S  RootAnimState     -- the live state; everything of §3 on is under it
      S  AnimateToRagdoll  -- 36: plays the death clip, then Ragdoll
      S  Fully Ragdoll     -- 45: powered ragdoll, waits for GetUpStart
      S  GetUpFromRagdoll  -- 44: pose-matched get-up, then GetUpEnd -> live state

The names vary (`RootAnimState`, `RootState`, `DraugrRoot`, `ST_Default`;
`FullyRagdoll` 7 times, `Fully Ragdoll` 37) and the ids are arbitrary; the four
roles do not. The only creature without the shell is the witchlight (root =
`RootState` + `StaggerState`, no ragdoll at all). The dragon has the three ragdoll
states and no get-up. Nine creatures skip `AnimateToRagdoll` -- chicken, hare,
goat, sabre cat, skeever, dwarven spider, slaughterfish, netch, horse -- and go
from `Ragdoll` straight to the ragdoll: **a death animation is optional, a
ragdoll is not.**

What the three ragdoll states hold is the same node-for-node in every project
that has them (chicken, dog and draugr checked in full):

| state | generator | modifiers | events |
| --- | --- | --- | --- |
| `AnimateToRagdoll` | the death clip, `mode` once, firing `Ragdoll` at its end (42 clips) | `hkbKeyframeBonesModifier` (full ragdoll keyframed), `BSRagdollContactListenerModifier` | enter: `AddRagdollToWorld`; `on Ragdoll -> Fully Ragdoll` |
| `Fully Ragdoll` | none, or the pose-matcher (see next) | `hkbEventDrivenModifier` "FullRagdoll" holding `hkbPoweredRagdollControlsModifier` (no matching); `hkbEventDrivenModifier` "TurnOnMatchingRagdoll" holding a second powered-ragdoll (matching) and `hkbTimerModifier` "GetUpTimerMod" | enter: `RemoveCharacterControllerFromWorld` (+ `InterruptCast` on casters); `on GetUpStart -> GetUpFromRagdoll` |
| `GetUpFromRagdoll` | `hkbManualSelectorGenerator` on `iGetUpType` choosing between a **Reanimate** blend and a **Get Up** blend; each is a plain blend (flags 0, weights 1) of one to three get-up clips that the pose matcher picks between | `hkbGetUpModifier`, `BSIsActiveModifier` raising `bAnimationDriven`, and on quadrupeds the keyframe/ragdoll-drive pair again | `on GetUpEnd -> live state`; the get-up clips fire `GetUpEnd` (160), `AddCharacterControllerToWorld` (160), `Getup` (92), `Reanimated` (63) |

The get-up blend is the one place the number of animations is a free choice: one
clip (27 blends -- the atronachs use their idle), two (32 -- face up / face down,
or left / right), or three (21 -- left, right, standing). The engine reads
`iGetUpType` before it sends `GetUpStart` (`docs/animation-events.md` §7), which
is why the selector is bound to it and why 45 of 46 declare it.

### 2.2 The root modifier list

Under the live state, before its machine, a `hkbModifierGenerator` whose list is
(count = creatures placing it at the root; every one of them also appears
elsewhere):

| modifier | at root | in project | what it is for |
| --- | ---: | ---: | --- |
| `hkbKeyframeBonesModifier` | 34 | 45 | the ragdoll follows the animation |
| `hkbRigidBodyRagdollControlsModifier` | 34 | 45 | drives the ragdoll bodies at the pose |
| `BSSpeedSamplerModifier` | 20 | 38 | `state<-iState, direction<-Direction, goalSpeed<-Speed, speedOut<-SpeedSampled` -- the speed table join (`docs/speed-data.md` §4.5) |
| `hkbEvaluateExpressionModifier` | 18 | 44 | the draw/sheathe bookkeeping: `weaponDraw if (iCombatStance == 1)` and its three siblings, and per-family `iState` writes |
| `BSIsActiveModifier` | 18 | 46 | latches a boolean while a branch is live (`bAnimationDriven`, `bHeadTrackingOn`, `IsAttackReady`) |
| `hkbGetUpModifier` | 17 | 44 | |
| `BSLookAtModifier` | 11 | 32 | `lookAtTarget<-bHeadTracking, targetLocation<-TargetLocation` |
| `hkbFootIkControlsModifier` | 3 | 20 | the twenty-two `m_*Gain` variables are its |

Missing the sampler are the 8 creatures whose speed is not sampled (witchlight,
wisp, ice wraith, chaurus flyer, flame and storm atronach, dragon priest, and the
dragon, whose flight is its own system) -- the hoverers and drifters, and all of
them declare `iState` anyway.

### 2.3 The engine contract, as the creatures actually declare it

495 distinct variable names appear across the 46 creatures; 385 of them in fewer
than eight projects. The shared core is small:

| declared by | variables |
| ---: | --- |
| 46 | `bAnimationDriven`, `iState`, `IsAttackReady`, `iSyncIdleLocomotion`, `Direction` |
| 44–45 | `iGetUpType`, `Speed`, `bAllowRotation`, `TurnDelta`, `bEquipOK`, `staggerMagnitude` |
| 40–42 | `IsStaggering`, `iSyncTurnState`, `IsRecoiling`, `staggerDirection` |
| 30–37 | `IsAttacking`, `TargetLocation`, `IsBashing`, `SpeedSampled`, `bIsSynced`, `camerafromx/y/z` |
| 23–29 | `bHeadTracking`, `bHeadTrackingOn`, `FootIKEnable`, `blendDefault`, `blendFast`, `blendSlow`, `iCombatStance`, `fMinSpeed`, the 22 foot-IK gains |

The first three rows are the engine's side of `docs/animation-variables.md` §2–3,
and their initial values are uniform: everything zero except `bEquipOK` (1 in 48
of 55 files), `iSyncTurnState` (1) and `IsAttackReady` (1 in 23). The blend
durations are the graph's own convention and worth keeping: `blendDefault` 0.2,
`blendFast` 0.1, `blendSlow` 0.5 in the great majority.

The events the graphs *declare* follow the same shape -- a core the engine sends
or a clip fires in every creature, then a long per-family tail:

| declared by | events |
| ---: | --- |
| 46 | `moveStart`, `moveStop`, `staggerStop` |
| 45 | `staggerStart`, `weaponDraw`, `GetUpStart`, `GetUpBegin`, `GetUpEnd`, `Ragdoll`, `Reanimated`, `AddCharacterControllerToWorld`, `RemoveCharacterControllerFromWorld` |
| 42–44 | `weaponSheathe`, `recoilStart`, `recoilStop`, `turnLeft`, `turnRight`, `turnStop`, `attackStop`, `preHitFrame`, `HitFrame`, `weaponSwing`, `DeathAnimation`, `AddRagdollToWorld`, `Getup` |
| 30–40 | `SoundPlay`, `PickNewIdle`, `RagdollInstant`, `PairEnd`, `deathStart`, `returnToDefault`, `KillMoveStart/End`, `KillActor`, `2_*` mirrors for paired victims |
| 20–29 | `recoilLargeStart`, `bleedOutStart/Stop`, `HeadTrackingOn/Off`, `IdleStop`, `aggroWarningStart/Stop`, `combatStanceStart/Stop`, `SprintStart/Stop`, `swimStart/Stop`, `weapEquip/Unequip`, `FootLeft/Right/Front/Back` |
| 16 | `cannedTurnLeft90`, `cannedTurnLeft180`, `cannedTurnRight90`, `cannedTurnRight180`, `cannedTurnStop`, `cannedTurnMove`, `clipEnd` |

Which of them the engine originates is in `docs/animation-events.md`; the point
here is only that **a generated creature declaring this core and nothing else has
every hook the game will ever pull**, and adds names only for its own modules.

### 2.4 The sync-mode machines

55 machines in 31 creatures start in `START_STATE_MODE_SYNC`, reading their
start state from a variable and writing their current state back into it
(`docs/animation-variables.md` §2.1 for the mechanism and the engine's part in
it). Read against the engine's list, the creatures' sync variables split cleanly:

| variable | creatures | machines | what the ids mean | who else writes it |
| --- | ---: | ---: | --- | --- |
| `iSyncDefaultState` | 13 | 14 | the base machine: 0 non-combat, 1 combat (or equip), then unequip, stagger, kill-move, aggro, ambush | the machine alone in 11; an expression in the riekling and the vampire brute |
| `currentDefaultState` | 8 | 8 | the same machine under the `MT_State` / `Weap_*_State` naming, ids up to 999 and 1000 for kill-move and summon on the draugr | an expression in 4 (`ReanimateSetCurrentDefaultState`, resetting it on a get-up: draugr ×2, falmer, troll) |
| `iCombatStance` | 6 | 13 | the chaurus family's idle, turn and default machines, 0 non-combat, 1 melee, 2 spit | the draw/sheathe expressions in 3 |
| `iAttackState` | 2 | 6 | the vampire lord's and werewolf's combat/non-combat pairs | expressions |
| `iSyncIdleLocomotion` | 2 | 2 | 0 standing, 1 moving (falmer, riekling) | the engine, at bind time |
| `iSyncSprintState` | 1 | 4 | the horse's locomotion, jump, fall and land: 0 plain, 1 sprint | the engine, every frame |
| `iState` | 1 | 2 | the benthic lurker's walk/run machines, ids 0 and 1 | see below |
| `currentState`, `iSyncWard`, `iCamera_Sync`, `iRightHandType` | 1 each | | the dragon priest, the vampire lord's camera, the wisp's idle by hand type | |

Only the last four rows touch anything the engine writes, and they use it as
the doc says: `iSyncSprintState` and `iSyncIdleLocomotion` with the engine's
ids. Everything above them is the graph's own bookkeeping, and the pattern is
one machine per creature holding the combat mode so that a stagger or a get-up
returns it to the right stance. A generated creature needs exactly one such
variable, initial value 0, on its base machine.

The benthic lurker is the one to copy with care: its walk and run compasses are
states 0 and 1 of a machine synced on **`iState` itself**, and its movement
types are `iState_BenthicLurkerDefault = 0` and `iState_BenthicLurkerCombatRun
= 1`. The machine's state id *is* the movement type, so the sync write is the
`iState` write. It works because the ids were chosen to match; it is not a
fourth way to set `iState` and should not be copied without the constants.

**Character properties are a quadruped thing.** Only the shared quadruped file
(and its boar and scrib copies) uses `hkbCharacterData` properties -- `IsBear`,
`IsCanine`, `IsDeer`... and the `FootIKDisable_*` bone-weight arrays -- because
one graph has to know which of ten bodies it is animating. A single-creature graph
never needs one.

## 3. The behaviour machine: situations, not animations

Two levels below the root sits the machine that the engine's events actually
address. Its state names are the census's clearest signal of what a creature
*does*. Counted over the 46 (a state is counted where it appears at this level or
one below, since the families nest differently):

| situation | creatures | entered by | left by |
| --- | ---: | --- | --- |
| default (`DefaultState`, `MT_State`, `NonCombatState`, `*BaseState`) | 46 | start | -- |
| stagger | 44 | `staggerStart` wildcard | `staggerStop` fired by the clip |
| recoil / recoil large | 39 | `recoilStart`, `recoilLargeStart` | `recoilStop` |
| attacking (own state, or nested in the combat stance) | 42 | `attackStart*` | `attackStop` fired by the clip; `recoilStart` mid-way |
| combat stance or combat idle (`CombatState`, `Weap_Readied_State`, `CombatReadyState`, `CombatIdleState`) | 41 | `weapEquip`, `combatStanceStart` | `weapUnequip`, `combatStanceStop` |
| equip / unequip transition states | 21 | the same | `weapEquipOut` / `weapUnequipOut` fired by the clip |
| paired kill-move (`BSSynchronizedClipGenerator`) | 23 | `KillMove*`, `2_KillMove*` | `PairEnd`, `pairedStop` |
| aggro warning | 24 | `aggroWarningStart` | `aggroWarningStop`, `returnToDefault` |
| bleed-out | 11 (20 declare `IsBleedingOut`) | `bleedOutStart` | `bleedOutStop` |
| animated death (`DeathState`) besides the ragdoll shell | 10 | `deathStart` | `deathStop`, then `Ragdoll` |
| swim | 12 | `swimStart` | `swimStop` |
| sprint | 4 | `SprintStart` | `SprintStop` |
| trap / ambush / pod / sarcophagus / statue / porthole (a place to wait in) | 29 | `idle*EnterInstant`, `Trap*` | `TrapExitEnd`, `IdleStop` |
| summon / dispel / initialise (an entrance) | 10 | `summonStart`, start state | `summonStop`, `InitiateEnd` |
| furniture | 7 | `IdleFurniture*` | `idleChairGetUp`, `forceFurnExit` |
| casting / spit / ranged / shout | 18 | `MLh_*`, `spitStanceStart`, `attackStart_Spit` | `MLh_SpellFire_Event`, `Spell_Stop` |
| bash / block | 14 / 6 | `bashStart`, `blockStart` | `bashStop`, `blockStop` |

Everything from "paired kill-move" down is a module: present or absent whole, and
absent from the smallest creatures. The chicken has default, lay-down and aggro
warning; the hare adds feeding; the witchlight has default, stagger, recoil and one
attack. That is the floor.

### 3.1 How a situation state is built

The same three lines, forty times over. An attack state, from the chaurus:

    S1 'AttackState'
     on recoilStart -> S2
     exit: attackStop
     SM 'AttackBehavior'                         one state per attack animation
      S0 'Attack_RightChop'
       MODGEN
        mod BSIsActiveModifier [bIsActive0<-bAllowRotation]     -- or bAnimationDriven
        CLIP 'Attack_RightChop' = Animations\AttackRChop.HKX mode=0

- **the state exits on the clip's own stop event**, and also *raises* it on exit
  (`exit: attackStop`) so the engine's handler runs whichever way the state was
  left. Stagger, recoil, bash, block, equip and idle states all do this with
  their own `*Stop`;
- **a `BSIsActiveModifier` says what the engine may do meanwhile**: 120 attack
  states raise `bAllowRotation` (the AI may still turn the actor) or
  `bAnimationDriven` (the clip's root motion moves the actor); 65 recoil and 42
  stagger states raise `bAnimationDriven`; idle states raise it too, or
  `bHeadTrackingOn`;
- **the clip is `mode` 0** (play once). 2081 of 4299 creature clips are once,
  2184 loop; the loops are idles and locomotion, the onces are everything
  situational.

The per-attack machine is where a creature with N attack animations gets N
states; nothing else scales with the attack count. The recoil state is the same
shape with one clip per attack that can be interrupted, or a single recoil clip.

### 3.2 What an attack clip fires

Across the creatures' attack clips, the trigger sets in use:

| clips | triggers |
| ---: | --- |
| 105 | `attackStop` only |
| 92 + 38 | `returnToDefault` / `ReturnToDefault` only (the quadruped file's spelling) |
| 32 + 8 | the full humanoid set: `preHitFrame`, `weaponSwing`, `HitFrame`, `InitiateStart*`, `InitiateMove`, `InitiateEnd`, `AttackStop`, `SoundPlay.*` (+`WinAttackStart`) |
| 19 | `recoilStop` (the attack's own recoil half) |
| 10 | `attackStop` + `slowdownStart` |

So the game's melee handlers (`HitFrame` for the hit, `preHitFrame` for
anticipation, `weaponSwing` for the swing sound and stamina) are **optional on a
creature and present on 42 of 46 as declared names**; the one thing a clip must do
is end its state. An attack without `HitFrame` lands nothing
(`docs/animation-variables.md` §5) -- but many shipped creature attacks have
theirs on the animation's annotation track rather than in a trigger, which is
why the cache's event list is derived from both (`README.md`).

### 3.3 Attack event names

The start event is whatever the race record's attack data names, and the graphs
spell it two ways: `attackStart_<Name>` / `attackPowerStart_<Name>` (32
creatures, the quadruped family, the atronachs, the giant, the spriggan...) and
`attackStart<Name>` with no separator (draugr, falmer, troll, steam and sphere
centurions, ballista, dragon, netch). The cache's set data derives from the
first form (`docs/animation-set-data.md` §5.6: an attack is the nested state its
start event names, and its clips are the generators under it), so a generated
creature should use `attackStart_` and `attackPowerStart_` and put each attack's
clips in a state of their own.

### 3.4 Being killed: the victim half of a paired kill-move

23 creatures can be killed by a humanoid's kill-move, and the state that does it
is the same in all 23 (277 victim-side generators counted; the bear, wolf,
draugr, falmer, giant and dragon read in full):

    S 'KillMoveState'                  entered by a wildcard on KillMoveBearA, KillMove2HMBearB ...
     BSSynchronizedClipGenerator 'KillMoveBearA'
        m_SyncAnimPrefix     "2_"
        m_bLeadCharacter     true
        m_bReorientSupportChar true
        m_bApplyMotionFromRoot false
        m_sAnimationBindingIndex -1
       CLIP = ..\SharedKillMoves\Human&Bear\Paired_1HMKillMoveBearA.hkx
         triggers: 2_KillMoveStart, 2_SoundPlay.*, 2_DeathEmote, 2_KillMoveEnd, 2_PairEnd, 2_KillActor (, 2_pairedStop)

- **the animation is the same file the human plays**, listed in both character
  files (`docs/paired-animations.md`); the victim's generator names its half by
  the `2_` prefix, which is the prefix of its bones' tracks in that file;
- **the event that enters the state is the kill-move's name without `pa_`.** The
  human's graph enters its half on `pa_KillMoveBearA`, the bear's on
  `KillMoveBearA`, on every one of the 277 pairs read. The `pa_` form is the
  paired idle's own event; the partner is addressed by the bare name. The horse
  and the dragon, where the creature is the one the mount idle is played on,
  listen for the `pa_` form themselves;
- **the victim's triggers are all `2_`-prefixed** and the animation cache
  restates them as such: `2_KillActor` is what kills the creature
  (`docs/animation-events.md` §3, `KillActor` is "the paired-kill end for the
  victim"), `2_DeathEmote`, `2_KillMoveStart` / `End`, `2_PairEnd`. The response
  file maps none of the `2_` names, so the engine strips the sync prefix when a
  synchronised clip fires -- the human's half carries the same events prefixed
  `NPC` (`NPCKillMoveStart`, `NPCHitFrame`, `NPCweaponSwing`, `NPCpairedStop`)
  and the humanoid graph fires those unprefixed nowhere else either;
- **the flags are the mirror of the human's**: the creature's generator is
  `lead = true`, the human's `lead = false`; both `reorient = true`; root motion
  from the paired root only on the mounts and the dragon's bite grapple.

So a generated creature that should be killable by the stock kill-moves needs
one such state per shared animation whose `2_` half fits its rig, entered by the
bare kill-move name, declaring the `2_` event names its triggers use. Whether a
kill-move is chosen at all is the killer's side's business -- the paired idle's
conditions and the victim's set data -- and is not measured here.

## 4. The five locomotion plans

The default situation is a standing/moving pair in every creature, and the moving
half is one of five constructions. Counted by node shape, not by name:

| plan | creatures | moving generator | standing machine |
| --- | ---: | --- | --- |
| **A. compass biped** | 14: draugr ×2, falmer, troll, giant, benthic lurker, vampire brute, hagraven, spriggan, steam / sphere / ballista centurion, vampire lord, werewolf (+ HMDaedra without the cyclic flag) | `BSCyclicBlendTransitionGenerator` over an 8-arm cyclic parametric blend; each arm a speed ladder | idle + `TurnLeft`/`TurnRight` blends on `TurnDelta` |
| **B. four-arm compass** | 12: chaurus, mudcrab, frostbite and dwarven spiders, chaurus flyer, ice wraith, wisp, riekling, dragon priest, the three atronachs | the same over 4 arms (0, 0.25, 0.5, 0.75) | idle + looping turn clips |
| **C. quadruped** | 13: the ten sharing the file, boar, scrib, horse | a speed ladder whose rungs are turn tri-blends on `TurnDeltaDamped`; backward is a separate state | idle + looping turn clips |
| **D. forward only** | 4: chicken, hare, netch, witchlight | one speed ladder (chicken), one clip with a speed multiplier (witchlight, netch) | idle + looping turn clip |
| **E. swimmer / flyer** | 3: slaughterfish, dragon, (horse swim, horker swim, werewolf) | slaughterfish: 6-arm blend bound to `Direction` directly; dragon: `docs/flight.md` | -- |

### 4.1 The compass (A and B)

    S1 'LocomotionState'           on moveStop -> Standing
     BSCyclicBlendTransitionGenerator
      BLEND 'Direction_Blend' flags=49 n=8            weights 0, .125, .25 ... .875
        BLEND 'ForwardBlend'      flags=17 [blendParameter<-SpeedSampled]
          CLIP WalkForward   (weight 5)  = the walk clip again, the ladder's floor
          CLIP WalkForward   (weight w)  = Animations\WalkForward
          CLIP RunForward    (weight r)  = Animations\RunForward
        BLEND 'ForwardRightBlend' ...
        ... eight arms: F, FR, R, BR, B, BL, L, FL

- 41 eight-arm and 29 four-arm compasses in the creatures, all `flags=49`
  (`SYNC | PARAMETRIC | CYCLIC`) and **all with no binding**: the engine writes
  the heading into `m_blendParameter` itself, so the wrapper is what tells the
  engine this blend is the compass (`docs/speed-data-research.md` §5). The
  wrapper is the marker; a generated compass must have it;
- **every arm is a parametric speed ladder** (`flags=17`) whose first rung is the
  walk clip at 5 -- the floor below which nothing plays faster than walking --
  and whose upper rungs are the walk and run clips at the movement type's
  speeds (`docs/speed-data.md`). Three rungs where a run exists (walk, walk, run),
  two where it does not (the draugr's backwards, the bow-drawn set, the block
  set). The rung values are the only numbers in the plan that come from outside
  the graph;
- **the clips loop** (`mode` 1), and the sync flag keeps the eight arms in phase
  so the heading can change mid-stride;
- a walk/run **gait split** is a second construction on top, not part of the
  compass: two states each holding its own compass, switched by an expression
  modifier on the sampled speed (`runStart if (SpeedSampled > 100); walkStart if
  (SpeedSampled < 130)` on the falmer; 190/150 on the giant), which also writes
  `iState` for the speed table. Ten creatures do it (falmer, giant, benthic
  lurker, and the quadruped file's `Speed > 420` split inherited by bear, deer,
  dog, sabre cat, wolf, boar and scrib); the rest have one compass and let the
  ladder cover the range.

A four-arm compass is the same with the diagonals missing: an animation set of
four walks (F, R, B, L) is enough, and the engine's blend fills 45° headings
from the neighbours. Its arms are speed ladders on `SpeedSampled` like the
eight-arm ones, and the speed table holds a block for every creature -- the
eight without a sampler included -- so the plan changes nothing on the table's
side. One creature does it differently: the dwarven spider has no ladders and
binds each arm's clip `playbackSpeed` to a `speedMult<Heading>` expression,
`max(5, SpeedSampled) / speed<Heading>`; the chaurus, whose graph it was copied
from, computes the same four multipliers and binds none of them.

### 4.2 The quadruped (C)

    S0 'ForwardLocomotionState'   on moveBackward -> Backward
     mod hkbDampingModifier [rawValue<-TurnDelta, dampedValue<-TurnDeltaDamped]
     SM  ForwardWalkState / ForwardRunState        split by runStart / walkStart (Speed > 420)
      BLEND flags=17 [blendParameter<-SpeedSampled] n=6
        BLEND 'WalkSlowBlend' flags=17 [blendParameter<-TurnDeltaDamped]  weights +75, 0, -75
          CLIP WalkForwardL / WalkForward / WalkForwardR
        BLEND 'WalkBlend'     ...  +75  0 -75
        BLEND 'WalkFastBlend' ...
        BLEND 'TrotSlowBlend' ...  +112.5 0 -112.5
        BLEND 'TrotBlend'
        BLEND 'TrotFastBlend'
    S1 'BackwardLocomotionState'  on moveForward -> Forward
     CLIP WalkBackward  (looped, rate from walkBackSpeedMult = clamp(Speed/walkBackRate, ...))

The quadruped does not strafe: it has forward gaits and it turns while moving.
Each gait is a left/centre/right tri-blend on the damped turn delta, and the
authority grows with the gait -- walk ±75, trot ±112.5, run ±270 on the bear
and the boar; ±135 on the deer's trot; the horse ±135 on all three. The
speed ladder over the gaits has six rungs because each gait is listed three
times (slow, normal, fast) with the same clips -- the ladder's way of shaping
the rate curve, not three animations. The animations a gait needs are therefore
**three clips per gait** (left, straight, right) plus one backward walk, and a
creature with only straight clips can still be built by using the straight clip
in all three slots.

### 4.3 Standing, and turning in place

The standing state is a three-state machine in every plan:

    SM 'StandingBehavior' start=Idle
     S 'TurnRight'   on turnLeft -> TurnLeft, on turnStop -> Idle
     S 'Idle'        on turnLeft -> TurnLeft, on turnRight -> TurnRight
     S 'TurnLeft'    on turnRight -> TurnRight, on turnStop -> Idle

with `moveStart` on the state above it going to the locomotion state and
`moveStop` coming back. The turn states are filled two ways: a **looping turn
clip** (`TurnLoopingL`, mirrored for the right; on the falmer and the chaurus
scaled by an expression `turnSpeedMult = fabs(TurnDelta/90)` -- 33 creatures)
or a **parametric blend on `TurnDelta`** of the same 90° turn clip at weights 5,
90, 135 (and -5, -90, -135 for the right -- 12 creatures, the compass bipeds,
20 + 18 blends). The blend form needs one clip per side; the loop form needs one
clip and a mirror.

**Canned turns** are a separate state beside standing and locomotion in 27
creatures: `cannedTurnLeft90/180`, `cannedTurnRight90/180` (and the `Flee`
variants on prey) each play a once clip of that turn, with `cannedTurnStop` and
`cannedTurnMove` returning to standing or locomotion by `iSyncIdleLocomotion`
(the quadruped) or by `SpeedSampled >= 5` (the draugr). The compass bipeds name
theirs `turnLeft180` / `turnRight180` and return on `TurnInPlaceEnd`. Four clips,
or two and their mirrors.

### 4.4 The combat stance

33 creatures double the plan under a readied state: `weapEquip` (or
`combatStanceStart`) goes through an equip state whose clip fires `weapEquipOut`,
into a readied state that holds *its own* standing machine (combat idle, combat
turns), its own compass or ladder, and the attack states of §3.1; `weapUnequip`
goes back through the unequip clip. The hand-to-hand draugr, the troll, the giant
and the vampire brute are this exact shape; the draugr repeats it per weapon
type under a selector on `iRightHandType`. What it costs in animations is a
second idle, a second locomotion set and the two transition clips; the twelve
creatures that skip it (chicken, hare, chaurus family, spiders, mudcrab, wisp,
witchlight) put their attacks directly beside the default state and use a
`combatStanceStart` machine in sync mode on `iCombatStance` to swap the idle
only.

The stance is also what writes `iState`: most creatures set it with an expression
in the readied and default states' modifier lists (`iState = iState_DraugrH2H`;
22 of the 49 projects write it *only* this way), and 8 tag the subtree with a
`BSiStateTaggingGenerator` instead (ballista, falmer, horse, netch, sphere,
spriggan, vampire lord, werewolf -- `docs/speed-data.md` §4.5 for why both).

## 5. Hit reactions and the idle loop

**Stagger** is a parametric blend on `staggerMagnitude` of two or three clips:
small / large at 0 and 1 (45 blends, or at 0.3 and 1 on the quadruped file, 11),
or light / medium / heavy at 0, 0.5, 1 (47). The clip fires `staggerStop`; the
state exits on it. A `staggerDirection`-selected forward stagger is a separate
state in 13.

**Recoil** is one clip per attack (draugr, giant) or one clip (most), reached
from inside the attack state by `recoilStart` and from the default state by
`recoilLargeStart`; `recoilStop` ends it.

**Bleed-out** (20) is enter / idle / exit states under `bleedOutStart`, raising
`IsBleedingOut`.

**Death** has two shapes: the shell's `AnimateToRagdoll` playing one death clip
that fires `Ragdoll` (36), and, on the quadruped family, an extra `DeathState`
under `deathStart` for a directional death before the ragdoll (12).

**Idle** is one machine repeated: a main idle loop, either wrapped in a
`BSEventEveryNEventsModifier` that fires `00NextClip` every so many loops (16
creatures) or waiting for the engine's idle events, an `IdleState` holding a
**random-start machine** (`startStateMode` 2 -- 78 such machines across 25
creatures) of once clips, returning on `idleStop` / `IdleStop`, and
`PickNewIdle` raised on exit so the engine schedules the next one (declared by
37).

**`IdleStop` and `idleStop` are one event spelled two ways, and no creature uses
both.** 23 creatures declare the capital form (the quadruped file, the draugr,
giant, horse, netch, riekling, spriggan...), 17 the lower one (the atronachs,
chaurus, chicken, hare, mudcrab, falmer, hagraven, troll, the centurions, the
vampire lord, the werewolf), six neither. Within a project every idle state
exits on the declared spelling and every once idle clip fires that same
spelling as its last trigger, so the once idles are self-contained. The states
that exit on it with **no clip firing it** are the looping ones -- the
quadrupeds' sitting, laying and standing full-body idles, the bear's and boar's
lay loop, the chicken's and hare's lay-down, the riekling's prayer -- and those
can only be ended from outside, by the engine's `IdleStop` idle. The chicken's
and the hare's loops exit on the *lower-case* name and the game does stop them,
and the executable says why: a name sent to a graph is interned through the
case-insensitive string pool and matched to the graph's event by pointer
(`docs/animation-events.md` §1), so the two spellings are one event. A generated
creature may use either; `IdleStop` is the form 23 of the 40 use and the one the
idle record sends. Feeding, laying, sitting and sleeping are the same machine again behind
their own `idle*Start` events. A creature with one idle animation has a
one-state random machine; every additional idle animation is one more state.

## 6. What the plan consumes: animation slots

Reading the four plans against the character files gives the slot table an FBX
set has to be matched against. "Same clip" means the shipped creatures reuse one
animation in that slot; "mirror" means a `[Mirrored]` clip of the other side.

| slot | A compass | B four-arm | C quadruped | D forward | fired events |
| --- | --- | --- | --- | --- | --- |
| idle loop | 1 | 1 | 1 | 1 | -- |
| walk | 8 (F FR R BR B BL L FL) | 4 (F R B L) | 3 (L F R) + back | 1 | `FootLeft/Right` optional |
| run | 8 (or fewer: back arms may stop at walk) | 4 or same as walk | 3 per extra gait | 1 or same | |
| turn in place | 1 per side, or 1 + mirror | same | same | same | |
| canned turns | 2 per side or 2 + mirrors | -- | 2 per side (+ flee) | 2 + mirrors | `cannedTurnStop` / `TurnInPlaceEnd` |
| combat idle + combat walk/run | second set, optional | swap idle only | swap idle only | -- | |
| equip / unequip | 1 + 1, optional | -- | -- | -- | `weapEquipOut`, `weapUnequipOut` |
| attack | 1 per attack | 1 per attack | 1 per attack (+ power, side, lunge) | 0 | `attackStop`, `HitFrame`, `preHitFrame`, `weaponSwing` |
| recoil | 1, or 1 per attack | 1 | 1 | -- | `recoilStop` |
| stagger | 2–3 | 2 | 2 | 0–2 | `staggerStop` |
| death | 0–1 | 0–1 | 0–2 | 0 | `Ragdoll` |
| get up | 1–3 | 1–2 | 2 | 1–2 | `GetUpEnd`, `AddCharacterControllerToWorld` |
| idles | any | any | any | any | `idleStop` |
| bleed-out, aggro, swim, sprint, bash, block, ambush, kill-move | module | module | module | -- | per module |

The floor is the witchlight: idle, one walk, one attack, one recoil, three
staggers -- seven animations, eleven clips, six machines, and no ragdoll. The
chicken shows the next rung with twenty: idle, walk, run, a looping turn and its
mirror, four canned turns, two get-ups, and nine idles, and it does everything
the AI asks of a non-combatant.

## 7. What this says about a generator

- **One template, five locomotion plans, N modules.** The shell of §2, the
  situation machine of §3 and the idle loop of §5 are the same nodes in every
  creature; a generator writes them once. The plan is a choice made from the
  animation set: eight directional walks give A, four give B, a left/right pair
  of forward gaits gives C, a single forward clip gives D. Each module is
  present iff its slot is filled;
- **the input is a role for each animation**, not a name: the shipped names are
  inconsistent (`MTForward`, `WalkForward`, `Forward_Walk`, `RunF`,
  `forwardWalk`) and the graph binds a clip to an animation by path and the cache
  by list position (`README.md`), so the FBX side needs a mapping of file → slot
  and nothing else. Tokens that recover the role from the shipped names
  (`idle`, `forward`, `left`, `right`, `back`, `walk`, `run`, `turn`, `attack`,
  `power`, `stagger`, `recoil`, `getup`, `death`) cover 45 of 46 projects, so a
  guessed default mapping is feasible with a review step;
- **the numbers that are not in the graph** are the ladder rungs (the movement
  type's walk and run speeds per heading), the turn authority per gait, the gait
  split thresholds, and the `iState` ids -- exactly the speed-data inputs
  `docs/speed-data.md` already names, and the generator there already writes the
  table from them. Every other constant (blend durations, stagger weights, the
  compass positions, the 5 floor) is a convention this document records;
- **the events are fixed by the engine and the race record**, not by the graph:
  the core of §2.3 plus `attackStart_<name>` per attack. The graph may invent
  events only for its own transitions and the triggers that end its states;
- **the caches follow**: the character file's animation list is the set data's
  set (`docs/animation-set-data.md` §3.1) and the cache index of every clip; the
  attacks are the nested states under the start events; the speed table is
  generated from the graph's `iState` keys. All three already exist as
  generators in this repository, so the behaviour is the missing piece, and the
  one whose shape this census fixes.

## 8. Open

Four questions were left open by the first pass and are answered above from the
corpus: the sync-mode machines (§2.4), the four-arm creatures against the speed
table (§4.1), the two spellings of the idle stop (§5) and the victim half of a
kill-move (§3.4). What remains is what the corpus cannot say:

- **event-name case** is settled: the executable interns every name through a
  case-insensitive pool and matches events and variables by pointer
  (`docs/animation-events.md` §1, `docs/reverse-engineering.md` §4);
- **the `pa_` convention.** §3.4 reads it off 277 pairs: the initiator's graph
  listens for `pa_<name>`, the partner's for `<name>`. The executable tests the
  prefix with `_strnicmp` in the player camera's action handling, which is a
  reader of the convention and not the code that addresses the partner. Which
  actor the engine treats as initiator for a given paired idle is a record and
  engine question still, and the horse and dragon rows show it is not always
  the humanoid;
- **the sync prefix at dispatch.** §3.4 infers that the engine strips `2_` (and
  `NPC`) from a synchronised clip's events before routing them, since no
  response line names a prefixed event and the victim demonstrably dies on
  `2_KillActor`. The code that does it has not been located.
