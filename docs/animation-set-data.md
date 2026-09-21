# animationsetdatasinglefile.txt — the animation set data

What the file is for, what the engine does with it, how it is rebuilt from the
other assets, and the one value in it that took an executable to explain. The
measurements behind each statement, and everything that was tried and refuted, are
in `docs/animation-set-data-research.md`; this document states the results.

Section numbers are stable: code comments and the other documents cite them.

## 0. Purpose

A behaviour graph references hundreds of animation files and a character plays a
few of them at a time. Loading a clip's file when the clip first activates is late:
the frame it is wanted is the frame it is read from disk. The set data is the
engine's **prefetch list**. For each project it groups the animation files by the
situation that needs them — a swap event such as drawing a weapon, the hand types
that make a weapon set, an idle's event — so that when the game decides what an
actor is about to do, it can ask for the files first.

It carries one more thing that is not about loading at all. Each **attack** entry
names the behaviour event that starts it, the clips it may play, and a flag that
tells melee combat whether to measure the attack's reach by the actor's own movement
or by the animation's root motion (§4.6). That flag is read every time an AI decides
whether it is close enough to swing.

Neither part is necessary for the game to run: a missing set loads its clips late,
a missing attack entry leaves combat with a reach of zero. Both are necessary for it
to run well.

## 1. Layout

Text, one value per line. The projects are listed, then one block per project in
the same order. Nothing marks a block's end; each is self-delimiting through its
counts. `HKSK.Cache.AnimationSetDataFile` reads and writes it byte-exact.

    <project count>
    <Project>Data\<Project>.txt          × count      the key, always this shape
    per project:
      <set count>
      <Name>.txt                         × count      unique within a project
      per set:
        V3                                            the version line
        <swap event count>, then the events
        <hand variable count>, then name, min, max per variable
        <attack count>, then per attack: event, moving-attack flag, clip count, clips
        <animation count>, then three lines per animation: folder CRC, name CRC, 7891816

- **Swap events** are behaviour events (1,910 of 1,921 declared by a behaviour the
  project loads). 791 of the 990 shipped sets carry swap events and nothing else.
- **Hand variables** are behaviour variables, `iRightHandType` and `iLeftHandType`
  for 297 of the 386; min and max are inclusive, and a range picks a family
  (`iRightHandType 1..4`, the one-handed weapons).
- **Attacks**: the event is a behaviour event, the clips are **clip generator
  names** (`m_name`, not the animation path); the flag is 0 or 1 (§4.6).
- **Checksums** name animation files (§2). 77 shipped sets name none, which is fine.

The `V3` line is read: the parser compares the number after `V` with 3, and at 3
and above each checksum entry is three lines. Every shipped set says `V3`.

## 2. The checksums

Each animation is the CRC-32 of its **folder** (data-relative, lower-cased, no
trailing separator), the CRC-32 of its bare **stem**, and `7891816` = `0x786B68`,
the bytes of `hkx` reversed. The CRC is polynomial `0x04C11DB7`, reflected in and
out, **with no initial value and no final xor** — not the zlib variant
(`HKSK.Cache.HavokCrc`). The loader stores them as a `BSResource::ID` — name,
extension, directory — which is what the file manager takes (§4.4). Because both
halves are checksums of short enumerable strings, every one of the shipped 20,807
triples resolves to a concrete file under `meshes`.

## 3. How the shipped file was made

### 3.1 A creature's set is its character file's animation list

For a project that never chooses by hand type — 40 of the 49 — the one set lists
exactly the animations the character file lists, in that order, with the project's
attacks. The file is named by the **character**, not by the clip: a clip generator
stores a path, but the engine binds the clip by name through the animation cache
and plays what the character lists at that index, which is how the female body
plays female files from a graph that says `Animations\male\...`.

### 3.3 A generated base plus later bulk edits

The shipped file is a tool's output with hand edits on top: first-person killmoves
moved or copied to the victim, a werewolf whose human-side killmoves its character
does not list, DLC sets added without the base graphs learning their events. A
rebuild reproduces the base and documents the edits rather than copying them.

### 3.4 How a set was emitted, and where the flag sat

The split form under `animationsetdata/`, one text file per set, is the authoring
unit, and **every list in it was written out of a sorted container**: 774 of the
774 multi-entry lists are in Windows word-sort order (case-insensitive, underscore
before digits and letters, set names compared without `.txt`). Only the project
order (the animation cache's build order) and a set's checksums (the character
file's order) come from elsewhere. The attack lists were curated per hand pair, not
enumerated from the graph, and the moving-attack flag is a **hand-filled column** on
each set's attack list — set by attack type, with one slip (`H2HShield.txt`). That
is the whole reason the flag cannot be *copied* into a rebuilt file whose sets are
not vanilla's sets, and why it is derived instead (§6).

## 4. The engine side

Read out of `SkyrimSE.exe` 1.6.1170, unwrapped for reading only, by the recipes in
`docs/reverse-engineering.md`; addresses are image-base `0x140000000` virtual
addresses. The trail is stated so that each conclusion can be re-derived.

### 4.1 Where the file is read

The six path strings of the animation text data are `BSFixedString` globals built
by static initialisers `0x14008fff0`–`0x1400900e0` and released by
`0x141718bb0`–`0x141718c00`:

    0x14315c900  Meshes/AnimationData/          0x14315c920  Meshes/AnimationSetData/
    0x14315c910  BoundAnims/                    0x14315c928  DirList.txt
    0x14315c918  Meshes/AnimationDataSingleFile.txt
    0x14315c930  Meshes/AnimationSetDataSingleFile.txt

The set data's two globals have one user, the loader **`0x14053b000`**. It publishes
the manager as a singleton at `0x143138d58`, reads `bLoadCollatedAnimTextData:
Animation` (setting record `0x1420107d8`, reader `0x140541fa0`, compiled default
**1**), and with the gate on opens the single file, reads a line of at most 260
characters, converts it to the project count and loops. With the gate off, **or
when the single file does not open**, it goes to `0x14053b69b` and reads the split
form, `Meshes/AnimationSetData/` and its `DirList.txt` (`0x14053b6f6`,
`0x14053b776`). The manager is created unconditionally at start-up in
`0x140541b80`, after the animation data manager (`0x140536ec0`, singleton
`0x143138d50`) and in the routine that also creates the speed database, and
destroyed by `0x140541e80`.

### 4.2 What the loader builds, and what reads it

The project loop at `0x14053b200`–`0x14053b243` reads each set **name** into one
scratch buffer on the stack, overwriting it every time, and only then allocates
`count` sets of 0x88 bytes: **the names are discarded**, and a set is addressed by
index. The per-set parser `0x14053d700` fills the object in file order:

    +0x00  swap events, string handles, count at +0x10
    +0x18  hand variables, 16-byte (name, min, max), count at +0x28
    +0x70  attacks, 0x18-byte (event, clip list, flag byte at +0x10 = atoi > 0)
    +0x30  checksum triples, written last

The version line is optional -- a first line starting with `V` is compared with 3
at `0x14053de18`, and at 3 and above each checksum entry is three lines; below 3 it
is one line, read and discarded. A set with no swap event is given the empty
string as one (`0x14053d650`). The checksum writer `0x14053de91`–`0x14053debf`
stores the **name** CRC at `+0x00`, the `hkx` constant at `+0x04` and the
**folder** CRC at `+0x08` -- a `BSResource::ID` (file, extension, directory) -- which
settles which of the file's first two lines is the folder.

The singleton has fifteen referencing functions: the life cycle, and ten consumers.
Each consumer takes a key and the actor's graph, finds the sets whose swap events
hold the key and whose hand variables all hold as ranges against the graph's live
variables, and turns each match's checksums into a load request to
`AnimationFileManagerSingleton` (entries `0x140bca970` and `0x140bcab30`). The
smallest consumer returns early without the file manager, which is how the
dependency in §4.5 was found.

### 4.3 What sends a key

No consumer passes a literal. Every key traced is the animation event of an idle
record, the `ENAM` string `TESIdleForm` keeps at `+0x50`, reached two ways: code
that has chosen an idle passes its event; and `0x1407c66b0` fills a `TESActionData`
(vftable `0x14178c478`) with the actor and an **action** and hands it to
`0x14065cd10`, which, when the action data carries no event, asks the idle manager
(`0x1403b1f80`, global `0x1420f8980`) which idle the action would pick for this
actor now, copies that idle's event into the action data at `+0x28`, and returns
it as the key -- nothing is sent to the graph. Actions are default objects by
index: the manager at `0x1420f5600` holds them from `+0x20` with a loaded flag per
index from `+0xb90`, and their names are a table of 24-byte records at
`0x141fd8f50` (`Action Draw`, `Action Force Equip`, `Action Idle` = 0x40, the null
fallback). So the keys the file can ever be asked for are the idle tree's events.

### 4.4 How an animation file gets loaded

Three ways. **Ahead of need** from a selected set (§4.2). **When a clip
activates**: the executable names `IAnimationClipLoaderSingleton` (vftable
`0x141989cd8`, all slots pure), implemented by `AnimationFileManagerSingleton`
(`0x141989d10`); start-up copies the file manager pointer to a second global,
`0x1431b2820`, which the behaviour runtime reads, and
`hkbClipGenerator::activate` (`0x140acd750`, named by its profiling string), when
the clip's `userData` at `+0x30` is 0, calls slot 1 (`0x140bcbb90`), which maps the
clip's `animationBindingIndex` (`+0x70`) to a file id through the character's
animation list and demands it. **From a save game**: the wrappers `0x140bcb710`
and `0x140bcb820`, each with one caller (`0x1407c6170`, `0x1407c6290`) that builds
an `AnimationStreamLoadGame` reader over the stream.

### 4.5 Is it necessary

`bInitiallyLoadAllClips:Animation` (record `0x142015620`, compiled default **0**)
is read in one place, `0x1407c50d0`, which caches its negation at `0x1431a939c`;
start-up `0x1407c4d80` creates `AnimationFileManagerSingleton` only when that
flag is set and shutdown `0x1407c4f90` mirrors it, and `0x1407c50d0` has no other
caller. At the default the file manager exists, the consumers prefetch, and a clip
they missed demands its file on activation; at 1 the file manager is never created,
the consumers return before selecting, and every clip is loaded up front. The set
data decides what is loaded ahead of need and nothing else.

### 4.6 The moving-attack flag

The reader was found by the shape of the structure it walks, not by a string.
`CombatBehaviorContextMelee`'s attack update, **`0x1408a3d30`**, iterates the
attacks the actor's race can make with its hand types, finds each by event name in
the set data, and asks the animation data (`0x140442a80`) for the clips' end
translation, hit-frame translation and hit-frame time; the six translation floats
are multiplied by the actor's scale, and a **reach** is recorded at the entry's
`+0x20` with the hit time at `+0x24`:

- flag **clear**: `sqrt(x² + y² + z²)` of the scaled end translation;
- flag **set**: `-1.0`, the constant at `0x141769578` beside `5.0`, `32.0`, `128.0`,
  `2.0` and `0.9`.

The context's longest reach, a running `maxss` at `+0x30`, rises with a clear flag
and never with a set one. The attack check **`0x1408a2ee0`** then uses the reach
twice. First an early-out: a reach of zero or more skips the fetch of the
attacker's own speed altogether, so the allowed distance is the clip's travel plus
the base, plus a combat setting read at `+0x1a8`, plus the target's speed times the
time to the hit frame; with the flag set the first term is the attacker's speed
times that time instead. Then the prediction: the hit frame's translation, rotated
into the actor's frame, places the blow whatever the flag; the reach is compared
with zero a second time at `0x1408a31bd` and, set, `0x140853fb0` projects the actor
along its own motion for that time, while clear the end translation is added to the
position **only when the reach exceeds 5.0** -- below that the actor is modelled as
standing still. So the flag is which of two estimates combat trusts, and an attack
whose clips do not travel is, with the flag clear, both unreachable beyond the base
distance and motionless for the swing. The setting reads first taken for the
flag's "only reader" (`fCombatAttackMoving*`) turned out to sit on the common path;
the flag itself is the byte at `+0x10` of the attack entry (§4.2), `atoi > 0`.

## 5. Rebuilding the file

`HKSK.SetData.SetDataGenerator` writes it, `tools/setgen` runs it. Nothing is read
from a shipped set-data file; `setgen` empties one before generating.

### 5.1 Inputs

The behaviour graphs and the animation cache; from the game's records
(`HKSK.Records.IGameRecords`, read by the caller), every idle record's
event (the only keys the lookup is given, §4.3), the equip events (the idles under
the drawing and equipping roots, the one input picked by name), and each race's
attack events for the graph it wears.

### 5.2 – 5.5 Sets

- **One set** for a project that never chooses by hand type: its character list, in
  order, with its attacks. 38 of 49 come out this way, 26 identical to vanilla's.
- **A base set, weapon sets and a set per event** for a project that does. The graph
  is built for each of the 121 hand combinations the race asks about, with every
  hand-bound choice resolved (a selector's index, a machine's start state, a
  condition on hand types); graphs that come out the same are built once.
- **A weapon set** holds what a combination reaches beyond what every holdable
  combination shares; its keys are the equip events.
- **An event's set** holds the files its region of the graph reaches **until the
  graph is home again** — a behaviour is a cyclic graph, and the boundary is the
  return to the default state.
- **Groups are covered by ranges** (one inclusive range per variable, so a group is
  written as rectangles) and **merged within a slack** (`--slack`, default 1.5×).

### 5.6 Attacks

An attack is the nested state its start event names. Its clips are the clip
generators' names under that state for the hand pair; attacks are stated per
combination and never merged across cells that differ, because the race reads them
per combination. Two shipped events are generator names rather than attacks and are
not produced.

### 5.7 What comes out

    projects 49     sets 2,243     animations listed 56,652     attacks 3,979     2.3 MB
    (shipped: 990 sets, 20,807 animations, 737 attacks, 0.8 MB)

    every file the shipped data lists, in some set of the project      6,758 of 6,829
    files of a shipped idle set that its own keys load                 2,145 of 2,689
    files of a shipped weapon set in a set applicable to the weapon    15,248 of 15,538
    attack entries identical over the 121 combinations                 22,949 of 27,039

The 71 files not listed are the hand edits of §3.3. The idle-set gap is keys no
transition takes (dialogue idles, furniture) and files the graph reaches from
nowhere. Transition conditions on the hand types are evaluated, not assumed to hold.

## 6. Deriving the moving-attack flag

The flag is not in any asset, and the masters record no trace of it: the race's
attack data is byte-identical between a flagged and a clear attack, and the idle
records carry no timing that follows it. What the assets do record is the
**condition the engine acts on** — whose travel the attack has — and that is what
is derived. An attack's travel is the character's, and the flag is set, when:

1. **a blender above its clips is parametric on the actor's speed**
   (`GraphReach.TravelChosenBySpeed`): no single root motion exists, the
   locomotion is interpolated under the swing — the player's ordinary attacks;
2. **the graph says the character is sprinting** over that state (`IsSprinting`,
   the netch's sync state): sprinting has one direction, so there is no blend, but
   the actor is carried at sprint speed;
3. **the idle tree chooses the attack only on the move** (`GameEvents.MovingAttacks`,
   read from the masters: `IsSprinting == 1`, `GetMovementSpeed >= 1`, or a beaten
   sibling that requires standing) — the werewolf's running powers;
4. **the creature hovers** (`SetDataGenerator.Hovers`): no speed-parametric blend in
   its graph and nothing under its direction blends travels more than the engine's
   own 5-unit gate — the storm atronach, the wisp, the witchlight; not the flyers,
   whose direction blends carry the travel, and not the spider centurion, which walks;

and **never inside a branch that raises `bAnimationDriven`**
(`GraphReach.PlaysAnimationDriven`), because the engine then moves the actor by the
clip's root motion, whatever the file says. That is 41 (project, event) pairs:
**33 of vanilla's 38**, eight vanilla left clear beside the identical case it
flagged (the Vampire Lord's blend that is the player's, the werewolf's single sprints
beside its dual one, the wisp beside the witchlight), and one vanilla flags against
its own engine (the storm atronach's standing power attack, animation-driven). The
four not derived are the player's shield bash, flagged in one set of thirteen, and
the werewolf's two side attacks, about which no asset says anything. Copying
vanilla's values was implemented and removed: it had to fall back to event names
where the sets differ and over-flagged 88 sets, and it carried the contradiction.

The idle tree and the graph are read together: a branch's condition (sprinting,
moving, a direction) is what the tree states in the masters, and what the graph
does under it is what the behaviour files state; neither alone separates the cases.

## 7. Method

The loader was found from its path literal's global; the per-set parser gave the
set object's layout; the lookup's key was followed one level up to the idle manager
and the default object table; the flag's reader was found by the shape of the
structure it walks rather than by any string. The recipes are
`docs/reverse-engineering.md`.

## 8. Tooling

- `HKSK.Cache.AnimationSetDataFile` — read, write, `HavokCrc`.
- `HKSK.SetData.SetDataGenerator`, `GraphReach`, `StateGraph` — the rebuild (§5) and
  the flag (§6); `tools/setgen` runs it against the extracted meshes and the masters.
- `SetDataGenerator.Amend` — one project's sets, rebuilt in place, the others untouched.
- `HKSK.Records.GameRecordRules.Events` — the inputs of §5.1 from the records.
- `SetDataRebuildTests`, `MasterCorpus` — the numbers above, as exact assertions.
