# speeddatasinglefile.txt — the speed sampler database

What the file is for, what the engine does with it, how every part of it is
recovered from the other assets, and how to author the movement-type records the
engine pairs it with. The measurements behind each statement, and the dead ends,
are in `docs/speed-data-research.md`; this document states the results.

Section numbers are stable: code comments and the other documents cite them.

## 0. Purpose

A creature's speed is written down twice.

The **movement type** (`MOVT`, in the masters) says how fast the game may ask the
creature to move: a walk and a run speed for each of forward, back, left and right.
The AI and the player's controller request speeds on that scale, and the character
controller moves the actor's capsule at the request.

The **animations** say how fast the creature actually moves. A locomotion clip
carries root motion, a travel over a duration, and the behaviour graph blends such
clips on a *speed ladder*: a parametric blend whose arms sit at the movement type's
speeds and whose parameter is the requested speed.

When the two agree, the feet and the capsule travel together. They drift: an
animator retimes a clip and nobody revisits the record; a blend between two clips
of different duration is not linear in speed. The chicken's run is recorded as
251.94 and the clip delivers 403.

**This file is the measured relation between the two.** For every locomotion
state, every heading and every requested speed it records the speed the animation
will deliver. The engine reads it every frame through `BSSpeedSamplerModifier`,
feeds the answer back into the graph as the ladder's parameter, and keeps a copy on
the actor. For a correctly authored creature the table is the identity; its
departure from `y = x` is the animator's error, plus one term that is not an error
at all (§6).

## 1. On-disk format

Little-endian. The dirlist is CRLF text; everything after it is 32-bit words.

    file    := dirlist block[count]                  /* blocks in dirlist order */
    dirlist := "<count>\r\n"  count x "<Project>Data\<Project>.spd\r\n"

    struct block  { u32 version /* 1 */; u32 n_entries; entry entries[n_entries]; }
    struct entry  { u32 key; u32 n_records /* 19, or 0 if malformed */; record records[n_records]; }
    struct record { f32 direction; u32 n_points; struct { f32 x; f32 y; } points[n_points]; }

No padding. Blocks follow the dirlist in its order with no index or length prefix,
so a reader walks them in sequence. The shipped file: 49 blocks, 88 entries, 1,634
records, 18,302 points, 162,527 bytes. `HKSK.Cache.SpeedDataFile` reads and writes
it byte-exact.

## 2. Invariants

    I1  version == 1
    I3  n_records == 19 on every well-formed entry
    I4  direction[i] is the float ACCUMULATION of +0.05f, i = 0..18  (bit-exact;
        0.05f * i differs from it from i = 7 and the game's lookup then misses)
    I5  the last direction is 0.90; 0.95 never appears
    I6  x is a multiple of 0.5
    I7  x is non-decreasing within a record
    I8  the 19 records of an entry share one max(x)

Not invariant: `y` need not be monotonic and may exceed `x`; the records of an entry
need not share a minimum `x`; a record's last point is often written twice and that
is not a plateau marker. A reader must tolerate `n_records == 0` (§10).

## 3. Fields

### 3.1 key — the locomotion state

The graph variable `iState`. The graph declares the values it can take as
`iState_<name>` integer variables in the project's **root** behaviour graph, one per
movement type, each initialised to the state id:

    iState_DeerDefault = 20      iState_DeerDefaultRun = 21      iState_NPCBowDrawn = 3

Read them from `hkbBehaviorGraphData.m_stringData.m_variableNames` paired by index
with `m_variableInitialValues.m_wordVariableValues`, and from the root graph only:
shared templates such as `quadrupedbehavior.hkx` carry the union for every creature
that references them and disagree with two of them.

The `<name>` is the movement type's **`MNAM` name field**, not its editor id. That is
what the engine matches (§4.5); 101 of the 103 names the root graphs declare are an
`MNAM` exactly, and the two that are not resolve to nothing in the game as well.

**The graph writes `iState`; the engine reads it.** Four things write it, and a key
is live in the engine exactly when one of them can reach it (`StateKeys.Writable`):

| writer | what it says |
| --- | --- |
| the variable's initial value | the state the creature rests in |
| `BSiStateTaggingGenerator.m_iStateToSetAs` | the subtree it guards carries this key |
| a `BSIStateManagerModifier` row | (machine, state) → key |
| an expression, `iState = <constant>`, `cond(...)`, or `<constant> + <var>` | a computed key |

A constant that is declared and never written is dead: the dog declares 30 and 31
and writes only 30. Two shipped blocks are dead that way, the spriggan's and the
lurker's key 1.

### 3.2 direction — the heading

The graph's `Direction` variable, a fraction of a turn: 0 ahead, 0.25 right, 0.5
behind, 0.75 left. The 19 records sample every 0.05; the graph's compass blends sit
every 0.125, so 15 of 19 records are a blend of two adjacent arms.

### 3.3 x and y — one axis

`x` is the requested speed, `y` the delivered speed, both in game units per second on
the movement type's scale. `x` is not a time or an index.

## 4. The engine side

Read out of `SkyrimSE.exe` (unwrapped for reading, §11). Addresses are RVAs.

### 4.1 The gate

`bUseSpeedSampler:Animation` defaults **on** in the compiled settings; no shipped INI
names it. The data is live for every actor whose graph carries the modifier.

### 4.2 The query

`BSSpeedSamplerModifier::Update` (`0xb9f000`) reads its four bound members and calls
the database singleton's second virtual, `Query(context, state, direction,
goalSpeed) -> float` (`0xbc0e30`), storing the result in `speedOut`. Nothing is
computed at runtime:

1. **state** → the entry whose key matches exactly; no match returns `goalSpeed`;
2. **direction** → exact match, else the **first record above** the request; past the
   last record it **wraps to record 0**. Records are never blended;
3. **goalSpeed** → the first point at or above the request. **Below the first point
   the curve is interpolated from the origin (0, 0). Above the last point the request
   is returned unchanged**, which is the same as having no table;
4. a linear interpolation between the two bracketing points.

No offset is applied to `goalSpeed` anywhere. In memory a point is `{y, x}`; the
loader swaps the on-disk order.

With no database the call is skipped and `speedOut = goalSpeed`.

### 4.3 The load path

The merged file `Meshes/SpeedDataSingleFile.txt` is read ungated inside the
manager's constructor. A per-project `MESHES/SPEEDDATA/<Project>.SPD` is read
lazily behind the gate; the game ships none, and a mod may add one creature that way.

### 4.4 Nothing writes a table

Every reference to the path literals, the manager, the query singleton and both
virtual tables is construction, destruction, loading or the query. The sampler that
recorded the shipped file was an external tool, and its habits (§6.2, §8) are not
the engine's.

### 4.5 The graph names the movement type, and the engine reads the answer back

At graph load, `0x140bc5890` scans the graph's variable names for the `iState_`
prefix and keeps each suffix against the variable's initial value. Whenever the
movement type may have changed, `0x14069ba40` reads `iState` from the graph
(`0x140bb3b20`), turns the value into that suffix, looks it up by name
(`0x14038c710`) and, when a movement type of that name exists, applies it to the
actor (`0x14069b860`). The `MTState` animation event, raised by seven graphs, only
marks the actor's process so that the next update looks again.

So the direction is the one `bAnimationDriven` has: **the graph tells the game which
movement type it is in.** The race's six base movement defaults (walk, run, swim,
fly, sneak, sprint) are what the actor has when the graph names nothing.

The engine also keeps the sampler's answer per actor. `SpeedSampled` and
`HorseSpeedSampled` are registered as
`BSTAnimationGraphDataChannel<Actor, float, ActorCopyGraphVariableChannel>`, a
channel polled each frame that reads the graph variable into the actor's side. So a
wrong `y` is wrong twice: as the ladder's parameter and as what the actor believes
it delivers. `Speed`, `Direction` and `TurnDelta` go the other way, actor to graph.

## 5. The graph side

### 5.1 The sampler

    BSSpeedSamplerModifier
        state      <- iState
        direction  <- Direction
        goalSpeed  <- Speed
        speedOut   -> SpeedSampled | SampledSpeed | HorseSpeedSampled

The output variable is what the ladders read: 1,037 parametric blends over 38
projects bind `blendParameter` to it. Match the name off the sampler, not by regex,
and resolve variable indices **per file**: each packfile has its own table.

41 of the 49 actor projects carry the modifier. The 8 that do not — the flame and
storm atronachs, the chaurus flyer, the dragon, the dragon priest, the ice wraith,
the wisp, the witchlight — cannot consult a table however it is filled, and the
game ships one for each anyway. Two more (netch, slaughterfish) run their ladders
on `SpeedDamped` and raw `Speed`; their sampler still writes, and the actor's copy
still reads.

### 5.2 Ladders and compasses

A locomotion state is a **compass** — a blend over the eight headings, children at
0.125 apart, each arm a **ladder** — a synchronised parametric blend whose children
sit at the movement type's speeds (the bottom rung is the walk clip stretched to a
near-standstill, a rigging convention). `SpeedSampler.FromProject` walks a project
to its ladders; `SpeedLadder.FromBlender` reads a ladder's rungs and resolves each
child's clip through the animation cache for its travel and duration.

### 5.3 Which compass serves a key

Where a tag or a manager row writes the key, the compass is the one under it.
Where an expression writes it, the compass is under the subtree the expression sits
in (`StateExpressions.GovernedBy`). Where the key is the initial value, it is the
compass the graph plays at rest. Where none of that decides — the perk stances, the
attack states, the layered cases — the graph is **driven into the state** (§8) and
whichever sampler-fed ladder is live beside it is the key's.

## 6. The curve

The blend is time-synchronised: Havok interpolates the children's root-motion
translation and their duration as two independent linear ramps. For rungs
`(w_i, V_i, d_i)` and `x` between rungs `a` and `b`:

    u = (x - w_a) / (w_b - w_a)
    y = |V_a + u (V_b - V_a)| / (d_a + u (d_b - d_a))

clamped to the first rung's speed below `w_0` and the last's above `w_n`. Travel is a
vector: a heading between two arms mixes two directions, and collapsing to a
magnitude first overestimates by up to 8%. A quotient of two linear ramps is a
hyperbolic arc, which is why the shipped curves look like acceleration ramps and
why every chord model came in short. Between clips of equal duration a segment is
exactly straight; the curvature is the duration mismatch and nothing else. A pose
that carries only part of its root motion in the ladder — a blend with a standing
idle — delivers that share of it (the daedra's half).

`SpeedLadder.Evaluate(x)` is this law, and is the runtime's own response to about
0.003%. Bit-exact reproduction of the shipped floats is unreachable: the cache holds
root motion at six significant digits.

### 6.1 Two things the shipped file has that the engine does not

**A 0.0404 offset in x** (§6.2). Solving the shipped points back through the law,
the sampler recorded the response at `x - 0.0404` while storing `x` — a lag of the
tool that wrote it, constant within a block and slightly different between blocks.
The query applies none. `SpeedLadder.Tabulate` keeps it for reading the shipped
file; the generator does not use it.

**A sweep that stops at 324.5** on 74 of 86 entries, below the run speed of 47 of
them. Above its last point a record hands the request back, so the shipped sampler
switches off for those creatures at speed.

### 6.3 Damaged data

`HorseProject`'s cache disagrees with its own rungs by a constant per animation,
its `RunForward` travels zero and its `SprintForward` has no motion block. The
werewolf is the same case in miniature. No model recovers them from the cache and
none should try; the cache is the input, and a block built from it is the engine's
honest reading of it.

## 7. Authoring a creature: the movement type

The engine pairs the table with the movement type through one string, and the two
records must be authored together.

### 7.1 The record

A `MOVT` record carries an editor id, an **`MNAM` name**, and the speeds:

    ForwardWalk  ForwardRun     the speed along each heading
    BackWalk     BackRun        walking and running
    LeftWalk     LeftRun
    RightWalk    RightRun
    RotateInPlaceWalk / RotateInPlaceRun / RotateWhileMovingRun   yaw rates   (SPED)
    Directional / MovementSpeed / RotationSpeed                   anim-change thresholds (INAM, §7.5)

**`MNAM` is the join.** The root behaviour graph declares `iState_<MNAM>` with the
state id as its initial value, and the engine matches the suffix against `MNAM`
(§4.5). An editor id is never read. The two shipped mismatches — a misspelt
`CowSiwmDefault`, a `CombatSpider_MT` no record backs — make those states resolve
to no movement type in the game.

### 7.2 Where the numbers come from

`MOVT` is authored *from* the animations, and the ladder's arms are placed at its
values. So for each heading:

- **walk** = the delivered speed of the walk clip on that heading's arm,
  `|travel| / (duration / playbackSpeed)`, from the cache's root motion;
- **run** = the delivered speed of the run clip on the same arm.

For a creature with one compass, that is eight numbers read off eight clips, and
`SpeedLadder.FromBlender` produces them (`SpeedRung.Delivered`). A creature whose
graph has no lateral clips (the chicken) records 0 for the sides.

**The three rotation rates are not measured from anything.** `RotateInPlaceWalk`,
`RotateInPlaceRun` and `RotateWhileMovingRun` are degrees per second, and over the
107 shipped records they take 17 values, almost all of them 45, 90, 120, 135, 180,
270 and 360, with a few fractions of those (22.5, 33.75, 84.375). Set against the
turn clips' own root yaw over duration -- the cache records it, `ClipMovement.Turn`
reads it, 43 projects have such clips -- they do not agree: the bear's looping turn
yaws at 79°/s against a record of 120 and 180, the chicken's at 216 against 84, the
sabre cat's at 150 against 240, and the wisp has no turning clip at all against
360. Where they coincide (the troll, the netch, 180 for 180) it is the round number
that coincides. Turning is driven by the controller -- the graph blends its turn
clips on `TurnDelta`, and the humanoids' turn clips carry no root yaw -- so the
rate is a gameplay constant capping how fast the controller may yaw the actor. Pick
it by feel from the family the creature belongs to: a walk value at or below the
run value, a moving value at or above it, 90/180/180 for a humanoid, 180/270/360
for a bounding quadruped, 0 for the moving rate where the creature does not turn
while running (the bow drawn, the blocking stance, the flame atronach).

Authored this way, the table is the identity along every rung and departs from it
only between gaits (§6), which is the smallest table a creature can have.

### 7.3 Wiring the record

- one `MOVT` per locomotion state the graph can write `iState` at (§3.1); the
  `MNAM` must equal the `iState_` suffix, case-insensitive;
- the race's six base movement defaults name the types the actor has when the
  graph names none — the walk default at least; a graph that writes its own sprint
  and swim keys needs no race link for them;
- a new state needs its `iState_` constant in the **root** graph, with the id
  unique within it; the humanoids reserve 0–17 and 60–63, the shared quadrupeds
  ten per species alphabetically (bear 0, cow 10, deer 20, ... wolf 100).

The `Skyrim.esm` records are read here through Mutagen (`tools/speedgen/MasterData`),
and writing one is the same API in the other direction.

### 7.4 `iState` and `iState_<MNAM>`: the protocol between a graph and the engine

Two variables with similar names do two different jobs, and authoring a creature
means writing both correctly.

**`iState_<MNAM>` is a declaration.** It is an integer variable in the project's
**root** behaviour graph whose name is the `iState_` prefix followed by a movement
type's `MNAM`, and whose **initial value** is the state id the graph will use for
that movement type. It is never read by the graph and never bound to anything: it
exists so that the engine, at graph load, can build a table of `state id → MNAM`
from the variable names (§4.5). One per movement type the creature can be in.

**`iState` is the current state.** It is the integer the graph *holds* at one of
those ids, and it is the graph's responsibility to hold it there. The engine reads
it, never writes it, and does two things with the value: it looks the id up in the
table above and applies the movement type of that name to the actor, and it hands
the same id to `BSSpeedSamplerModifier` as the key of the speed table. Nothing in
any shipped graph selects a generator on `iState` — it is an output, not a switch —
and reading it as a switch is the mistake that made the block set look arbitrary.

The graph holds `iState` by one of four means, and a new creature should use the
first that fits (`StateKeys` reads all four):

1. **the variable's initial value** — a creature with one movement type sets
   `iState`'s initial value to that id and is done: the chicken, the hare, the
   troll. 22 of the 41 sampled projects need nothing more;
2. **`BSiStateTaggingGenerator`** — a generator that wraps a subtree and sets
   `iState` to `m_iStateToSetAs` while the subtree is active, with `m_iPriority`
   settling which tag wins when several are active at once (the humanoid stacks
   sneak over weapon over locomotion this way). This is the right tool for a
   *stance*: sneaking, a drawn bow, blocking, a mounted state, an attack. Put the
   tag directly above the state's locomotion, and any ladder under it is that
   key's ladder by declaration (§5.3);
3. **`BSIStateManagerModifier`** — a modifier carrying rows of (state machine,
   state id) → `iState`, for a graph that wants one table rather than tags in
   many files (the spriggan, the vampire lord, the werewolf);
4. **an expression** — `hkbEvaluateExpressionModifier` writing
   `iState = iState_<MNAM>`, or choosing with `cond`, or offsetting
   (`iState = iState_DeerDefault + iMovementSpeed`, where the graph computes
   `iMovementSpeed` from `Speed`). This is how a graph changes movement type by
   *speed* rather than by stance: the deer walks in one type and runs in another.

Rules the engine imposes, each learned from a shipped creature that breaks it:

- **declare in the root graph.** A referenced file's declaration is not scanned; a
  shared template that declares a species' constants for the whole family
  (`quadrupedbehavior.hkx`) is ignored in favour of the root's, and where the two
  disagree the root wins;
- **the id is unique within the root**, and any value works; the shipped
  conventions (§7.3) are for readability;
- **the name must equal `MNAM`**, case-insensitive, or the state resolves to no
  movement type and the actor keeps whatever it had. A declared id that nothing
  writes is harmless and dead — the sampler will never be asked for it, and the
  generator writes no block for it;
- **an id written but not declared** still keys the sampler: the rider's mounted
  states are declared in the horse's file, not the player's root, so the player's
  `iState` reaches 60–63 with no movement type of its own, and the table is asked
  for those keys all the same. Declare what you write;
- **the sampler's answer goes where the ladders read it**: bind every locomotion
  ladder's `blendParameter` to the sampler's output variable (`SpeedSampled` by
  convention), not to `Speed`, or the table is consulted and ignored — the netch
  and the slaughterfish ship that way.

The minimal correct creature, then: one `MOVT` with an `MNAM`, one
`iState_<MNAM>` in the root graph initialised to some id, `iState` initialised to
the same id, one `BSSpeedSamplerModifier` bound to `iState`, `Direction`, `Speed`
and `SpeedSampled`, and a locomotion compass whose ladders read `SpeedSampled` with
their rungs at the `MOVT`'s speeds. Add a movement type by adding a `MOVT`, its
declaration, and one of the four writers above; `speedgen` then writes its block.

### 7.5 The anim-change thresholds

The `INAM` subrecord holds three floats -- a **directional** threshold in radians, a
**movement-speed** threshold in units per second, a **rotation-speed** threshold --
named in the record type for the change in the request that is large enough to be
worth changing the animation for. Measured over the 106 shipped records:

    absent                                          12   the stances and the flyers:
                                                         NPCBowDrawn, NPCBlocking, NPCMagic,
                                                         NPCMagicCasting, NPCBleedout, the
                                                         atronachs, the dragon's four
    all three FLT_MAX                               88   never
    Directional = pi/4, MovementSpeed = 100          4   WolfDefault, WolfRun, ScribDefault,
                                                         NPCHorse (the rider)
    MovementSpeed = 100 only                         2   HorseSprint, HorseSwim

`FLT_MAX` is "never": the mechanism is switched off on every creature but the
wolf, the scrib and the horse with its rider, where a heading change of 45° or a
speed change of 100 u/s crosses it, and the rotation threshold is never set at all.
So for authoring the answer is the shipped default -- write `FLT_MAX` three times,
or omit the subrecord as the stances do -- and the six exceptions are the only
place the field does anything. What it does there is not traced in the executable
here (the movement controller's use of the record was not read); the names and the
values say a re-selection of the locomotion animation on a large enough change of
request, and the three creatures that set it are the ones whose direction blends
are coarsest. Treat it as a tuning knob with a known safe value, not as data to
derive.

## 8. Generating the table

`HKSK.Speed.SpeedDataGenerator` writes it, `tools/speedgen` runs it. It is written
for the engine that reads it, not to reproduce the shipped file:

    for each project with a BSSpeedSamplerModifier:                 /* §5.1 */
        for each value s the graph can put iState at:                /* §3.1 */
            arms = the compass the graph plays at s                  /* §5.3 */
            for d in the 19 headings:
                for (x = 0; x <= max(top rung, 2 x fastest MOVT speed); x += 0.5):
                    y = §6 at x, times the pose's root-motion share
                thin at 0.5 units                                    /* the game's own greedy pass */
            emit { key = s, 19 records }

- **the keys** are the values the graph writes, not the constants it declares:
  128 blocks over the 41 projects; 76 of vanilla's 86, the other ten being the 8
  unread tables and the 2 keys no graph writes; 52 vanilla never swept
  (`FirstPerson`'s stances, the draugr skeleton's weapons, the attack states, the
  netch's sprint, the sphere's ranged stance, the horker's swim, the horse's
  sprint, fall and swim);
- **the curve** is §6 at the goal speed, no offset;
- **the sweep starts at zero** on every heading, because below the first point the
  query reads from the origin, and **ends past everything the game can ask** — the
  whole ladder (the humanoids' top rung is the run at ten times speed, 3,510) and
  twice the fastest speed the type names, for `SpeedMult`; beyond the top rung the
  curve is flat and costs one point;
- **thinning** is the shipped file's own greedy pass, at 0.5 units rather than its 2.

**Driving the graph into a state** (`WayInto`, `TaggedAt`) is how a key with no
ladder of its own gets its curve: from the writing node up through its states, one
entry event per level — the one that also enters the most other states on the way,
then the shortest, since raising every transition's event at once sends the player's
root through `CartExit` — each chooser on the way pinned to its state (a sync
variable, a bound `startStateId` where it can name the state, a manual selector's
index), what the chosen transition's condition asks pinned too (`iRightHandType ==
7` into the bow attack), and the clips' end triggers **held**, because the evaluator
otherwise reads the graph at rest and an attack's own clip has raised `attackStop`
by then. The reading counts only when the writer comes out active. Then the
sampler-fed ladder live beside the state is the key's — the attacks take the
weapon locomotion under them, the perk stances the bow and block locomotion — and
where none is live the pose is read flat. One rule of the choice matters: a return
event such as `pairedStop` or `PairEnd` enters the resting state of many machines
and so is shared by many levels, and is taken only when nothing else enters the
state, or it beats `HorseEnterInstant` and `MountedSwimStart` and the rider's fall
and the horse's swim never land. Driven that way, all 24 blocks the graph declares
show their own ladder, and every other placed key, 27, lands too.

Six writable keys get no block: the rider's mounted states and the first-person
camera carry no root motion, and the horse's sprint clip has no motion block. An
absent block is the game's own answer there, the request unchanged.

Read back through the game's lookup, the result holds 13,451 of the 16,930 shipped
points on the shared blocks. Each engine-side choice costs against that measure —
the offset 171 points, the zero start 99 — and is kept, because the engine is the
measure. 62,641 points, 524 KB, about seven seconds.

## 9. Open

- **Bit-exact floats**: unreachable from six-digit root motion.
- **The per-block offset** (§6.1): the tool's, not explained, not needed.
- **The riekling's lateral records** and the **horse**: the shipped file's own
  anomalies and damaged cache respectively (`docs/speed-data-research.md`).
- **Rotation rates** for `MOVT` authoring are settled as constants (§7.2); nothing
  in the assets derives them.
- **The chaurus flyer's zero table**: nothing reads it, so nothing depends on it.

## 10. Known corruption

`FalmerProjectData` declares 4 entries, 2 malformed: key `0x80000000` and key
`0x626E7572` — the ASCII `runb`, the head of a clip name written into a `u32` —
both with `n_records == 0`. A reader must tolerate them; a writer must not
reproduce them.

## 11. Method

The engine side was read from the retail executable unwrapped with Steamless for
reading only, by the recipes in `docs/reverse-engineering.md`: from a path literal to
its loader, from a class name to its virtual table, from a variable name to its slot
in the engine's string table and the code that takes it. The unwrapped binary is not
redistributable and is not in this repository; `tools/exe-re` holds the helpers.

## 12. Tooling

- `HKSK.Cache.SpeedDataFile` — read, write, and `Sample(project, state, direction,
  goalSpeed)`, the query as the engine makes it (§4.2), returning `goalSpeed` for an
  absent project, state or curve.
- `HKSK.Behavior.StateKeys` — the values a graph can put `iState` at, and by what.
- `HKSK.Speed.SpeedSampler`, `SpeedLadder`, `Compass` — a project's ladders and the
  curve law.
- `HKSK.Speed.SpeedDataGenerator` — the table (§8); `tools/speedgen` runs it.
- `tools/hkmeasure` — runs a graph inside Havok's own runtime, for measured answers.
