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

## 3. What is fixed and what is free for a new flying creature

Fixed, because the engine reads it by name or number:

- the race flag `Flies`, or nothing in the AI will ever request a take-off;
- the six fly-state events from the response file, sent from the graph's own state
  entries, or the engine thinks the creature is still on the ground and the
  controller keeps gravity;
- `iState_<MNAM>` variables naming movement types with the speeds the pathing should
  plan with, since the fly path follower works from the movement type as the ground
  one does;
- the inbound names: whatever the idle records send. The shipped roots key on the
  dragon's names, but an idle tree is data, and a new race's roots can send any names
  under any conditions. What cannot change is the *set of actions*: Fly Start, Fly
  Stop, Hover Start, Hover Stop, Land, and the path-end turn;
- the per-frame inputs `TargetSpeed`, `Pitch`, `Roll`, `PitchDelta`, `TurnDelta`,
  `Speed`, `Direction`, `DistToGoal`, the tween set, and the landing four. A graph is
  free to ignore any of them, as the dragon ignores `Roll`;
- `Injured` if the injured-landing conditions are wanted, `iGetUpType` for get-up.

Free, because the executable never sees it: the number and shape of flight states,
whether there is a wing model at all, hovering as a state or as a tween, how speed
integrates, banking and pitching, how many perch types, whether landing needs an
approach. The hovering creatures show the other pattern that already ships: the ice
wraith, wisp, witchlight and chaurus flyer have **no flight machine and no fly state**;
they are ground creatures whose animations float, walking the navmesh with a tall
offset, so they climb and descend only as the terrain does. A creature that should
leave the navmesh needs the fly state and everything above; one that should only
look airborne needs nothing.

Three things a different pattern runs into:

- **the combat tree is one tree.** `CombatBehaviorTreeFlight` is chosen for a flying
  actor and its nodes are dragon-shaped (dive bomb, perch attack, orbit). A creature
  whose graph cannot orbit will be asked to; the request is an `ActionFlyStart` or a
  hover with a tween target, so a graph that answers every request with *some* state
  and reports its fly state honestly will not break, it will just fly like a dragon
  that cannot dive;
- **landing needs a place.** `FlyStop` conditions ask the pathing whether the landing
  is hasty or a crash and which furniture type the target is; a creature with no
  perch furniture gets only the default and hasty landings, which is fine;
- **the tween is the engine's.** Take-off and landing clips are carried by
  `BSTweenerModifier` to the engine's target; a graph that does not tween will land
  where its root motion says, not where the path ends.

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
