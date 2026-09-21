# Animation events: what the engine does on each

Step 3 and 4 of the event investigation (`docs/animation-variables.md` §4,
`docs/animation-events-census.md`). Every one of the 93 `<Name>Handler` functors was
read in the unwrapped executable, and this says, per event, what the game does when a
clip fires it. Addresses are given so any claim can be re-read with
`tools/exe-re/index.py <exe> text.asm dump <address>`.

## 1. How a handler runs

A functor is slot 1 of the handler's vftable and has one signature throughout:

    bool operator()(Handler* this, Actor* actor, const BSFixedStringCI& payload)

`payload` is the text after the dot in the clip's annotation (`NPCAttach.SaddleBone`,
`CameraShake.0.5`, `AnimObjectLoad.AnimObjectBook`). Most handlers ignore it; the
ones that read it are marked below. The return value is almost always `true`; the four
that can return `false` (HitFrame, the spell casts and fires, the chair exit) do so to
say the event did not apply, and nothing acts on it.

**A clip does not send a handler's name.** The factory registers each handler under
its class name (`0x1407bafd0(manager, "WeaponRightSwingHandler", creator)` in the
initialiser at `0x1407b3ee0`), and the translation from what a clip fires to that key
is a data file: `meshes/responses/actorresponse.txt` in `Skyrim - Animations.bsa`, 88 lines of `name : Handler`,
read by `0x1407b2860` with `" : "` as the separator. The player character reads
`playercharacterresponse.txt` first, whose `BackupResponse : ActorResponse` line
chains to the actor file; the names `ActorResponse` and `PlayerCharacterResponse` in
the engine's string table are these two files. Lookup is case-insensitive. The
headings below keep the class names, because that is how the executable and the
census name them; the table gives the name a clip fires. Several handlers have two
names, two handlers are registered under an older name (`StopHorseCameraHandler`,
`RemoveCharacterControllerHandler`), and the player file's nine entries are the only
route to the player-only handlers. The last two columns count trigger sites in the
shipped behaviours and annotations in the shipped animations, from a corpus that
holds every behaviour file but only 1,308 of the character's clips.

| clip fires | handler | file | triggers | annotations |
| --- | --- | --- | --- | --- |
| weaponSwing | WeaponRightSwing | actor | 333 | 256 |
| weaponLeftSwing | WeaponLeftSwing | actor | 88 | 0 |
| HitFrame | HitFrame | actor | 461 | 261 |
| preHitFrame | AnticipateAttack | actor | 433 | 262 |
| AttackWinStart, AttackWinStartLeft | AttackWinStart | actor | 210, 109 | 2, 1 |
| AttackWinEnd, AttackWinEndLeft | AttackWinEnd | actor | 210, 109 | 2, 1 |
| attackStop, bashStop | AttackStop | actor | 480, 16 | 0 |
| recoilStop | RecoilStop | actor | 82 | 0 |
| staggerStop | StaggeredStop | actor | 184 | 0 |
| Decapitate | Decapitate | actor | 2 | 0 |
| KillActor | KillActor | actor | 7 | 0 |
| KillMoveStart, KillMoveEnd | KillMoveStart, KillMoveEnd | actor | 0 | 0 |
| pairedStop | PairedStop | actor | 2 | 0 |
| DeathEmote | DeathEmote | actor | 0 | 0 |
| DeathStop | DeathStop | actor | 11 | 0 |
| BeginWeaponDraw | WeaponBeginDrawRight | actor | 34 | 22 |
| weaponDraw | RightHandWeaponDraw | actor | 31 | 48 |
| BeginWeaponSheathe | WeaponBeginSheatheRight | actor | 36 | 14 |
| weaponSheathe | RightHandWeaponSheathe | actor | 26 | 33 |
| BowDrawn, BowRelease | BowDrawn, BowRelease | actor | 35, 1 | 0 |
| BowZoomStart, BowZoomStop | BowZoomStart, BowZoomStop | player | 0 | 0, 8 |
| arrowAttach | ArrowAttach | actor | 9 | 31 |
| arrowDetach, bowReset | ArrowDetach | actor | 1, 0 | 0 |
| arrowRelease | ArrowRelease | actor | 3 | 24 |
| BeginCastLeft, BeginCastRight, BeginCastVoice | the three SpellCast | actor | 0, 0, 1 | 0 |
| MLh_SpellFire_Event, MRh_SpellFire_Event, Voice_SpellFire_Event | the three SpellFire | actor | 35, 20, 49 | 22, 5, 15 |
| InterruptCast | InterruptCast | actor | 0 | 0 |
| summonStop | EndSummonAnimation | actor | 6 | 0 |
| VampireFeedEnd | VampireFeedEnd | actor | 0 | 0 |
| idleChairSitting, idleBedSleeping | ChairEnter, BedEnter; the Player pair in the player file | both | 9, 0 | 0 |
| idleChairGetUp, idleBedGetUp, idleSleepGetUp | ChairFurnitureExit, BedFurnitureExit; PlayerFurnitureExit | both | 3, 10, 0 | 0 |
| PickNewIdle | PickNewIdle | actor | 27 | 0 |
| IdleDialogueLock, IdleDialogueUnlock | IdleDialogueEnter, IdleDialogueExit | actor | 0 | 0 |
| ActivationDone | ActionActivateDone | actor | 5 | 0 |
| NPCAttach, NPCDetach | NPCAttach, NPCDetach | actor | 0 | 0 |
| ExitCartBegin, ExitCartEnd | ExitCartBegin, ExitCartEnd | actor | 0 | 0 |
| MountEnd, DismountEnd | MountDismountEnd | actor | 0 | 0 |
| StopHorseCamera | StopMountCamera | actor | 0 | 0 |
| AnimObjLoad, AnimObjDraw | AnimationObjectLoad, AnimationObjectDraw | actor | 0, 34 | 5, 3 |
| AddRagdollToWorld, RemoveRagdollFromWorld | AddRagdoll, RemoveRagdoll | actor | 0 | 0 |
| RemoveCharacterControllerFromWorld | RagdollStart | actor | 0 | 0 |
| GetUpStart, GetUpEnd | GetUpStart, GetUpEnd | actor | 0, 180 | 0 |
| JumpBegin | JumpAnimEvent | actor | 6 | 0 |
| FlightTakeOff, FlightCruising, FlightHovering, FlightLanding, FlightPerching, FlightLanded | the fly-state six | actor | 0 | 0 |
| FlightLandEnd | FlightLandEnd | actor | 4 | 0 |
| FlightAction, FlightActionEntryEnd, FlightActionEnd | FlightAction… | actor | 0, 5, 0 | 0 |
| FlightActionGrab, FlightActionRelease | FlightActionGrab, FlightActionRelease | actor | 1, 1 | 0 |
| FlightCrashLandStart | FlightCrashLandStart | actor | 0 | 1 |
| StartMotionDriven, StartAnimationDriven, StartAllowRotation | MotionDriven, AnimationDriven, AllowRotation | actor | 0, 12, 0 | 0, 28, 0 |
| MTState | MTState | actor | 0 | 0 |
| EnableCharacterPitch, DisableCharacterPitch | the pitch pair | actor | 0 | 0 |
| EnableBumper, DisableBumper | the bumper pair | actor | 0 | 0 |
| HeadTrackingOn, HeadTrackingOff | HeadTrackingOn, HeadTrackingOff | actor | 17, 14 | 3, 1 |
| ZeroOutCameraPitch | ZeroPitch | player | 0 | 0 |
| PitchOverrideStart, PitchOverrideEnd | PitchOverrideStart, PitchOverrideEnd | player | 14, 14 | 0 |
| StartAnimatedCamera, StartAnimatedCameraDelta | AnimatedCameraStart, AnimatedCameraDeltaStart | player | 0 | 0 |
| EndAnimatedCamera, GraphDeleting | AnimatedCameraEnd | player | 0 | 0 |
| CameraOverrideStart, CameraOverrideStop | CameraOverrideStart, CameraOverrideStop | actor | 0 | 0 |
| CameraShake | CameraShake | actor | 0 | 118 |

The flight, mount, cart and bumper names with no vanilla sender are sent by the
engine or by graphs the corpus does not hold; a zero here is a lower bound, not an
absence. What a clip sends that is in neither file (`NPCKillMoveStart`,
`NPCPairedStop`, `PairEnd`, `IdleStop`, `CastOKStart`, `SoundPlay.*`, the feet) never
reaches a handler: it is the graph's own transition event, or is read by another
system.

Many handlers begin with a call to `0x1406d1ee0`, a check on which thread the event
arrived on. On the wrong thread the same work is queued on the task list at
`0x1431993a0` and done later; on the right one it is done in place. Both paths do the
same thing, and the descriptions below do not repeat the split.

**The actor fields the handlers touch.** This executable is the AE layout: from the
`MagicTarget` subobject on, every `Actor` offset is 8 bytes past the SE one.

| field | at | read or written by |
| --- | --- | --- |
| actor state word 1 | `Actor+0xC8` | attack, sit/sleep, fly, life and knock states, below |
| actor state word 2 | `Actor+0xCC` | head tracking, weapon state, recoil, stagger |
| process | `Actor+0xF8` | almost everything; `+0x08` middle-high, `+0x10` high, `+0x137` process level |
| graph holder | `Actor+0x38` | `iGetUpType` reads, ragdoll enumeration |
| actor values | `Actor+0xB8` | bow zoom, arrow release, shout recovery |
| movement controller | `Actor+0x150` | the motion-mode three, ExitCartBegin |
| magic casters | `Actor+0x1A8` | InterruptCast |
| race | `Actor+0x1F8` | Decapitate, DeathStop, HeadTrackingOff |
| position, rotation, cell | `Actor+0x54`, `+0x48`, `+0x60` | sounds, get-up placement, the Havok world |

The bit fields in state word 1, with the values the handlers write. The names are
CommonLibSSE's where the value matches what the handler does; the numbers are what
the code writes.

| bits | field | values the handlers use |
| --- | --- | --- |
| 14–17 | sit/sleep | 1 want to sit, 2 waiting for sit anim, 3 sitting, 4 want to stand, 5–8 the same for a bed, 0 none |
| 18–20 | fly | 1 take-off, 2 cruising, 3 hovering, 4 landing, 5 perching, 6 action, 0 none |
| 21–24 | life | 1 dying (DeathStop requires it) |
| 25–27 | knock | 6 getting up |
| 28–31 | attack | 2 swing, 3 hit, 4 next attack, 5 follow-through, 9 bow attached, 10 bow drawn, 11 bow releasing, 12 bow released, 13 bow next attack, 14 bow follow-through, 0 none |

State word 2: bit 3 head tracking, bits 5–7 weapon state (3 and above is drawn), bits
10–11 recoil, bit 13 staggered.

## 2. Motion and the character controller

**AnimationDriven, MotionDriven, AllowRotation** (`0x1407b9cd0`, `0x1407b9c60`,
`0x1407b9d40`). All three ask the actor's movement controller for its
`IMovementMotionDrivenControl` interface by name (`0x1407c2c10`, the name is the
static string at `0x1431a5dd0`) and call one method on it: slot 3 for AnimationDriven,
4 for MotionDriven, 5 for AllowRotation. The census called `0x1407c2c10` a shared
setter; it is the interface lookup, and the three events are three different methods.
These are the events the engine also sends itself when `bAnimationDriven` or
`bAllowRotation` changes; a clip sends them to switch who moves the actor for its
duration, and a clip that goes animation-driven without sending MotionDriven at the
end leaves the actor stuck on its animation root until something else switches it
back.

**MTState** (`0x1407ba940`) marks the process (`0x140713080(process, true)`) so the
next update re-reads `iState`; see `docs/speed-data.md` §4.5.

**EnableCharacterPitch, DisableCharacterPitch** (`0x1407ba660`, `0x1407ba690`) set or
clear bit 25 of the character controller's flags at `+0x218`; disabling also zeroes the
pitch pair at `+0x24C`. A creature that pitches with the ground (the dragon, the
horse) sends these around clips that must not.

**EnableCharacterBumper, DisableCharacterBumper** (`0x1407ba420`, `0x1407ba4c0`)
switch the controller's bumper on or off (`0x140e9ec60(controller, bool)`), after
taking a float from the actor (`0x1402ef290`) and raising the value cached on the
loaded-data block (`Actor+0x68 → +0x20`) if the new one is larger. The bumper is
what pushes other actors out of the way; a sitting or paired clip disables it.

**ZeroPitch** (`0x1407ba970`) tells the player camera (`0x1430fd7f8`) to level: sets
its flag at `+0x164`, records the camera target's current pitch at `+0x15C` and resets
the timer at `+0x158` (`0x1408e5970`).

**PitchOverrideStart, PitchOverrideEnd** (`0x1407ba8e0`, `0x1407ba910`) set or clear a
byte at `+0x85` on one particular camera state, the one stored at `PlayerCamera+0xB8`,
and only if that state is current. The vanilla senders are the character's
`0_master`, `mt_behavior` and `weapequip` graphs, in both persons, and the draugr's;
which state the slot holds was not established.

**HeadTrackingOn** (`0x1407b8a90`) sets bit 3 of state word 2 and nothing else.
**HeadTrackingOff** (`0x1407b8aa0`) clears it, drops the head-track target on the
process (`0x1406ebbb0`), and for a race flagged `UsesHeadTrackAnims` (race flags bit
15) also resets the head-track animation (`0x140710a40`).

## 3. Melee

**WeaponRightSwing, WeaponLeftSwing** (`0x1407b79a0`, `0x1407b7a30`) set the attack
state to 2 (swing), fetch the weapon in that hand (`0x14065d270`, right = 1, left = 0,
non-null only for a `WEAP`), and if it has a sound descriptor at `+0x1D8` play it at
the actor's position attached to the actor's 3D (`0x140551af0`). Then they call actor
virtual `0xEF` (`0x1406b7900`; the player's override adds the control state), which
evaluates the swing's perk entry points against the current attack data and the
weapon in hand. A clip without a swing event makes no sound and never enters state
2, so its HitFrame refuses to land.

**HitFrame** (`0x1407b81b0`; reads the payload). If the actor's virtual `0x99` says
no (`0x140674ed0`, called with 0), it resolves the hit: `0x1406b9460(actor, isLeft,
true)`, where `isLeft` is the payload compared to the name-table entry `Left`
(`0x1420f6370+0x5D0`). The shipped behaviours fire `HitFrame.Left` at 54 trigger
sites, the left hooks and off-hand attacks in `0_master`, and plain `HitFrame` at
407. That function counts the hit on the high process (`+0x448`),
takes the weapon in the named hand, breaks invisibility and ethereal form
(`0x1406c6d90`), evaluates perk entry points (`0x140385f30`) and applies the hit to
each actor the swing has collected (`0x1406ba870`, twice). Afterwards the handler
moves attack state 2 to 3 (hit) and returns `true`, or returns `false` if the state
was not swing. That last check is why HitFrame after a missing swing does nothing.

**AnticipateAttack** (`0x1407b8240`; a clip fires it as `preHitFrame`; reads the
payload as HitFrame does) records the
named hand's current weapon-node position on the high process (`+0x2DC` left,
`+0x2E8` right, via `0x1406e3ae0`), then finds the combat target (`0x1406b9a10`),
checks it is close enough and facing (`0x140852c30`), and sends it a `BGSActionData`
so its AI can block or dodge. It changes no state on the sender.

**AttackWinStart** (`0x1407b7ac0`) sets attack state 4 (next attack), or 13 if the
current state is a bow state (above 8). **AttackWinEnd** (`0x1407b7b00`) sets 5
(follow-through), or 14 for a bow, and leaves 0 alone. No call is made; the window
exists so the input system can queue the next attack while it is open. A clip with no
window cannot chain.

**AttackStop** (`0x1407b7b40`) releases the high process's current attack data
(`+0x258`, a `BGSAttackData`, via `0x1406ee160`) and sets attack state 0. Every attack
clip ends with it; without it the actor is still "attacking" for the AI and the next
attack's data is never re-read.

**RecoilStop** (`0x1407b7b80`) clears bits 10–11 of state word 2.
**StaggeredStop** (`0x1407b8280`) clears bit 13. Both are state-only: the engine set
the bit when it started the recoil or stagger, and the clip says when it is over. A
recoil clip that never sends it leaves the actor unable to attack.

**Decapitate** (`0x1407b8a70`) — `0x140685140`: unless already done
(`0x1402e91d0(actor, 1)`), look up the actor's sex from its base (`0x1403a8df0`) and
whether the race decapitates that sex (`0x1403e0be0`); if so, equip the race's
decapitate armour for that sex (`race+0x270+8×sex`, actor virtual `0x57`), fire the
follow-up (`0x1402e9100`) and register the actor with the task list. A race with no
decapitate armour ignores the event.

**KillActor** (`0x1407b8920`) is the paired-kill end for the victim: it asks the
paired-animation manager (`0x143137780`, `0x1405482f0`) for the actors paired with
this one and takes the first that is a `Character` as the killer; if a kill is pending
from flight (below) it resolves that first; then `KillImpl(victim, killer, 0.0, true,
false)` (`0x140664f80`). The corpse ragdolls normally rather than instantly.

**KillMoveStart** (`0x1407b9900`) returns `true` and does nothing.
**KillMoveEnd** (`0x1407b9910`) — `0x1406b9dc0`: resolves the handle at
`Actor+0x108` and if it is set kills the actor by it with the same `KillImpl` call
as KillActor, resolves any flight-pending kill (`0x140715ae0`), and clears bit 14 of
`Actor+0x204`.

**DeathEmote** (`0x1407b9930`) — `0x1406e1c00`: once per actor (flag at high
process `+0x46B`) plays the combat dialogue line of type 3, subtype 33
(`0x1406e1c70`), which is the Death subtype. The line, not the animation, is what the
event carries.

**DeathStop** (`0x1407b9650`) calls actor virtual `0xAA` (`0x140665020`), which
requires a process, life state 1 (dying) and a race not flagged Immobile (race flags
bit 9), then settles the corpse's physics from its 3D. It is the last event of a death
clip; without it the actor stays dying and never becomes dead.

**PairedStop** (`0x1407b97e0`) — if the sit/sleep state is 2 (waiting for the sit
animation) it becomes 3 (sitting) through the furniture path of §6, and then
`0x14077a100` releases the paired interaction if the actor carries the interaction
extra data. Paired furniture entries and paired kill moves both end on it.

## 4. Weapons

**WeaponBeginDrawRight** (`0x1407b7e90`) plays the right-hand weapon's sound at
`+0x1E8` (the draw sound) at the actor, and if the weapon state is below 3 (not yet
drawn) and the actor is the camera target or is riding, tells the player camera that
a weapon is coming out (`0x1408e5850(camera, true)`).

**RightHandWeaponDraw** (`0x1407b7fb0`) is the commit: both hands' weapons are fetched;
actor virtual `0xB4` (`0x14066bd70`) is called for the right weapon with `(true,
false)` and, if the left slot holds the same object the process reports for the left
hand, for the left with `(true, true)`; the actor-state virtual 21 (`0x1406c6f30`) then
sets the drawn state, updates `iMagicEquipped` and the casters; the process refreshes
both hands' 3D (`0x1407159c0`, twice). For the player it also refreshes the equipped
list (`0x14072ec90`), and if the player is riding, pushes the `WarHorseMode` control
layer (`0x14091eda0`), which is the mounted-combat input set.

**WeaponBeginSheatheRight** (`0x1407b7f40`) plays the sound at `+0x1F0` (unequip).
**RightHandWeaponSheathe** (`0x1407b8110`) — `0x14065d2b0`: the same virtual `0xB4`
for each hand with `false`, then the actor-state virtual 21 with `false`. Draw and
sheathe clips that omit the commit event leave the hands drawn as far as the game is
concerned while the animation shows them sheathed; the mismatch is visible as the
next draw doing nothing.

**BowDrawn** (`0x1407b9060`) sets attack state 10. **BowRelease** (`0x1407b9080`)
sets 11. State only.

**BowZoomStart** (`0x1407b93d0`) — unless a global at `0x1430fdaa0` is in a mode that
forbids it (`+0x20 != 0` or `|+0x80|` above a threshold): reads the actor value 78
(`BowSpeedBonus`) and sets it as the global time multiplier (`0x140cc97e0` on
`0x1431cc270`), which is the Steady Hand slow-motion; evaluates perk entry point 20
(`ModBowZoom`) for the equipped weapon (`0x140385f30`) and if it is non-zero sets the
camera's zoom flag at `+0x161`, plays the misc dialogue subtype 96 (`0x14067c1d0`,
`EnterBowZoomBreath`) and for the player enables a control state (`0x1407440c0(…, 8)`).
**BowZoomStop** (`0x1407b94b0`) resets the multiplier to 1.0, and if the flag was
set plays subtype 97 (`ExitBowZoomBreath`), clears the flag and the control state.

**ArrowAttach** (`0x1407b90a0`) takes the right-hand weapon (`0x14070db20(process,
0)`, the bow) and the current ammo (actor virtual `0x9E`, `0x1406b9c80`); if the
weapon is a bow (animation type 7) and the ammo is an arrow, or a crossbow (9) and
the ammo is a bolt (ammo flags at `+0x118`, bit 2 is *non-bolt*), it attaches the arrow
to the biped (virtual `0x7F` for the biped, virtual `0xCD` to attach, which names the
node `Arrow%d`) and sets attack state 9. A wrong ammo type is silently not attached.

**ArrowDetach** (`0x1407b9160`) detaches (virtual `0xCE`) if the attack state is 9 or
above, or if the same weapon-and-ammo match holds, and sets attack state 0.

**ArrowRelease** (`0x1407b9230`) requires a bow or crossbow and attack state 9 or
above. It takes the weapon's enchantment (instance extra data, else the base record;
`0x1401dc910`), computes its per-shot cost (`0x14014c1f0`), and if the actor's
`RightItemCharge` (actor value 64) covers the integer part, subtracts it (actor-value
virtual 6 with modifier 2); otherwise the shot is unenchanted. It then launches the
projectile (`0x140286c90(weapon, actor, 0, enchantment, ammo-entry)`), sets attack
state 12, and breaks invisibility and ethereal form (`0x1406c6d90(actor, -1)`). The
same name `arrowRelease` is also in the engine's string table, where the animation
data code reads its time (`0x140537f20`); one annotation serves both.

## 5. Magic

The six cast and fire handlers share two functions. **Cast** (`Left…` `0x1407b7d60`,
`Right…` `0x1407b7db0`, `Voice…` `0x1407b7e00`) gets the caster for the source (actor
virtual `0x5C`, `0x1406c20d0`; left 0, right 1, voice 2), and only if the caster's
state at `+0x30` is 1 sets it to 2 and starts the charge (`0x1405bf030`: caster
virtual 4, then `0x1405bef00(caster, 1)`); it returns whether a spell is now held at
`+0x28`. **Fire** (`0x1407b7ba0`, `0x1407b7bf0`, `0x1407b7c40`) requires a held spell
and releases it (`0x1405bbd40(caster, 0, true)`), returning its result.

The voice pair adds the shout bookkeeping on the high process: VoiceSpellCast sets the
shout state at `+0x00` to 2 when the cast started; VoiceSpellFire requires that state
to be 4, fires, sets it to 5, and computes the cooldown as the variation's recovery
time (`TESShout` variation at `+0x60+0x18×level`) times actor value 86
(`ShoutRecoveryMult`), stored by `0x1406ed560`; for the player it also updates the
shout meter (`0x140381010(level)`). If there is no spell to fire it clears the shout
(`0x1406ed3b0`) and the executable carries the message it would log:
`VoiceSpellFireHandler::executeHandler - MagicEventHelper::ReleaseCastForActor
failed.`

A clip fires the casts as `BeginCastLeft`, `BeginCastRight` and `BeginCastVoice`,
and the fires as `MLh_SpellFire_Event`, `MRh_SpellFire_Event` and
`Voice_SpellFire_Event`; the three fire names are also in the engine's string table,
where the animation data code reads their time (`0x140537f20`). The shipped
behaviours fire the three fires 104 times and the casts once, so the cast handler is
nearly always reached by the engine's own send rather than a clip.

**InterruptCast** (`0x1407b95d0`) calls `InterruptCast(refund)` on all four casters at
`Actor+0x1A8` (`0x1405bbfa0`), with `refund` true unless the actor is staggered (state
word 2 bit 13): a stagger interrupts without giving the magicka back.

**EndSummonAnimation** (`0x1407b9600`) — `0x1406c5c20`: resolves the handle the
process is holding for the pending summon (`0x140714370`) and completes it
(`0x1406c5cb0(actor, summoned, 0)`). The summoned creature appears at this event, not
at the spell's fire.

**VampireFeedEnd** (`0x1407ba990`) — if the actor's current package
(`0x14068ea90`, `AIProcess+0x20`) has procedure type 27 at `+0xD8`: asks the high
process to pick a new idle (`0x1406de6d0(process, true)`) and sets bit 1 of
`AIProcess+0x136`.

## 6. Furniture, idles and attachment

Four handlers share one path. With a process at level 0 or 1 (`AIProcess+0x137`), they
call `0x140711b60(process, actor, newState, actorHandle, furnitureMarker)`, which
writes the sit/sleep state and does the furniture bookkeeping (the marker comes from
`0x140712780` when the process holds a furniture handle at `0x1407127c0`, else −1);
with a lower-level process they write the state directly through actor-state virtual
20.

- **ChairEnter** (`0x1407b82a0`): unless the state is already 4, set 3.
- **BedEnter** (`0x1407b8500`): unless already 8, set 7.
- **ChairFurnitureExit** (`0x1407b86e0`): if the state is 1 to 4, set 0; otherwise
  return `false`.
- **BedFurnitureExit** (`0x1407b87b0`): the same for the bed range.

The **Player** variants (`PlayerChairEnter` `0x1407b8390`, `PlayerBedEnter`
`0x1407b85f0`, `PlayerFurnitureExit` `0x1407b8880`) do the same and additionally put
the player camera into or out of furniture mode (`0x1408e3a10(camera, bool)`), and
for the chair pair switch the input layer (`0x140cd56e0`/`0x140cd5650(…, 0x40)` on
enter, `0x140cd5700` on exit) and register or clear the furniture reference with the
HUD (`0x14097adb0`).

**StopMountCamera** (`0x1407b9750`): if the sit/sleep state is 4 (the dismount has
been requested), set it to 0 by the same path; then `0x1406bfb10(actor)`, which
finishes the dismount from the actor's 3D and controller and clears the interaction
flag (`0x140673960` is among its calls).

**PickNewIdle** (`0x1407b9620`) sets the high process's flag at `+0x455` to true
unless `+0x456` is set, in which case it clears that instead (`0x1406de6d0`). The
idle system reads the flag on its next pass.

**IdleDialogueEnter** (`0x1407ba780`) does nothing.
**IdleDialogueExit** (`0x1407ba790`) takes the dialogue idle and its target the
process is holding (`0x1406ec820`, `0x1406ec860`) and plays it with flags `0x40`
(`0x1406dde70(process, actor, 0x40, idle, true, true, target)`), which is how the
idle that was queued during a line starts once the dialogue idle ends.

**ActionActivateDone** (`0x1407b9670`) resolves the activation target the process
recorded (`0x1406e4230`), clears it (`0x1406e4260`), activates it as the actor
(`TESObjectREFR::Activate(target, actor, 0, 0, 1, false)` at `0x1402eac20`), and
asks for a new idle. The pick-up, lever and door idles activate at this event, so a
clip without it plays the reach and picks up nothing.

**NPCAttach** (`0x1407ba330`; reads the payload). Resolves the process's furniture
handle to its reference, finds the node named by the payload in that reference's 3D
(`0x140e18620`), and attaches the actor to it (`0x1402fe880(actor, ref, node, …,
true)`). It is how a rider sits on the cart or the horse: `NPCAttach.<bone>`.
**NPCDetach** (`0x1407ba400`) detaches (`0x1402fe9d0`).

**ExitCartBegin** (`0x1407ba2d0`) detaches and then asks the movement controller for
its `IMovementSetTweener` interface (name at `0x1432a7488`) and calls its slot 2.
**ExitCartEnd** (`0x1407ba300`) —
`0x140699080(actor, invalidHandle)`: clears the attached-to handle at `Actor+0x1F0`,
resets the process (`0x1406cf0e0(actor, 0)`) and detaches again.

**MountDismountEnd** (`0x1407ba250`) fetches the mount (`0x1406c0800`) and clears the
interaction flag, bit 3 of `Actor+0x204`, on both rider and mount
(`0x140673960(x, false)`), which also tells each process the interaction ended.

**AnimationObjectLoad, AnimationObjectDraw** (`0x1407ba560`, `0x1407ba5e0`; read the
payload). The payload is looked up as an editor id (`0x1401e0250`); if it names an
`ANIO` (form type `0x53`) the animated-object manager at `0x1420f6a08` loads
(`0x1407c32b0`) or shows (`0x1407c3570`) it for the actor's handle. Any other payload
is ignored. The book, the broom and the tankard are these.

## 7. Ragdoll and recovery

**AddRagdoll** (`0x1407b9960`). Takes the cell's Havok world (`Actor+0x60 →
0x1402b8d30`), the character controller's collision group (controller virtual 8, high
16 bits), and the actor's 3D; under the graph manager's lock it visits every behaviour
graph on the actor (`+0x48` array, `+0x50` count) with `0x14054e280`, then adds the
ragdoll to the world (world virtual `0x35(root, true, true, group, false)`, or the
queued `0x140655a00`). This is the moment the second skeleton in `skeleton.hkx`
becomes live.

**RemoveRagdoll** (`0x1407b9db0`) removes it (`0x140e87150(root, true, false)`, or
queued `0x140655a90`).

**RagdollStart** (`0x1407b9e30`) visits every graph with `0x14054e3a0` and a
`{1.0f, false}` argument, which sets the ragdoll's drive to full, clears bit 29 of
`Actor+0x204`, and detaches the character controller from the world
(`0x140e9e9c0(controller, null)`: removes its body from the old world and sets none).
From here the ragdoll moves the actor.

**GetUpStart** (`0x1407b9fc0`) reads the graph variable `iGetUpType`
(`0x1420f6370+0x590`, graph-holder virtual 17). It re-attaches the controller to the
cell's world (`0x140e9e9c0(controller, world)`), places it at the actor's position
scaled to Havok units (controller virtual 3) with the actor's yaw as a quaternion
(`0x140ea06e0`), zeroes the pitch pair at `+0x24C`, and sets the wanted character
state at `+0x21C` to 5 (swimming) if `iGetUpType` is 2, else 2 (in air). Then by type:
0 sets bit 4 of state word 2; 1 sets the knock state to 6 (getting up); 2 calls actor
virtual `0xE5` (`0x1406b70a0`) and then also sets 6.

**GetUpEnd** (`0x1407ba170`) reads `iGetUpType` again, removes the ragdoll (queued
`0x140655a90`), and for type 1 or 2 finishes the get-up (`0x140655920`); for type 0 it
calls `0x1406c6e00(actor)` and re-layers the 3D's collision (`0x140655b10(root, 8)`,
`0x140655ad0(root, 4, 0)`).

**JumpAnimEvent** (`0x1407ba6d0`) — with a controller that has a supporting body
(controller virtual 15) and an actor that is not riding (`0x140280270`): jump height
is the reference scale (`+0x98`, in hundredths) times the race's jump height
(`0x1403bf500` on the base `NPC_`), times a constant; if the controller's character
state (`0x140b7ae60` on the context at `+0x1E0`) is 5, swimming, and bit 1 of
`Actor+0x204` is clear, it is multiplied again; then `0x140e9ea30` sets the wanted
state to 1 (jumping) and stores the height at `+0x23C` in Havok units. The jump
happens here, not at the clip's start.

## 8. Flight

**FlightTakeOff, FlightCruising, FlightHovering, FlightLanding, FlightPerching,
FlightLand** (`0x1407b8ac0`, `0x1407b8b10`, `0x1407b8b60`, `0x1407b8bb0`,
`0x1407b8c00`, `0x1407b8c50`) set the fly state to 1, 2, 3, 4, 5 and 0 respectively
(`0x14069be40(actor, state)`, or queued `0x1406567c0`). The setter ignores a state
equal to the current one; for a new state other than none and perching it also raises
a flag and value on the process. **FlightAction** (`0x1407b8cc0`) sets 6 and first
marks the flight action begun (`0x140691ea0(actor, true)`); **FlightActionEnd**
(`0x1407b8d60`) marks it ended (`…, false`) and does not change the fly state.
**FlightActionEntryEnd** (`0x1407b8d40`) — `0x140691cd0(actor)`: the action's entry
is over and its target may be taken.

**FlightActionGrab, FlightActionRelease** (`0x1407b8db0`, `0x1407b8dd0`; pass the
payload) tail-call `0x1407babc0(actor, payload, grab)` with `true` and `false`: the
dragon's grab of an actor and its release, with the payload naming the dragon's bone
that holds it. The one vanilla sender is `CLIP_Flight_Grab` in `dragonbehavior`,
which fires both with the payload `NPC RLegFoot`.

**FlightLandEnd** (`0x1407b8ca0`) — `0x1406b9ed0(actor, false)`, which is
`0x140715ae0(process, 0, actor)`: if the middle-high process holds a pending-kill
count at `+0x2F8` above 1, kill the actor now, by the player (`KillImpl(actor,
player, 0.0, true, false)`), and clear the count. A dragon killed in the air is not
dead until it has landed, and this is where it dies. KillActor and KillMoveEnd resolve
the same count.

**FlightCrashLandStart** (`0x1407b8df0`). If `0x140693050` yields an object for the
actor whose type id matches `0x1404ef3e0` (the flight-related object the dragon
carries), the actor is re-placed from it (`0x1402ea960`, `0x1402ea6a0`) and the
reference it holds at `+0x128` is flagged (`0x1401e14a0(ref, 0x10, true)`). Then a
`BSTerrainEffect` (vtable `0x14185e878`, 0xB8 bytes, constructor
`0x1404afa20`) is created for the actor's handle and cell; if it accepts the actor
(effect virtual `0x36`), the node `dragonMoveBone` is found in the effect's 3D and
attached (`0x1404b00f0`), and the effect is registered with the effect manager at
`0x1420f69b0`. This is the ground-tearing trail of a crashing dragon; the string
`dragonMoveBone` at `0x1418bce38` is the only bone the handler names.

## 9. Camera

**AnimatedCameraStart, AnimatedCameraDeltaStart** (`0x1407ba860`, `0x1407ba890`; read
the payload) call `0x1408e42d0(camera, payload, delta)` with `false` and `true`. The
function refuses if the camera is in either of two states (`PlayerCamera+0xE8`,
`+0xD0`), otherwise takes the player's 3D (virtual `0x6F` on the player at
`0x1431874f8`) and copies the payload as the name of the camera animation to run
(`0x140cec680`); the delta form is the same call with its flag set.
**AnimatedCameraEnd** (`0x1407ba8c0`) ends it (`0x1408e4460`).

**CameraOverrideStart, CameraOverrideStop** (`0x1407b8130`, `0x1407b8170`), only when
the actor is the camera's target (`PlayerCamera+0x3C`), set or clear the override
(`0x1408e5930(camera, bool)`).

**CameraShake** (`0x1407b98b0`; reads the payload). If the payload is non-empty and
parses as a number above zero (`atof` through the import at `0x14174fc90`), shakes the
camera with that strength from the actor's position (`0x1405506d0(strength, &pos)`).
`CameraShake.0.5` is the whole interface.

## 10. What the census got wrong

The census file said that a functor with no calls does nothing. Of its twelve, seven
write actor state without calling anything: AttackWinStart, AttackWinEnd, BowDrawn,
BowRelease, RecoilStop, StaggeredStop and HeadTrackingOn set or clear bits in the
state words. DeathStop calls a virtual, which the summary does not list, and
FlightActionGrab and FlightActionRelease tail-jump to their worker. The two that
truly do nothing on receipt are **KillMoveStart** and **IdleDialogueEnter**; even
PickNewIdle sets a flag. A clip may still send those two, since scripts can wait on
them.

And `0x1407c2c10` is not a setter shared by the motion-mode three; it is the lookup
of `IMovementMotionDrivenControl`, and each event calls a different method on it.

The larger error is older and in `docs/animation-variables.md` §4, corrected there:
the event a clip fires is not the class name without `Handler`. It is the left column
of the response files, and the "string-table names the code compares for a time"
(`HitFrame`, `weaponSwing`, `arrowRelease`, the three spell fires) are the same names
seen from the other side: the animation data code reads their times, and the
response file routes them to handlers.

## 11. Authoring consequences

- **State is set by the clip, not the engine, for every "Stop" event.** RecoilStop,
  StaggeredStop, AttackStop, DeathStop, PairedStop, BowZoomStop, FlightActionEnd:
  the engine sets the state on the way in and waits for the clip to say it is over.
  A new creature's recoil, stagger, attack and death clips must carry them, or the
  actor is stuck in that state for the AI.
- **The hit is a state machine.** swing (2) → HitFrame (3) → AttackWinStart (4) →
  AttackWinEnd (5) → AttackStop (0), and HitFrame refuses to land unless the state is
  swing. Order matters and each step is a separate annotation.
- **Bows are their own machine.** ArrowAttach (9) → BowDrawn (10) → BowRelease (11)
  → ArrowRelease (12) → AttackWinStart (13) → AttackWinEnd (14), and ArrowRelease
  refuses below 9. A new ranged creature that is not a bow or crossbow by weapon type
  gets none of this.
- **Payloads are the interface for six families.** `NPCAttach.<node>`,
  `AnimObjLoad.<editorId>` and `AnimObjDraw.<editorId>`, `CameraShake.<number>`,
  `StartAnimatedCamera.<name>`, `FlightActionGrab.<bone>` and
  `FlightActionRelease.<bone>`, and `HitFrame.Left` / `preHitFrame.Left` for the off
  hand. Nothing else reads its payload.
- **The names come from the response files.** A new creature fires the left column
  of `actorresponse.txt`, not the handler's class name; `RagdollStart` and
  `StopMountCamera` reach nothing. A mod may ship its own response file to add
  names, since the file is read by path, but the right column can only name the 93.
- **Furniture, flight and get-up read the actor's own state first.** ChairEnter does
  nothing when the state is already 4, the fly-state setter ignores a repeat, and
  GetUpStart branches on `iGetUpType`. Set the graph variable before the event.
