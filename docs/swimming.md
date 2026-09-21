# Swimming and combat in water: the engine, the graphs, the player

The companion of `docs/flight.md`, asked the same way: what the engine does when an
actor is in water, what the graphs do with it, who may fight there, and what that
leaves the player. Evidence: the executable (AE layout, addresses as in
`docs/animation-events.md` §1), the masters through Mutagen (races, idles, actions,
movement types), and the graphs through HKX2 (the player's `0_master`, the
`quadrupedbehavior` shared by bear, horker and others, `slaughterfishbehavior`,
`mudcrabbehavior`).

## 1. The engine's side

**The swimming state** is bit 10 of the actor's first state word (`Actor+0xC8`). One
setter writes it, `0x14069bd10(actor, bool)`, and when the value changes it does one
thing: it requests the default-object action `Action Swim State Change`
(`ActionSwimStateChange` in the masters) through a `TESActionData`, which the idle
tree resolves; for the player the request goes through the player-controls path
(`0x14079b580`, code `0x2B`) instead of the action data directly. There is no
graph variable for swimming in the engine's 189-name table: the graph learns it
only from the event the idle tree sends, and reads it back from nothing.

**The decision** is made every frame in the actor's water update, `0x140667d40`,
the only caller of the setter besides the player's fast-travel and mount code. It
takes the water height at the actor from the water system (`0x14048f560` and its
neighbours), subtracts the actor's position, and compares the depth with **1.5 ×**
a per-actor size cached on the loaded-reference data (`Actor+0x68 → +0x20`, read by
`0x1402ef290`; the same value the bumper handlers raise). Deeper than that, the actor
swims. Before deciding it asks `0x1406610a0(actor)`, which is *Walks and neither
Swims nor Flies*: a race that cannot swim is not put into the swimming state, it
wades until it drowns. The same update reads `fActorSwimBreathBase`,
`fActorSwimBreathMult` and `fActorSwimBreathDamage` through their getters
(`0x14041a080`, `0x14041a0a0`) and so owns the breath meter and the drowning damage.

**The character controller** follows the actor. The water update writes wanted
state 5 into the controller (`controller+0x21C`) at three sites; the 3D-load path
(`0x14065d590`) does the same for an actor that spawns in water. State 5 is
`bhkCharacterStateSwimming`, whose simulate step (`0x140f02b30`) is the buoyancy:
it keeps the controller at a fraction of its height under the surface, sets flag
`0x400` in the controller's flags at `+0x218`, and hands a jump (wanted state 1)
through with `fJumpSwimmingMult`, which the `JumpBegin` handler also applies
(`docs/animation-events.md` §7). Swimming is therefore two things kept in step by
one update: a bit on the actor for the AI, the graph and the conditions, and a
Havok state on the controller for the physics.

**The movement type.** The movement-mode function of the flight document
(`0x140694250`) returns 2 for a swimming actor, the `Default MovementType: Swim`
object, unless the actor is flying. The graph then names the real one through
`iState_<MNAM>`: `NPCSwimming` for the player and NPCs (walk 80, run 370, rotate
180°/s), `HorkerSwimDefault`, `BearSwimDefault`, `iState_CowSwimDefault`, `HorseSwim`,
`SlaughterfishSwim` (walk 180 forward, run 360, turn 180 and 270°/s). `fMoveSwimMult`
scales the requested speed (`0x1404196a0`).

**Who may swim, and who may fight there.** Two race flags, both read by small
getters the AI and the combat code call:

- `Swims` (race flags bit 6, `0x1406610e0`): the race may enter deep water. Every
  playable race, the vampire and child variants, wolves, dogs, deer, sabre cats,
  cows, bears, werewolves, mudcrabs, horkers and slaughterfish carry it. Horses,
  draugr, skeletons, trolls, giants, spiders, atronachs, dwarven automata, dragon
  priests, spriggans, chaurus, netches, lurkers, rieklings and dragons do not, and
  the pathing keeps them out: the `Walks && !Swims && !Flies` getter is what the
  water pathing (`0x140464ec0`, `0x140468f10`, `0x14048de50`) asks.
- `NoCombatInWater` (bit 11). `0x140661140(actor)` is *Swims and not
  NoCombatInWater*, the "may fight while swimming" question. Only four races answer
  yes: **mudcrab, slaughterfish, horker, and the Solstheim mudcrab**. Every other
  race with `Swims`, the player's included, carries `NoCombatInWater`.
- The slaughterfish is the one race with `Swims` and not `Walks`. The larger
  predicate `0x140661b20` treats *Swims && !Walks && !Flies* as a water-only actor
  and answers before looking at the water at all, which is how a fish is never asked
  to leave.

**Where the water-combat flag bites.** `0x140661140` has three callers:

- `0x1406cf1b0`, called from the action mediator (`0x1406cd0d0`, `0x1406cd410`): an
  action requested for a swimming actor whose race cannot fight in water is refused
  at the mediator. This is what stops the player attacking, bashing and casting in
  deep water, before any graph or idle condition is consulted, and why the
  `SwimmingForceEquip` idle can sheathe on entry.
- `0x14071c020`, from `0x1406e2970` in the process code, the same question for the
  AI's own requests.
- `0x1408c4b90`, called from about thirty combat functions, which is where the
  combat AI decides whether a swimming actor, or a target in water, is worth
  attacking. `fCombatTargetLocationSwimmingOffset` (`0x140844310`) moves the aim
  point of a swimming target, since only its head is above the surface.

For an actor that cannot fight in water the combat AI has one tree,
`CombatBehaviorTreeExitWater`, a single node named `Exit Water`: leave the water
first, fight after. Ranged spells are refused underwater with the message
`sMagicCastRangedUnderwater`. Neither is the graph's business.

**Waterwalking** is `Action Waterwalk Start`, `ActionWaterwalkStart`, a separate
action for the water-walking effect (`0x14043c1e0` beside `Aggression` and
`Frenzied`), which keeps the actor out of the swimming state by lifting the
controller instead.

## 2. The graphs' side

**The event.** `ActionSwimStateChange` has one idle root per creature family, each
with two children: a start whose only condition is `IsSwimming == 1`, and a stop
with none.

    SwimRoot          -> SwimStart / SwimStop            (the player and NPCs; DefaultSwim, IsRidingMount == 0)
                         MountedSwimStart / MountedSwimStop   (riding)
    HorseSwimRoot     -> MountedSwimStart / MountedSwimStop
    WolfSwimRoot, DogSwimRoot, DeerSwimRoot, BearSwimRoot, CowSwimRoot, CrabSwimRoot,
    SkeeverSwimStateRoot, FalmerSwimRoot, WerewolfSwimRoot -> swimStart / swimStop
    horkSwimRoot      -> HorkerSwimStart / swimStop
    FishTurnLeft/Right (ActionTurnLeft/Right) -> SwimLeft / SwimRight
    FishCombatStart (the fish's draw)          -> combatSwimStart

So the graph sees `SwimStart` and `SwimStop`, or the family's spelling, and nothing
else; there is no depth, no surface height, no "underwater" event. The `Swimming`
condition function and the Papyrus `IsSwimming` read the actor bit, not the graph.

**The player** (`0_master`): `Swim_State` is a top-level state beside
`Default_State`, entered by the wildcard `SwimStart` (and `SwimStartFromRagdoll`),
with `InterruptCast` as its entry event, left by `SwimStop`. Inside it `Swim_Behavior`
is two states, `SwimIdle` and `SwimLocomotion`, switched by the ordinary `moveStart`
and `moveStop`; the locomotion is a `BSCyclicBlendTransitionGenerator` over four
directions in third person and one clip in first. Around them `SwimUnequip` sends
`weaponSheathe` on entry and `SwimForceEquip` sends `weaponDraw`, driven by the
`swimForceEquip` idle when the actor enters water with a weapon out. The machine
has **no attack, block, cast, sneak or sprint transition**: swimming is a posture
with locomotion and nothing else, and the mediator has already refused the actions
before the graph could be asked. `HorseEnterSwim` and `HorseExitSwim` route the
rider through the mounted swim, whose horse side is `HorseSwim` at 210 forward.

**The quadrupeds** (`quadrupedbehavior`, shared): a `HorkerSwimState` entered by
the wildcard `HorkerSwimStart` under a condition and left by `swimStop`, holding an
idle-and-turn machine, a locomotion blend, canned 90° and 180° turns, and a
`SwimAttack` state entered by `attackStart_HorkerSwim` and left by `attackStop`.
The horker is the shipped example of a creature that **fights while swimming**: its
race allows it, its idle tree asks for the attack, and its swim machine holds the
attack. The bear's swim is the same machine minus the attack, with `iState` chosen
by an expression, `cond(isSwimming == 1, iState_BearSwimDefault, iState_BearDefault)`,
where `isSwimming` is a graph variable the swim state's own `BSIsActiveModifier`
sets: the graph keeps its own copy of the state it was told about.

**The slaughterfish** has no swim state because it has nothing else. Its root is
`NonCombatState` / `CombatState` (entered with `weaponSheathe` and `weaponDraw`,
switched by `combatSwimStart` / `combatSwimStop`), a default machine of idle,
canned turns and a directional locomotion blend, attack, stagger and recoil states,
and one `iState_SlaughterfishSwim`. The engine never sends it `SwimStart`; it is
always in water, the race says so, and the controller keeps it there.

**The mudcrab** has swim idles in the masters (`CrabSwimStart`, `CrabSwimStop`)
and no swim machine in its graph: the events arrive and match nothing, and the crab
plays its walk in the water at its walking speed. Its race allows combat in water,
so it attacks from that walk. That is the smallest possible swimming creature.

## 3. What a new creature needs

- **`Swims`** on the race, or the pathing keeps it out of deep water and the water
  update never puts it into the swimming state.
- **`NoCombatInWater` clear** if it should fight there. That single flag is the
  whole permission: the mediator, the AI and the combat tree all read it, and the
  graph is never consulted. With it clear, the idle tree can request attacks while
  swimming and the graph's swim machine has to hold them, as the horker's does.
- **A swim idle root** under `ActionSwimStateChange` sending the start and stop
  events the graph expects, with `IsSwimming == 1` on the start. Without it the
  graph never hears about the water and swims in its walk, which is what the
  mudcrab does and is acceptable for a creature whose walk reads as a swim.
- **`iState_<MNAM>`** naming a swim movement type, or the creature swims at its
  walk speed.
- For a water-only creature, `Swims` without `Walks`, as the slaughterfish: the
  engine then never asks it to leave the water and never sends it the swim events.

What is free: everything the swim machine holds, the depth it sits at (the
controller's buoyancy is the engine's, but the skeleton's root offset is the
creature's), whether it fights, and how it turns. What is not: there is no diving.
The swimming state is a surface state; the controller holds the actor at the
surface, the swim rule is a depth *above the feet*, and nothing in the engine, the
graphs or the idles distinguishes a submerged actor from a floating one except the
player's underwater camera and breath. A creature that should swim below the
surface can only do so by being a slaughterfish, that is by never being a walker.

## 4. The player

What the player gets from the same machinery: the swimming state at 1.5 × its
size in depth, the swim posture, the buoyant controller, the breath meter, the
mounted swim, and the refusal of every combat action at the mediator because the
playable races carry `NoCombatInWater`.

**Underwater combat for the player** is therefore two record edits and one graph
edit away, and one engine limit short:

- clearing `NoCombatInWater` on the playable races lifts the mediator's refusal,
  the AI's, and the combat tree's; the "cannot cast ranged spells underwater"
  refusal is separate and stays;
- the player's `Swim_State` then has to hold attack, block and cast transitions of
  its own, as the horker's swim machine holds its attack; the shipped one has none;
- the limit is the surface. The controller's swimming state keeps the player
  afloat, and no input, variable or event takes the player down. A submerged player
  is the same problem as a flying one (`docs/flight.md` §4.1): a state the graph
  declares, movement by root motion under `bAnimationDriven`, and steering from
  `Direction`, `Speed` and `Pitch`. The difference is that the swimming controller
  state applies buoyancy, so the graph would be fighting the controller unless the
  dive leaves the swimming state for the flying one, which has no forces at all.

That last route is the interesting one: a dive is a flight machine that sends
`FlightCruising`, entered from `Swim_State`, with the water update overruled by the
graph's own state. Whether the water update re-asserts swimming every frame while
the actor is below the surface, and so fights the graph, is the one thing only a
run of the game can settle.

## 5. How this was measured

Race, idle, action and movement-type records were read from the five masters with
Mutagen. The graphs were dumped with HKX2 through `BehaviorFile.Load`. The
executable was searched for the swimming bit's writers and readers, the race-flag
getters (bits 6, 8, 7 and 11), the water and breath game settings by their setting
records, the `bhkCharacterStateSwimming` vtable, and the combat classes with *water*
in the name; the functions found were read with `index.py dump`. The swim-depth
rule and the mediator refusal are read from the code; the player's underwater
routes are reasoning from it, not a run of the game.
