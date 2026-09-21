# Furniture and the player's graph: what a minigame can be built from

The question was whether a furniture interaction can carry a minigame, with a
ballista the player sits at and shoots as the example. The answer is yes, from
data alone, because the four things a minigame needs already exist as separate,
composable pieces: a furniture puts the player into a state of its graph and
locks a camera; the graph can hold any machine inside that state; the engine writes
the player's aim into the graph every frame and the mounted-combat machine already
turns it into a bow; and a script can fire a weapon's projectile from any reference
on an animation event. What is missing is only the state, the clips and the script.
Evidence: the masters through Mutagen (furniture, keywords, idles), the player's
`mt_behavior` and `horsebehavior` through HKX2, and the executable (AE layout,
addresses as in `docs/animation-events.md`).

## 1. What a furniture is to the engine

A `FURN` record is a model whose `.nif` carries marker nodes (`BSFurnitureMarkerNode`:
entry positions, entry directions, the *animation type* sit, sleep or lean), plus
`Keywords`, an `InteractionKeyword` (`ActorTypeNPC` on 457 of 519, `ActorTypeDragon`
on the seven dragon perches, `DLC2RieklingKeyword` on the riekling's prayer),
optional `WorkbenchData` (a bench type and a skill: `CreateObject` for the 39 cooking
pots and forges, `Alchemy`, `Enchanting`, `SmithingArmor`, `SmithingWeapon`), and
`Markers` entries with enabled entry points. The record-side marker keyword is empty
on every shipped furniture; the animation type the conditions read
(`IsFurnitureAnimType`, `IsFurnitureEntryType`, `GetFurnitureMarkerID`) comes from
the model's marker nodes.

**Activation.** The player activates, the engine requests `ActionIdle` with the
furniture as target, and the idle tree chooses the entry: the sit roots key on
`IsFurnitureEntryType` (front, behind, left, right) and `GetSitting`; special
furniture keys on its keyword (`FurnitureWoodChoppingBlock`, `FurnitureLedgeSit`,
`FurnitureCounterLeanMarker`, `FurnitureLayDownMarker`, `FurnitureExecutioner`,
`FurnitureBedRoll`, the `Crafting*` keywords). The idle's event is a wildcard
transition into `mt_behavior`'s `FurnitureState`: `IdleChairFrontEnter`,
`IdleWoodChopStart`, `IdleBlackSmithingEnterStart`, `IdleWallLeanStart`,
`IdleCounterStart`, `IdleSharpeningWheelStart`, `IdleMillLoadStart`,
`IdleLeverPushStart`, `IdleRailLeanEnter`, `IdleCartBenchEnter`,
`IdleExecutionerIdle`, `IdleCarryBucketFillEnter`, and 88 routes in all, each with
its `nested=` target inside `Furniture_BehaviorGraph`.

**What the engine does on entry**, from the handlers of `docs/animation-events.md`
§6: the graph's entry state sends `idleChairSitting`, the response file routes it to
`PlayerChairEnterHandler`, and that handler sets the sit state with the furniture
bookkeeping, puts the player camera into `FurnitureCameraState`
(`0x1408e3a10(camera, true)`), registers the furniture reference with the HUD
(`0x14097adb0`), and **disables the fighting controls**: `0x140cd5650(controlMap,
0x40, false, false)`, where `0x40` is the `kFighting` user-event flag. Movement,
looking and activation stay. `PlayerFurnitureExitHandler` restores all three on
`idleChairGetUp`. So a seated player cannot attack, bash or cast by default, and a
minigame that wants those inputs has to give the flag back: Papyrus
`EnablePlayerControls` with fighting on writes the same control map.

**The two keywords that choose the person.** `FurnitureForce3rdPerson` (120
furniture) and `FurnitureForce1stPerson` (2) are default objects the camera code
reads; the crafting furniture are all third person.

## 2. What the furniture state can hold

`Furniture_BehaviorGraph` is an ordinary state machine, and its shipped states show
what a state in it may do:

- **hold a machine of its own**: `BlackSmith_State`, `Web_State`, the
  `CraftingSmelter_State`, all state machines with their own idles and exits;
- **load and show animated objects**: `AnimObjLoad.AnimObjectSpoon`,
  `AnimObjDraw.AnimObjectSpoon`, on entry, `AnimObjectUnequip` on exit; the mortar
  and pestle, the hammer and iron, the shovel and coal, the hook, the sword;
- **take the camera**: `StartAnimatedCamera.Camera3rd [Cam3]` on entry and
  `EndAnimatedCamera` on exit. The payload is a **node name in the player's own 3D**
  (`0x1408e42d0` copies it and calls `GetObjectByName`, virtual 42, on the player's
  root); the player's skeleton has `Camera3rd [Cam3]`, and the crafting clips animate
  that bone. Every crafting camera is a clip driving `Cam3`: `CraftingCookingSpitCamera`,
  `CraftingAlchemyBlendCamera`, `CraftingOvenCameraBlend` are blenders over such
  clips. `StartAnimatedCameraDelta.<node>` keeps the current offset (the sprint uses
  it with `AnimObjectA`);
- **turn head tracking off and on** around the state (`HeadTrackingOff`,
  `HeadTrackingOn`);
- **run under a menu**: the crafting states are entered by the engine when the bench
  menu opens and left when it closes; the menu is not the graph's, only the pose is.

A minigame state is one more of these: a machine entered by its own idle event,
holding whatever clips it wants, taking the camera through `Cam3` if the fixed
furniture camera does not suit, and exiting through `IdleFurnitureExit`.

## 3. Aiming inside a furniture

The aiming is not an engine input to the graph; it is a native modifier the graph
places. `BSDirectAtModifier` reads the camera itself, bends the bones it names
toward the camera's aim within `limitHeadingDegrees` and `limitPitchDegrees`, with
`offsetHeadingDegrees` and `offsetPitchDegrees`, and follows at `onGain` and
`offGain`; it publishes what it did through three output members that the shipped
graphs bind to variables: `active` to `bAimActive`, `currentHeadingOffset` to
`AimHeadingCurrent`, `currentPitchOffset` to `AimPitchCurrent`, and its own camera
position `directAtCameraX/Y/Z` to `camerafromx/y/z`, which the `BSLookAtModifier`s
take as their `lookAtCamera` input. None of these names is written by the engine:
`camerafromx` is not even a string in the executable, and every engine access to
`bAimActive`, `AimHeadingCurrent`, `AimPitchCurrent` and the two `Max` limits is a
read (`0x1406a07b0` from the player's update, and the getters at `0x14069b010`
through `0x14069b2d0`), which is how the launch learns where the graph is aiming.
The `Aim*` and `camerafrom*` entries of `docs/animation-variables.md` are therefore
graph-to-engine, and `bAimActive` is not "engaged by the engine"; it is the
modifier reporting that it is active.

The shipped consumers are all one modifier: `BSDirectAtModifier_Bow` in
`1hm_behavior` and `BSDirectAtModifier_Magic` in `magicbehavior` on foot,
`MC_BSDirectAtModifier_Bow` in `horsebehavior` with the `Mounted` limits and gains,
`DragonRider_BSDirectAtModifier` and its `_HeadOnly` twin in `0_master`. Nothing in
the modifier or its readers checks the furniture state or a drawn weapon: a
direct-at modifier placed in a furniture state aims at the camera for as long as
that state is active, with nothing to switch on. That is the aiming a seated
minigame gets, and the ballista's turret is one more bone in its list if the
ballista is an animated object on the player.

## 4. Firing

Two routes, and the shipped mounted graph uses the first:

- **the bow path on the player.** `horsebehavior.hkx` has the full archery machine
  seated: `BowAttackState` on `bowAttackStart`, `BowDraw` (entered with `BowDraw`,
  leaving with the pull-loop sound stopped), `Bow_Release` on `attackRelease` with
  its `arrowRelease` trigger, `CrossBow_AttackState` and `CrossBow_ReloadState`
  for the crossbow, `MC_AttackState` for melee, `MountedShout` for the voice. The
  attack requests reach the graph because mounted combat leaves the fighting
  controls on. The engine's side is the arrow handlers (`docs/projectiles.md` §2):
  attach the ammunition to the biped, launch it from the weapon. A furniture state
  that holds the same states, with the fighting flag restored, shoots whatever
  bow-type weapon and ammunition the player has equipped, from the player's hands;
- **a script firing from the furniture.** Papyrus `Weapon.Fire(akSource, akAmmo)` is
  the native at `0x140a45610` ("Cannot fire a weapon from a None source"), and it
  calls the weapon launch `0x140286c90` with the source *reference* in the actor's
  place; the same launch is reachable from a reference virtual (`0x14035dfe0`), which
  is how the Dwemer ballista traps shoot: their `TrapDweBallistaWeapon*` are bow-type
  weapons, their bolts Arrow-type ammunition with gravity 0.8, and the activator
  script fires them. A script that registers for the graph's release event
  (`RegisterForAnimationEvent` on the player, the event the fire clip triggers) and
  calls `Fire` on the ballista reference with the bolt ammunition launches from the
  ballista, not from the player's hand, along the ballista's facing. The ballista's
  turret then has to face where the player aims: either the ballista is an animated
  object on the player and the direct-at modifier turns it, or the script sets the
  reference's angle from the camera each frame.

Aim assist, spread, sticking, recovery of the bolt and the projectile's gravity all
come from the arrow path either way.

## 5. The ballista, assembled

Records: a `FURN` whose model has one sit marker at the operator's seat and
`FurnitureForce3rdPerson`, or `FurnitureForce1stPerson` for a sight; a keyword for
the idle tree to key on; an idle under `ActionIdle` conditioned on that keyword
sending the entry event; a bow-type `WEAP` and an Arrow-type `AMMO` whose
projectile is the bolt, non-playable if the player should not carry them; the
ballista as a `STAT` or `ACTI` with a script.

Graph, in `mt_behavior`'s `Furniture_BehaviorGraph`: a state entered by the idle's
event, sending `idleChairSitting` on entry; inside it an idle, a direct-at layer on
the aim variables, and the archery machine copied from `horsebehavior` with the
draw, the drawn idle, the release clip carrying `arrowRelease` and `bowRelease`,
and `attackStop`; `StartAnimatedCamera.Camera3rd [Cam3]` with a `Cam3` clip if the
view should sit on the weapon; `IdleFurnitureExit` to leave.

Script: on the furniture's activation, `EnablePlayerControls` with fighting on after
the entry event, so the attack input reaches the graph, and back off on exit; and
either nothing more, if the player's own bow launches the bolt, or
`RegisterForAnimationEvent` for the release and `Weapon.Fire` on the ballista
reference, with the reference's angle set from the camera heading and pitch, if the
bolt should leave the ballista.

Two things read in the executable rather than run: the aim needs no drawn weapon
and no engine switch, since the direct-at modifier is active whenever its state
is and publishes `bAimActive` itself; and the fighting flag a script restores
survives the seat, since the control map's fighting bit (`0x40`) is cleared only
by `PlayerChairEnterHandler` and restored only by `PlayerFurnitureExitHandler`'s
pop; the other toggles in the executable are the animated-object draw (`0x20`,
the POV switch, at `0x1407c37a0`, `0x1407c3a90`, `0x1407c3de0`), the menus, and
the Papyrus natives themselves. What only a run settles: that the exit's pop,
which restores the state saved at entry, does not undo a script's change made
before the entry, and the feel of the direct-at gains on a turret.

## 6. What else this makes possible

The same four pieces build any seated minigame: a furniture entry, a state with
its own machine and camera, the aim variables as an input, and a script listening
to the graph's events. A lockpick with a physical lock is a state whose direct-at
layer turns the pick by heading and the tension by pitch, with the script scoring
on events. A cooking or crafting game is the shipped crafting state with the menu
replaced by clips and a script. A cannon, a catapult, a turret are the ballista
with a different weapon and ammunition; a catapult stone is a gravity missile
(`CWCatapultProjectile`, gravity 1, speed 4000) on an ammunition, and the arrow
path lobs it. What the pieces do not give: a second input axis beyond the camera's
heading and pitch, since the movement keys are locked to the sit; anything that
needs the player to leave the marker without exiting the state; and a menu, which
is UI code, not a graph.

## 7. How this was measured

Furniture, keyword and idle records were read from the five masters with Mutagen.
The player's `mt_behavior` and `horsebehavior` were dumped with HKX2, with event
payloads. The executable was read for the furniture handlers' control-map and
camera calls, the animated camera's name resolution, the aim writer and its caller,
and the callers of the weapon launch, with `index.py dump`. The assembled ballista
is a design from those pieces, not a run of the game.
