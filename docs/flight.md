# Flight: what the engine does, what the dragon's graph does, and what can be changed

Two questions drove this: can a new flying creature have a flight pattern of its own,
and can the player character be given a flight state. The answer to the first is yes,
within a contract this document spells out; the answer to the second is that the
engine's flight is generic per actor but is *driven* only by AI, so the player can be
put into the fly state and will fly, but nothing turns the player's input into
flight. The evidence is the dragon's behaviour graph (`dragonbehavior.hkx`, read
through HKX2), the masters (idles, actions, races, movement types, read through
Mutagen) and the executable (addresses are the AE build; `docs/animation-events.md`
§1 has the layout).

## 1. The engine's side of flight

**The fly state** is bits 18–20 of the actor's first state word (`Actor+0xC8`): 0
none, 1 take-off, 2 cruising, 3 hovering, 4 landing, 5 perching, 6 action. One
setter writes it, `0x14069be40(actor, state)`, and it does four more things:

- it puts the character controller into Havok's **flying state** (wanted state 4 at
  `controller+0x21C`, mirrored at `+0x200`) when the new state is anything but none
  or perching, and back to 0 otherwise. `bhkCharacterStateFlying` is one of the
  controller's standard states beside on-ground, in-air, jumping, climbing and
  swimming; its simulate step is empty (`0x140f01ee0`, 16 bytes), so a flying
  controller gets no gravity and no ground: it goes exactly where its velocity says;
- it tells the movement controller (`Actor+0x150`, virtual 17 at `0x140783ca0`) that
  the actor is or is not flying, which is what selects `MovementAgentPathFollowerFlight`
  and `IMovementQueryFlight` over the ground path follower;
- it moves the actor, and its rider unless that is the player, between two per-cell
  actor lists (`0x14030af40` / `0x14030aca0`), gated by cell flag bit 13;
- on take-off (state 1) it copies the controller's pitch into the actor's rotation,
  and on leaving flight it resolves a kill deferred while airborne (`0x1406b9ed0`,
  the same call FlightLandEnd makes).

Nothing in the setter checks the race. The handlers of `docs/animation-events.md` §8
are one set of callers; the engine's own are the procedures and the path follower
(§1.3 below), a save-game restore (`0x140656f30`), and three wrappers that pair a
state change with a request to the graph (§1.4).

**The movement type.** `0x140694250(actor)` returns the actor's movement mode for the
default-object table: 3 for any fly state other than none and perching, else 2
swimming, 4 sneaking, 1 running, 5 sprinting, 0 walking. Those are the six
`Default MovementType: Walk/Run/Swim/Fly/Sneak/Sprint` default objects. The dragon
races carry no movement types of their own; the graph names them through `iState`
(`docs/speed-data.md` §4.5): `iState_DragonFlying` selects the `MOVT` whose name is
`DragonFlying` (forward walk 2000, run 7400, rotate 90°/s), `iState_DragonHovering`
one that only rotates (45°/s), `iState_DragonPerching` one that does nothing. The
engine writes the requested `Speed` from that movement type as for any actor.

**Who may fly.** `0x140661120(actor)` is the race flag `Flies` (race flags bit 7),
and only the dragon races carry it: `DragonRace`, `DragonBlackRace`, `AlduinRace`,
`UndeadDragonRace`, `DLC1UndeadDragonRace`, `DLC2DragonBlackRace`. It has 28 callers,
all AI, combat, player-mount and camera code. `0x140661190(actor)` is the fuller
gate the AI asks before a take-off: race `Flies`, the graph's `Injured` false, state
word 2 bit 12 (allowed to fly, set by the Papyrus and console `SetAllowFlying` at
`0x1409e9e40`, and *set by default* in the actor constructor at `0x14069cb7b`), bit
9 clear (flight blocked, set by the AI around landings and grabs), and Health above
zero. A third check, `0x140664240(actor, cell)`, asks whether the actor may fly in
this cell; it is the console's `CanFlyHere` and the player-mount code's question.
The 109 readers of the fly-state bits are almost all AI, combat, pathing and
player-mount code; none of them refuses a non-dragon.

**The flight AI.** Three package procedures, `Hover`, `Orbit` and `FlightGrab`
(`BGSProcedure*` with exec states), a combat behaviour tree `CombatBehaviorTreeFlight`
whose nodes are named `Takeoff`, `Hover`, `Orbit`, `Orbit Distant`, `Dive Bomb`,
`Flying Attack`, `Ground Attack`, `Perch Attack`, `Land`, `Land Nearby`, `Land Far`,
`Wait to Land`, `Wait for Cruise`, `Crash Land Sequence`, `Leave Perch`, and six
pathing requests, `PathingRequestFly`, `FlyTakeOff`, `FlyHover`, `FlyLand`,
`FlyOrbit`, `FlyAction`, each with its own path builder under `PathBuilderFlight`.
The path follower is `MovementAgentPathFollowerFlight`. The game settings that shape
it are `fCombatFlightEffectiveDistance`, `fCombatFlightMinimumRange`,
`fCombatFlyingAttackChanceMin/Max`, `fCombatFlyingAttackTargetDistanceThreshold`,
`fFlyingActorDefaultTurningSpeed`, `fHostileFlyingActorExteriorDistance`,
`fMoveFlyWalkMult`, `fMoveFlyRunMult`. This whole layer decides *where* the actor
flies; it never names an animation.

**Guidance, avoidance and attacking from the air.** The six pathing requests each
have a builder registered under `PathBuilderFlight` (`FlyPath`, `FlyTakeOffPath`,
`FlyHoverPath`, `FlyLandPath`, `FlyOrbitPath`, `FlyActionPath`), plain functions of
300 to 1,300 lines (`0x1404f38f0`, `0x1404f8110`, `0x1404f5420`, `0x1404f7430`,
`0x1404f6260`, `0x1404f3e50`) over one shared helper (`0x1404f8840`). Traced three
calls deep, they build their paths from arrays of points with the pathing-space
utilities and reach **no raycast, no line-of-sight grid and no navmesh search**,
with one exception: the landing builder runs a navmesh fit-sphere search
(`NavMeshSearchFitSphere`, `0x1404e2ee0`) to choose a landing spot. There is no
flight counterpart of the ground path's `GroundPathRayValidator` and
`GroundPathPathingNodeGenerator`, or of the water path's, and the avoidance agents,
`Avoid Box`, `Avoid Player`, `AvoidThreat`, `MovementPathManagerAgentStaticAvoider`,
belong to the ground path manager. A flying dragon's guidance is therefore
geometric: a path shaped for take-off, cruise, orbit, hover, action or landing,
followed by `MovementAgentPathFollowerFlight`, which turns the path into
`TargetSpeed`, `Pitch` and `TurnDelta` and clamps the turn by
`fFlyingActorDefaultTurningSpeed` (`0x140673210`). Nothing steers it round a tower;
the fly-state setter takes it out of the cell's actor lists, and the world is not in
its way because the paths are planned above it and the landing is planned on the
navmesh. Whether the builders sample the terrain height along the path was not
read; they share no call with the water-height queries the swim update uses.

Attacking from flight is the combat tree's, `CombatBehaviorTreeFlight`
(`0x140897d20`), and it has the shape of a dragon fight: `Takeoff`, `Wait for
Cruise`, `Orbit` and `Orbit Distant` around the target, `Hover`, `Flying Attack`,
`Dive Bomb`, `Perch Attack`, `Ground Attack` after a `Land` chosen by `Land
Selector` between `Land Nearby` and `Land Far`, `Wait to Land`, `Leave Perch`, and
a `Crash Land Sequence`. Its choices are the settings `fCombatFlyingAttackChanceMin`
and `Max` (`0x1408dc990`), `fCombatFlyingAttackTargetDistanceThreshold`
(`0x140897550`), `fCombatDiveBombChanceMin` and `Max` (`0x1408dc810`),
`fCombatFlightEffectiveDistance` (`0x1408dd350`) and `fCombatFlightMinimumRange`
(`0x1408dd240`). The approach is a combat path request of its own,
`CombatPathRequestFlyingAttack` with destination *none*, that is the target itself,
beside `CombatPathRequestFlight` to a location or a reference,
`CombatPathRequestHover` and `CombatPathRequestOrbit`; the node
`CombatBehaviorFlyingAttack` runs it. The dive is a `DiveBombSpeedController`
(`0x140894100`) with `fCombatDiveBombOffsetPercent` for where the dive aims and
`fCombatDiveBombSlowDownDistance` (`0x1408994c0`) for the pull-up. The breath
attack in the air is the graph's `BHR_Flight_Shout`, requested as a voice cast like
any other, and the snatch is the `FlightGrab` procedure with the graph's
`ST_Flight_Kill_Grab`. So: yes to guidance, in the sense of planned paths and a
follower; no to obstacle avoidance in flight; and yes to approaching and attacking
from the air, through a dedicated combat path request and a tree that decides
between a pass, a dive, a hover attack and a landing.

**Where the knobs live.** Three layers, none of them per actor except by
reference:

- **the combat style** (`CSTY`, its flight block): `HoverChance` and `HoverTime`,
  `DiveBombChance`, `GroundAttackChance` and `GroundAttackTime`, `PerchAttackChance`
  and `PerchAttackTime`, `FlyingAttackChance`. The tree reads them through the
  actor's style (`0x1406b5580`, fields `+0x80` dive, `+0x98` flying attack) and maps
  each chance into the range the game settings give (`0x1408dc070` with
  `fCombatDiveBombChanceMin/Max`, `fCombatFlyingAttackChanceMin/Max`). Every dragon
  NPC in the masters uses `csDragon` (hover 0.52 for 0.42 s, dive 0.36, ground
  attack 0.85 for 1 s, perch 0.5, flying attack 0.75) except Alduin
  (`AlduinCombatStyle`, no ground attack, dive 0.2), Odahviing in his quest
  (`MQ301OdahviingCombatStyle`, always a flying attack), the helgen dragon
  (`csDragonCharGen*`, no landing) and the test styles. So the fight is per actor
  through the style the NPC record names, and shared by every actor naming it.
- **the race**: `FlightRadius` (400 on every dragon race), read by the combat area
  code beside `fCombatAreaStandardFlyingRadiusMult` (`0x1407fa510`), and the
  `Flies` flag.
- **the game settings**, global to every flying actor: `fCombatFlightEffectiveDistance`
  and `fCombatFlightMinimumRange`, the chance ranges above, `fCombatDiveBombOffsetPercent`
  and `fCombatDiveBombSlowDownDistance`, `fHostileFlyingActorExteriorDistance`.
  `fFlyingActorDefaultTurningSpeed` is not one of them in practice: the turn-rate
  reader (`0x140673210`) first asks the actor's current movement type for its
  rotation speeds and returns the larger, and falls back to the setting only when
  the actor has no movement type.

The speeds and turn rates are therefore in the movement types the graph names, and
the wing model in the graph's own constants (§2): per behaviour file, shared by
every race on that project, and the dragon races share one.

What that means for a new creature: it cannot have its own engagement distances,
dive geometry or hostile radius, since those five settings are single values for
every flying actor in the game. It can have its own chances within the global
ranges (style), its own combat-area radius (race), its own speeds and turn rates
(movement types), and its own flight machine (graph). A different dive, in the
sense of a different pull-up distance or aim offset, needs a plugin that changes
the setting per actor, or a graph that ignores the engine's dive and animates its
own.

**How the AI reaches the graph.** The executable contains no `TakeOff`,
`FlyStartCruise`, `HoverStart` or `FlyStopDefault` string. The AI requests one of
five actions, `ActionFlyStart`, `ActionFlyStop`, `ActionHoverStart`,
`ActionHoverStop`, `ActionLand` (the `AACT` records behind the
`Action Fly Start` … default objects), and the **idle tree** turns the action into a
graph event by conditions:

    ActionFlyStart  -> FlyStartRootDragon
        GetFlyingState == 2  -> FlyStartCruise
        GetFlyingState == 3  -> FlyStartHover
        GetFlyingState == 5  -> TakeOff (FlyStartPerch) / TakeOff_Vertical
        otherwise            -> TakeOff / TakeOff_Vertical (FlyStartTakeOff)
    ActionFlyStop   -> FlyStopRootDragon
        GetIsCrashLandRequest -> FlyStopCrash
        GetIsHastyLandRequest -> FlyStopHasty / FlyStopHastyVertical
        GetIsInjured          -> FlyStopInjured / FlyStopInjuredSafe
        IsFurnitureAnimType   -> FlyStopPerchTower / Amp / RockL02 / TowerAttack / LakeDive, each with a Vertical form
        otherwise             -> FlyStopDefault / FlyStopDefaultSafe
    ActionHoverStart -> HoverStart / HoverStartSafe
    ActionHoverStop  -> HoverStop / HoverStopVertical
    path end while hovering (GetFlyingState == 3, GetPathingTargetAngleOffset) -> HoverTurnLeft90 / HoverTurnRight90

The three wrappers that do this are `0x1406919c0` (fly start: sets the fly state,
builds a `TESActionData` on the default-object action, asks the movement controller
for its flight interface, runs the action), `0x140691b40` (fly stop) and
`0x1406a4230` (hover). They are what the procedures, the combat tree and the player's
flying-mount code call. So the vocabulary a flying creature's graph must understand
is not fixed by the executable: it is fixed by the **idle records**, which a plugin
can add to for a new race with its own conditions.

**What the engine writes into the graph each frame.** Beside the standard channels
(`Speed`, `Direction`, `TurnDelta`, `DistToGoal`, the tween set, `bAnimationDriven`,
`bAllowRotation`) an Actor virtual at `0x14069dac0`, shared by Actor, Character and
PlayerCharacter, writes by name `TargetSpeed`, `Pitch`, `Roll`, `PitchDelta`,
`iWantBlock`, `bHeadTracking` and `TimeDelta`. These names are not in the 189-name
table of `docs/animation-variables.md`; they are set through their own literals. The
flight-specific engine-to-graph inputs are therefore `TargetSpeed`, `Pitch`, `Roll`
and `PitchDelta`, plus the tween set for take-off, landing and perching, and the
four landing names the table does hold: `Land`, `bCrashLand`, `LandTypeIndex`,
`PerchFireNode`.

**What the engine reads back.** The fly state itself, through the six handlers
`FlightTakeOff`, `FlightCruising`, `FlightHovering`, `FlightLanding`,
`FlightPerching`, `FlightLanded`, and `FlightAction`/`FlightActionEnd`; `iState` for
the movement type; `Injured`, `IsShouting`, `IsAttackReady`, `bVoiceReady`,
`IsBusy`, `LookAtOutOfRange`, `bSpeedSynced`, `HasTweenSpeed`, `bTweenUpdate`.

## 2. The dragon's side

`dragonbehavior.hkx` has 66 state machines, 214 clips, 153 variables and 482 events.
The part that is flight:

```
BHR_Default (start ST_Ground)
  ST_Ground   --TakeOff / TakeOff_Vertical-->  ST_TakeOff --TakeOff_to_WingState--> ST_Flight
  ST_Flight   [enter: FlightCruising]
              --HoverStart(Safe)--> ST_Hover [enter: FlightHovering] --HoverToCruise--> ST_Flight
              --FlyStop*--> ST_Land [enter: FlightLanding]
                              --LandEnd--> ST_Ground        --PerchLandEnd--> ST_Perch [enter: FlightPerching, idleChairSitting]
              --to_Flight_Kill_Grab_Enter--> ST_Flight_Kill_Grab [enter: FlightAction, exit: FlightActionEnd]
  ST_Perch    --TakeOff / TakeOff_Vertical--> ST_TakeOff (one launch clip per perch type)
  wildcard: FlyStartCruise -> ST_Flight, FlyStartHover -> ST_Hover
```

Every state-entry event on the left column of the response file is how the engine
learns the fly state: the graph declares it, the handler stores it. The engine never
sets 1 to 6 on its own except through those wrappers' explicit calls; the shipped
graph's enter events are the real source.

**The wing model is the graph's, not the engine's.** Inside `ST_Flight` the cruise
machine has three states, `Flap`, `Glide`, `Feather`, and the events that switch
them are raised by expression modifiers from the graph's own speed integration:

    Speed = clamp(Speed + MaxAcc*TimeStep, Speed, max(Speed, TargetSpeedDamped*TargetSpeedMaxScale))
    Speed = clamp(Speed - MaxDec*TimeStep, min(Speed, TargetSpeedDamped), Speed)
    Speed = clamp(Speed - 0.5*Speed²*Drag*1e-5*TimeStep, 0, Speed)
    Flap    if Speed <  TargetSpeedDamped*TargetSpeedThresholdMin  && !IsFlapping
    Feather if Speed >  TargetSpeedDamped*TargetSpeedThresholdMax  && !IsFeathering
    Glide   if between, && !IsGliding
    TargetSpeedDamped = clamp(TargetSpeed, 0, MaxSpeedDamped)     // MaxSpeed 7500, MinSpeed 2500
    MaxSpeedDamped    = MinSpeed + (MaxSpeedCurrent-MinSpeed)*(PathAngleThreshold-clamp(PathAngle,0,25))/25
    PathAngle         = fabs(TurnDelta) + fabs(PitchDelta)
    FlightPitchBlendTarget = fabs(MoveDirZ)     // the climb/dive blend
    TurnDeltaTarget  = TurnDelta  * TimeStep * TurnDeltaScale
    PitchDeltaTarget = clamp(PitchDelta * TimeStep * TurnDeltaScale, -25, 25)

`MaxAcc`, `MaxDec`, `Drag`, `MinSpeed`, `MaxSpeed`, the two thresholds and the gains
are graph variables with initial values in the file, none of them engine names. The
engine hands over `TargetSpeed`, `TurnDelta` and `PitchDelta`; the dragon decides how
fast it actually goes, banks by `TurnDeltaDamped` on every cruise blend, pitches by
`FlightPitchBlend` between flap, climb and dive clips, and slows when the path bends.
`Speed` is written back into the same variable the engine wrote, which is why the
engine's own speed sampling of the dragon is meaningless (`docs/speed-data.md`).

**Injury** is the graph's too: `Injured` (engine-written) becomes `iInjured`, a
manual-selector index on every flight clip family (default versus hurt), and
`InjuredScaleCurrent` lowers `MaxSpeedCurrent`; `IsAllowedToFly` reads `Injured`
back and the idle tree picks the injured landing.

**Take-off, landing and perching are tweens.** `BSTweenerModifier`s on the take-off
air states, the landing approach and the perch carry the actor to `TweenPosition` and
`TweenRotation`, which the engine writes from the pathing request; `TweenEntryDirection`
chooses among the five approach clips (straight, ±90°, ±180°) of every hover entry
and landing. The perch is furniture: `ST_Perch` enters with `idleChairSitting`, its
launch clips are one per perch type, and `IsFurnitureAnimType` in the idle tree picks
which `FlyStopPerch*` the engine sends. The dragon markers (`dragonmarker.nif`,
`dragontoweridle.nif`, `dragonwordwallidle.nif`, `dragonrockidle.nif`,
`dragonlaymarker.nif`, `dragonmoundmarker.nif`) are those furniture types.

**The rest of the file** is the ground creature: locomotion by `iSyncIdleLocomotion`
and `iDirectionForward` like any quadruped, shouts in every posture, the flight kill
grab and snatch, paired kill moves, bleed-out, summon, the mount and dismount paired
clips for the DLC2 rider, and the trailer shots.

## 3. Authoring a flying creature

What follows is the order in which the pieces have to exist, with what each one
decides. Everything here is data: records in a plugin, a behaviour project, and the
three caches. Nothing needs a plugin of code unless §3.9 says so.

### 3.1 Decide which of the two shipped patterns it is

A creature that should **leave the navmesh** needs the fly state and every step
below. A creature that should only **look airborne** needs none of them: the ice
wraith, wisp, witchlight and chaurus flyer are ground creatures whose skeleton root
sits high and whose animations float, walking the navmesh with a tall offset, so
they climb and descend only as the terrain does and fight as ground creatures. If
the creature never needs to cross a wall, a lake or a ravine, that pattern is
cheaper by every step that follows.

### 3.2 The race record

- **`Flies`** (race flags bit 7). Without it nothing in the AI ever requests a
  take-off; the fly-state setter itself does not check it, so a graph could still
  declare flight, but no procedure, combat tree or pathing request would ask for one.
- **`Walks`** as well, unless the creature must never be on the ground. The AI's
  pathing asks `Walks && !Swims && !Flies` and `Swims && !Walks && !Flies` as
  categories; a creature with `Flies` and not `Walks` has not been observed and would
  be a fourth category the code was not written for.
- **`FlightRadius`** (400 on every dragon race): the radius the combat area code
  scales by `fCombatAreaStandardFlyingRadiusMult`. Bigger creatures want a bigger
  one.
- `NoCombatInWater` is irrelevant in the air and matters only if the creature also
  swims (`docs/swimming.md`).
- The behaviour graph file, as for any race, names the project.

### 3.3 The combat style

The `CSTY` flight block is where the fight is tuned, and the NPC record names the
style, so two dragons of one race can fight differently:

| field | csDragon | what it weighs |
| --- | --- | --- |
| `HoverChance`, `HoverTime` | 0.52, 0.42 s | hovering in front of the target and shouting |
| `DiveBombChance` | 0.36 | the dive pass |
| `GroundAttackChance`, `GroundAttackTime` | 0.85, 1 s | landing to fight on the ground |
| `PerchAttackChance`, `PerchAttackTime` | 0.5, 0.5 s | attacking from a perch |
| `FlyingAttackChance` | 0.75 | the flying pass |

Each chance is mapped into the global range of its `fCombat…ChanceMin/Max` setting
(`0x1408dc070`), so 0 and 1 mean the range's ends, not never and always. A style
with zero ground attack and zero perch never lands to fight (`csDragonNoLanding`);
one with only a flying attack always passes (`MQ301OdahviingCombatStyle`).

### 3.4 Movement types and the `iState` names

One `MOVT` per flight posture, named through `iState_<MNAM>` variables in the root
behaviour graph (`docs/speed-data.md` §4.5). The dragon has three:

    iState_DragonFlying   -> MNAM "DragonFlying"   forward walk 2000, run 7400, rotate 90°/s
    iState_DragonHovering -> MNAM "DragonHovering" no translation, rotate 45°/s
    iState_DragonPerching -> MNAM "DragonPerching" nothing

The flight path follower plans with these speeds, and the turn-rate reader
(`0x140673210`) takes the larger of the movement type's two rotation speeds before
it would ever fall back to `fFlyingActorDefaultTurningSpeed`. So the creature's
cruise speed and how tightly it turns are here, not in any setting. The graph sets
`iState` on entering each posture (`iState = iState_DragonFlying` in the flight
machine's expressions).

### 3.5 The idle records

The AI requests five actions and the idle tree turns each into a graph event. A
new race needs its own roots under the same five actions, with race conditions so
the dragon's roots do not fire for it, sending whatever event names its graph uses:

    ActionFlyStart   root: GetFlyingState == 2 -> cruise entry, == 3 -> hover entry,
                     == 5 -> launch from perch, otherwise take-off (vertical variant by condition)
    ActionFlyStop    root: GetIsCrashLandRequest, GetIsHastyLandRequest, GetIsInjured,
                     IsFurnitureAnimType -> the landing variants; otherwise the default landing
    ActionHoverStart root: hover entry (and a "safe" variant)
    ActionHoverStop  root: hover exit
    ActionLand       is the ground creature's landing after a jump; dragons do not use it
    path-end turn:   under the turn actions, GetFlyingState == 3 and GetPathingTargetAngleOffset

The set of actions is fixed by the executable; the names, the conditions and how
many variants exist are the plugin's. A creature with one take-off and one landing
needs two idles under each root.

### 3.6 The graph

The contract the executable holds the graph to:

- **Declare the fly state from state entries.** The six response-file names,
  `FlightTakeOff`, `FlightCruising`, `FlightHovering`, `FlightLanding`,
  `FlightLanded`, `FlightPerching`, as enter events of the states that are those
  things, and `FlightAction` / `FlightActionEnd` around any grab. The engine learns
  the state only this way; a graph that flies without sending them leaves the
  controller under gravity.
- **Accept the inbound events** the idles of §3.5 send, as transitions out of the
  ground state (take-off), out of flight (hover entry, landings), out of hover
  (exit), and from a perch (launch).
- **Read the inputs it wants.** `TargetSpeed`, `Pitch`, `PitchDelta`, `TurnDelta`,
  `Speed`, `Direction`, `DistToGoal`, `Injured`, the tween set
  (`TweenPosition`, `TweenRotation`, `TweenEntryDirection`, `TweenSpeed`,
  `HasTweenSpeed`) and the landing four (`Land`, `bCrashLand`, `LandTypeIndex`,
  `PerchFireNode`). None is mandatory; the dragon ignores `Roll`.
- **Tween the take-off, the hover entry and the landing.** A `BSTweenerModifier` on
  those states carries the actor to the engine's `TweenPosition` and
  `TweenRotation`; without it the creature lands where its root motion says, not
  where the path ends, and hovers beside its target rather than in front of it.
- **Set `iState`** on entering each posture (§3.4).
- **Own the wing model, or not.** The dragon integrates `Speed` from `TargetSpeed`
  with `MaxAcc`, `MaxDec` and `Drag`, and chooses flap, glide or feather by
  thresholds on the ratio (§2). A creature with one cruise clip needs none of it:
  the movement type's speed is what the follower plans with either way. A creature
  with several needs its own rule for choosing between them, and the dragon's
  expressions are a template.
- **Injury**, if wanted: `Injured` becomes the selector between default and hurt
  clip families and the idle tree's `GetIsInjured` picks the injured landing.
- **Perches**, if wanted: each perch type is a furniture, its marker a `.nif`
  with the furniture's entry, the perch state enters with `idleChairSitting` beside
  `FlightPerching`, and the idle tree keys the landing on `IsFurnitureAnimType`.
- **Get-up**: `iGetUpType`, as for any creature.
- **Do not send the jump.** `JumpBegin` hands the controller to gravity.

### 3.7 Animations and the caches

The clips are ordinary; the take-off and landing ones carry the `to_TakeOff_Flight`
and `BeginLand` style triggers that move their own machines, and any `HitFrame`,
`preHitFrame` and sound events as on the ground. After the project changes, the
three caches have to say so: the animation set data must hold the new clips' hashes
in the sets the movement and idle events name, or the clips silently do not play
(`docs/animation-set-data.md`); the animation data cache carries the root motion;
the speed data is not meaningful for a flyer, since the graph writes `Speed` back
itself (`docs/speed-data.md`), which is how the shipped dragon is recorded.

### 3.8 What the creature will be asked to do regardless

- **The combat tree is one tree.** `CombatBehaviorTreeFlight` is chosen for any
  flying actor and its nodes are dragon-shaped: take-off, orbit, hover, flying pass,
  dive, perch attack, land and fight. The style of §3.3 weighs them; it cannot add
  or remove one. A graph that answers every request with *some* state and reports
  its fly state honestly does not break; it flies like a dragon that cannot dive.
- **The requests come with targets.** A hover entry comes with a tween target in
  front of the enemy, a landing with a spot found on the navmesh, an orbit with a
  centre; the graph is told where, never asked.
- **Landing without perches** gets the default, hasty and crash landings, chosen by
  the pathing's own conditions; that is enough for a creature that lands on the
  ground.

### 3.9 What cannot be changed from data

Five settings are single values for every flying actor: `fCombatFlightEffectiveDistance`,
`fCombatFlightMinimumRange`, `fCombatDiveBombOffsetPercent`,
`fCombatDiveBombSlowDownDistance` and `fHostileFlyingActorExteriorDistance`, plus
the `Min/Max` ends of the chance ranges. A creature cannot have its own engagement
distances or its own dive geometry. The choices are a plugin of code that changes
the setting per actor, or a graph that does not use the engine's dive at all and
animates its own pass from the flying-attack request. The path builders and the
follower are likewise one implementation; a creature that should fly in a way the
six path shapes cannot express (a straight-line darter, a hoverer that never
cruises) can still ship, but it will be flown along those shapes.

### 3.10 Approach, hover and melee from the air

How the AI would fight with a flying melee creature, from what the combat trees
say. The combat root (`CombatBehaviorTreeCombat`, `0x14085f7d0`) is a **Combat
Parallel** of two trees: an *Action* tree, which holds the melee, ranged, magic and
shout contexts, and a *Movement* tree, which selects among `CloseMovement`,
`FlankingMovement`, `RangedMovement`, `Search`, `ExitWater`, `ReturnToCombatArea`,
`Hide`, `Flee` and **`Flight`** (`0x1408ad390`). Flight is a movement tree. It moves
the body; it never swings. Swinging is the melee context's, running beside it,
and no melee function reads the attacker's fly state: the fly-state readers in
the combat code are the flight tree, the flight path requests, the shout context
and the flight settings, none of them the melee context.

The flight tree's loop (`0x140897d20`, read from its node names in construction
order): a `Flight Sequence` of `Takeoff` (conditional) and a `Flight Repeat`, each
turn of which is an `Attack Sequence Selector` between `Orbit Distant` and an
`Attack Sequence` of `Orbit` followed by an `Attack Selector` that picks, by the
style's chances (§3.3), one of `Hover`, `Flying Attack`, `Dive Bomb`, `Perch
Attack` or `Ground Attack`, with `Land Selector`, `Wait to Land`, `Wait for
Cruise`, `Leave Perch` and `Crash Land Sequence` around them.

For a creature that fights with its body rather than its breath, the turn that
matters is **Hover**:

1. The `Hover` node (`CombatBehaviorHover`) raises `CombatPathRequestHover`, whose
   destination is *none*, that is the target. The hover path builder
   (`0x1404f5420`) plans a path to a point beside the target and writes the tween
   set: `TweenPosition`, `TweenRotation`, `TweenEntryDirection`.
2. The engine's hover wrapper (`0x1406a4230`) sets fly state 3 and requests
   `ActionHoverStart`; the idle tree sends the graph's hover-entry event; the graph
   enters its hover machine, whose entry declares `FlightHovering`, tweens to the
   engine's target, and sets `iState` to the hover movement type (no translation,
   a turn rate), so the follower keeps the creature there, facing the target.
3. Meanwhile the Action tree's melee context asks its own question, reach: the
   distance from the attacker to the target's attack point, which for a flying
   attacker or target is taken by the flying variant (`0x140672e70`). When the
   race's attack data (`ATKD`) says an attack reaches, the context requests
   `ActionRightAttack` or its kin, the idle tree's attack roots resolve it for the
   race, and the graph receives `attackStart…`.
4. The graph's hover machine must **hold the attack**: a transition from the hover
   idle to an attack state that sends `weaponSwing`, `HitFrame`, `AttackWinStart`
   and `AttackWinEnd`, `attackStop`, and returns. The dragon's does not (its hover
   holds idle, turns, shouts, injury and stagger), which is why a dragon's hover is
   a breath attack: the shout context, not the melee context, is what fires there.
   A creature whose hover machine has the transition gets a melee hover from the
   same trees with no engine change.
5. After `HoverTime` the node ends, `ActionHoverStop` sends the exit, the creature
   returns to cruise, the repeat orbits and selects again.

The same shape gives the other two melee turns. **Ground Attack** is `Land`
followed by the melee context fighting on the ground as any walker, then
`Takeoff`; the dragon's bite, tail and wing attacks are these. **Perch Attack** is
the same from a perch furniture (`PerchTowerAttackSmashBite`).

What the creature has to bring, beyond §3.2 to §3.7:

- a style with `HoverChance` and a `HoverTime` long enough for a swing, and a
  `GroundAttackChance` if it should also land to fight;
- attack data on the race with a reach that matches where the hover puts it, and
  attack idle roots that fire while flying (the dragon's carry no flying
  condition, so the default is that they do);
- a hover machine with the attack transitions, and a hover movement type whose
  turn rate lets it keep facing a moving target.

What was not read, and decides whether the reach works: how the hover path
builder chooses the hover point's height and distance from the target. The
dragon hovers at a height and offset that suit a bite that does not come; a small
creature may be placed too high for its reach or too far for its swing, and the
adjustment is in the builder, not in data. The `Flying Attack` and `Dive Bomb`
turns are passes, not hovers: the pass ends at the target and the melee context
can fire during it, but the body is moving, and the dive's geometry is the five
global settings of §3.9.

## 4. The player

What is generic and would work for the player as it stands:

- the fly-state setter and everything it does: the controller's flying state, the
  movement controller's flight switch, the movement mode 3 and so the
  `Default MovementType: Fly` object, `iState_<MNAM>` selection;
- the six handlers and the per-frame writer, which Actor, Character and
  PlayerCharacter share;
- the permission bits: every actor is constructed allowed to fly, `IsAllowedToFly`
  fails only on the race flag, injury, health and the block bit, and the race flag
  is a record edit.

What is missing, and is the whole problem:

- **no input becomes flight.** The player's movement controller has agents that turn
  controls into a ground heading and speed; the only flight agent is
  `MovementAgentPathFollowerFlight`, which follows a *path*. With the fly state set,
  the player's controller is in Havok's flying state with no gravity, the movement
  controller is in flight mode with no path, and the graph receives `TargetSpeed`,
  `Pitch` and `PitchDelta` computed for a path that does not exist;
- **every line of player flight code is about the mount.** `PlayerCharacter::Update`
  (virtual `0xAD`, `0x140732660`) reads the fly state of the *mount* through
  `0x14074d450`; the map-menu and HUD paths (`0x140731780`) ask `CanFly` and
  `IsAllowedToFly` of the mount and show the `sTESVDLC2FlyingMount…` messages; the
  camera's `PitchOverride` and `ZeroOutCameraPitch` are the mounted pair. The DLC2
  dragon riding is the player choosing targets for an AI-flown dragon, not the player
  flying;
- **the player's graph has no flight states**, and the response files route the
  flight names to handlers that expect the actor to have declared them. A player
  graph with a flight machine that sends `FlightCruising` on entry would set the
  state correctly; it would then have to compute its own `Speed`, as the dragon does,
  because the engine's `TargetSpeed` would be zero.

So the player can be given a fly state today by any caller of `0x14069be40`, and
will float without gravity, but nothing in the engine turns input into flight: the
`Player Controls` agent yields a ground heading and speed, no key means pitch or
altitude, and the flight follower needs a path. What the engine lacks is the
*driver*, and the driver can be the graph.

### 4.1 The data-only route

Every hook a flying player needs is reachable from data, so a prototype needs
neither a patched executable nor an SKSE plugin. It is the dragon's own design
turned inward:

- **Entering flight is a graph event.** A flight machine added to the player's
  graph whose entry state sends `FlightCruising`: the response file routes it to the
  handler, the handler calls the setter on the player, and gravity stops. Leaving
  the machine sends `FlightLanded` and gravity returns. The setter checks no race
  flag and no permission bit on this path. The machine's own entry event is
  whatever name the graph defines, so Papyrus starts and stops flight with
  `SendAnimationEvent`.
- **Movement is root motion.** The flight states set `bAnimationDriven`; the engine
  answers with `StartAnimationDriven` and hands movement to the `Animation Driven`
  agent, and the clips carry the player. The dragon's cruise clips are this shape.
- **Steering is the variables the engine already writes for the player.**
  `Direction` and `Speed` from the movement keys, `TurnDelta` from the turn,
  `Pitch` from the per-frame writer (`0x14069dac0`, shared by PlayerCharacter),
  which is the actor's own pitch and for the player follows the look input, and
  `AimPitchCurrent`, written for aiming (`0x1406a07b0`). A blend on `Direction`
  picks forward, strafe and back; a blend on `Pitch` picks climb, level and dive; a
  threshold on `Speed` picks the fast clip while sprint is held.
- **The movement type is the graph's.** `iState_<MNAM>` naming a player-only
  flight `MOVT` in the plugin keeps the engine's requested `Speed` sane instead of
  falling through to the `Default MovementType: Fly` object.

What the machine must defend against: **the jump**. `JumpBegin` puts the controller
into its jumping state and gravity comes back, so the flight machine must own no
transition that sends it. Furniture, mounts, ragdoll and kill moves leave through
their own state changes; the flight machine should sit at a level those events
cannot enter from.

What is unverified, and is the in-game test: whether the animation-driven agent
applies vertical root motion while the controller is in the flying state (on the
ground the ground constraint overrides the Z delta; in the flying state nothing
does, so it should pass through); whether the third-person camera tolerates an
actor rising away from its pivot; and collision, which the flying state does not
remove, only gravity and ground contact, so walls should still stop the player.

### 4.2 What a plugin would add

Altitude by a key rather than by looking up, a camera state made for the air, and
speed as a number rather than a choice of clips: an agent, or a replacement of the
per-frame writer's inputs, that maps controls to `TargetSpeed`, `Pitch` and
`TurnDelta`, so that a motion-driven flight like the dragon's becomes possible.
None of that has a hook in the shipped engine; all of it is polish on a prototype
that data alone can show flying.

### 4.3 The mount route

The one Bethesda built: the player rides a creature that flies itself. The AI, the
pathing, the camera states and the messages all exist for it, and it needs only a
race with the `Flies` flag, its graph, and the ride packages. It keeps the flying
actor an NPC, which is why the player's own flight code is all about the mount.

### 4.4 Combat in the air

Nothing in the engine refuses a combat action to a flying actor. The action
mediator's gate (`0x1406cf1b0`), which refuses attacks, bashes and casts to a
swimming actor whose race carries `NoCombatInWater` (`docs/swimming.md` §1), does
not read the fly state. The player's attack roots in the idle tree carry no flying
condition: `AttackRightRoot` and `AttackLeftRoot` are gated on `IsSwimming == 0` and
`IsRidingMount == 0` in Skyrim.esm and on nothing in Update.esm. A flying player who
attacks has the request turned into its `attackStart` event and sent to the graph as
on the ground; the graph, not the engine, decides what is allowed in the air.

Bethesda shipped one form of it. On the flying mount, Dragonborn's idles allow
spells and shouts under `IsOnFlyingMount == 1` (`MountedMagicDraw`, `MountedVoice`,
the `AttackMagicLeft/RightRoot` duplicates), gated by the default-object lists
`Flying Mount - Allowed Spells` and `Disallowed Spells`, and block melee under the
same condition. The shipped design is ranged and voice combat from the air, no
melee, on a body the AI flies.

For the player's own flight (§4.1) the work is the graph's, on the pattern mounted
combat already uses in `0_master`: an upper-body attack layer over the flight
locomotion, as mounted combat lays attacks over the horse's motion and the dragon
lays `BHR_Flight_Shout` over its cruise. The attack states are the ordinary ones,
sending `weaponSwing`, `HitFrame`, `AttackWinStart`, `AttackWinEnd` and `attackStop`
for melee and the spell-fire events for magic; a shout needs nothing new and is
the nearest thing to the dragon's flying attack.

What the engine then does for free: a melee `HitFrame` resolves against whatever
the swing has collected, so it lands on anything within reach of the airborne
player; arrows and spells launch from the actor's nodes at any height; enemies
treat a flying player as they treat a dragon, archers and casters shoot, melee
attackers cannot reach and wait or flee.

Unverified, and settled by the same prototype as the flight itself: whether the
weapon's hit collection runs while the controller is in the flying state, since it
is fed by the character's movement update; and whether the enemy AI's reachability
test gives up on a low-flying player rather than jumping at it.

## 5. How this was measured

The dragon graph was dumped with HKX2 through `BehaviorFile.Load` (machines, states,
transitions, wildcard transitions, variable bindings, expression modifiers, clip
triggers). Idles, actions, races and movement types were read from the five masters
with Mutagen. The executable was searched for the flight strings, the fly-state bit
patterns (`0x1c0000`, `shr $0x12`), the race-flag and state-word-2 bit tests, and the
RTTI classes with *fly*, *flight*, *hover*, *orbit* or *perch* in the name; the
functions found were read with `index.py dump`. The reading of the player's
functions is from their calls and the mount accessors they use, not from a run of
the game.
