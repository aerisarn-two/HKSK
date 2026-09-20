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

Read out of `SkyrimSE.exe` (unwrapped for reading; `docs/reverse-engineering.md`).

### 4.1 Where the file is read

One loader reads `Meshes/AnimationSetDataSingleFile.txt`, behind
`bLoadCollatedAnimTextData`, and publishes a singleton the consumers reach. The
split form `MESHES/ANIMATIONSETDATA/` is the other path the same loader knows.

### 4.2 What the loader builds, and what reads it

Each set becomes a 0x88-byte object filled in file order: the swap events as string
handles, the hand variables as (name, min, max) triples, the attacks as (event, clip
list, flag byte `atoi > 0`) records, and the checksum triples last. **Set names are
read and discarded** — a set is addressed by index, so in the merged file only the
order matters. A set with no swap event is given the empty string as one, so that it
can still be selected.

The lookup is keyed on a **swap event**: the consumer finds the sets whose event list
holds the key and, of those, the ones whose hand variables all hold as **ranges
against the graph's live variables** (`min <= value <= max`). Each matching set's
checksums become a load request to `AnimationFileManagerSingleton`.

### 4.3 What sends a key

Every non-empty key traced is the **animation event of an idle record**, the
`ENAM` string, reached either directly by code that has chosen an idle, or by
resolving an **action without performing it**: `Action Draw`, `Action Force Equip`
and their kin are default objects, and a helper asks the idle manager which idle the
action would pick for this actor now and uses that idle's event as the key. Nothing
is sent to the graph by the lookup. So the keys the file can ever be asked for are
the idle tree's events (1,246 in the masters) and the empty string.

### 4.4 How an animation file gets loaded

Three ways. Ahead of need, from a selected set (§4.2). **When a clip activates**:
`hkbClipGenerator::activate` with no bound file maps the clip's binding index to a
file id through the character's animation list and demands it from the same file
manager, through a copy of its pointer the behaviour runtime holds. And from a save
game, through a reader over the stream.

### 4.5 Is it necessary

Not strictly. `bInitiallyLoadAllClips:Animation` (compiled default 0) decides
whether the file manager exists at all: at 0 it exists, the set data prefetches and a
clip it missed loads when it first plays; at 1 every clip is loaded up front and the
consumers return before selecting anything. **The set data decides what is loaded
ahead of need**, and an actor whose sets are wrong hitches rather than breaks.

### 4.6 The moving-attack flag

The one value in the file that is not about loading. `CombatBehaviorContextMelee`
(`0x1408a3d30`) walks the attacks the actor's race can make with its hand types,
finds each by event name in the set data, asks the animation data for the clips'
end translation, hit-frame translation and hit-frame time, scales the translations
by the actor, and records a **reach**:

- flag **clear**: reach = |end translation| — the clip's own root motion;
- flag **set**: reach = −1, and the attack check (`0x1408a2ee0`) uses the
  **attacker's speed × time to the hit frame** instead, projecting the actor along
  its own motion.

Either way the hit frame's translation places the blow. With the flag clear, an
attack whose clips do not travel has a reach of 0, fails the 5.0-unit gate that
predicts the attacker's position, and is treated as standing still for the whole
swing. So the flag says **whose travel the attack has**: the animation's, or the
character's locomotion under it. Vanilla sets it on 38 (project, event) pairs in 6
projects — the player's regular, sprint and hand-to-hand attacks, the werewolf's
running ones, three hovering creatures' — and clears it on lunges and power
attacks. It was misnamed "mirrored" until the executable was read.

## 5. Rebuilding the file

`HKSK.SetData.SetDataGenerator` writes it, `tools/setgen` runs it. Nothing is read
from a shipped set-data file; `setgen` empties one before generating.

### 5.1 Inputs

The behaviour graphs and the animation cache; from the masters, every idle record's
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
- `SetDataRebuildTests`, `MasterCorpus` — the numbers above, as exact assertions.
