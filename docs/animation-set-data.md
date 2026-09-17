# animationsetdatasinglefile.txt

What the animation set data holds, how much of it can be read back to concrete files,
and how the shipped file was put together. Every number here was measured against the
shipped game; where a question is still open it says so, with what has been ruled out.

This is an investigation in progress. §1–§3 are established; §4 lists what has not been
looked at yet.

## 0. What the file is for

A creature's animations are not all loaded at once. The set data partitions each
project's animations into **sets** — a set is a group that loads together — and says, for
each one, what brings it in and what it covers:

    set
      swap events       the behaviour events that bring the set in
      hand variables    behaviour variables and the values the set applies to
      attacks           attack events, each with the clips it may play
      checksums         the animation files the set covers

When a set's files are not there, its animations do not play even though the behaviour
reaches the right state — the failure mode modders know as "the hash is not in the right
set".

The file covers the same 49 projects as the animation cache and no others: a project has
set data if and only if it has a clip cache. Creatures carry one set each; the player
carries 374 (`DefaultMale`, `DefaultFemale`) and first person 109.

## 1. Layout

The file lists its projects, then one block per project in the same order. A project
block lists its set names, then one set per name. Nothing marks where a block ends — each
is self-delimiting through its counts, and the file is read until it runs out.
`HKSK.Cache.AnimationSetDataFile` reads and writes it.

    <project count>
    <Project>Data\<Project>.txt          × count      the key, always this shape
    per project:
      <set count>
      <Name>.txt                         × count      unique within a project
      per set:
        V3                                            every one of the 990 sets
        <swap event count>, then the events
        <hand variable count>, then name, min, max per variable
        <attack count>, then per attack: event, mirrored flag, clip count, clips
        <animation count>, then three lines per animation: folder CRC, name CRC, 7891816

What the parts hold, as the shipped data has them (measured when the reader was written,
and recorded beside it):

- **Swap events** are behaviour events: 1,910 of the 1,921 are declared by a behaviour the
  project loads, and the eleven that are not are Dawnguard and Dragonborn additions the
  base graphs never gained. They are also most of the file: 791 of the 990 sets carry swap
  events and nothing else.
- **Hand variables** are behaviour variables, all 386 declared by a behaviour the project
  loads. `iRightHandType` and `iLeftHandType` are 297 of them; `iWantMountedWeaponAnims`
  and `bWantMountedWeaponAnims` are the others seen. Min and max are inclusive and usually
  equal; a range selects a family, as `iRightHandType 1..4` does for the one-handed
  weapons.
- Hand variables are **not** what makes a set an attack set: 40 sets have attacks and no
  hand variables, and 68 have hand variables and no attacks.
- **Attacks**: 737 in the file. The event is a behaviour event (735 of 737 declared);
  the clips are clip generator names (776 of the 793 named are clips the project caches).
  685 attacks name one clip, 50 several, 2 none. The mirrored flag is only ever 0 or 1.
- **Checksums**: 77 of the 990 sets name no animations at all, which is normal.

## 2. The checksums decode completely

Each animation is three lines: the checksum of its folder, the checksum of its file name,
and `7891816` — `0x786B68`, the bytes of `hkx` reversed, a constant rather than a
checksum. The checksum is CRC-32 over the lower-cased text, polynomial `0x04C11DB7`,
reflected in and out, **with no initial value and no final xor** — not the zlib variant,
which is why a stock CRC-32 does not reproduce these numbers (`HKSK.Cache.HavokCrc`).

The folder is **data-relative and has no trailing separator**, the name is the bare stem:

    meshes\actors\ambient\chicken\animations        folder  2725300844
    aggrowarning1                                   name     329189360

Because both halves are checksums of short, enumerable strings, the whole file can be read
back to concrete files. Checksum every folder under the extracted `meshes` and every
animation stem, and look the triples up:

    triples in the shipped file                      20,807
    folder checksum found                            20,807
    name checksum found, stems of files on disk      20,780
    name checksum found, adding every stem any
      character file names                           20,807
    resolves to a file in the extracted corpus       20,780

The 27 that name no file on disk are animations character files list whose files are not
in the extracted corpus: 25 are the five human-side werewolf killmoves
(`Paired_1HMKillMove_WWKidneyStab` and four more) wherever they are listed — the player's
sets, first person's and the werewolf's own, in third and first person — and 2 are
`IdleValericaWalkForward`, once in each of the player's projects.

Two traps, both of which silently produce wrong checksums rather than errors:

- **Character files store some animations relative to the project, with `..`.** Paired
  killmoves are listed as `..\SharedKillMoves\Human&Bear\Paired_1HMKillMoveBearA.hkx`
  (or `..\..\` for projects one folder deeper). The `..` has to be collapsed before
  checksumming; the checksum of the literal path matches nothing.
- **The extracted corpus is lower-case on a case-sensitive filesystem** while character
  files say `Animations\WalkForward.HKX`. Path existence has to be checked
  case-insensitively. The checksum itself lower-cases, so it is unaffected.

## 3. How a one-set creature's set is built

40 of the 49 projects carry a single set, named `FullCharacter.txt` on most and
`FullBody.txt`, `Full.txt`, `Base.txt`, `base.txt`, `default.txt`, `defaultSet.txt`,
`Default.txt` or `MT.txt` on the rest. The other nine carry 950 sets between them.

### 3.1 The set is the character file's animation list, in order

Take the character file's animation list, resolve each entry against the project folder
(collapsing `..`), and checksum it: that **is** the set, entry for entry and in the same
order, for 28 of the 40.

    ChickenProject, HareProject, AtronachFlame, AtronachFrostProject,
    AtronachStormProject, DogProject, ChaurusProject, HighlandCowProject,
    DeerProject, ChaurusFlyer, VampireBruteProject, BenthicLurkerProject,
    BoarProject, ScribProject, HMDaedra, NetchProject, Dragon_Priest,
    DwarvenSpiderCenturionProject, GiantProject, GoatProject, HorseProject,
    IceWraithProject, MammothProject, MudcrabProject, SkeeverProject,
    SlaughterfishProject, VampireLord, WitchlightProject

The list's order is load-bearing elsewhere — a slot's index is its identity throughout
the animation cache — and the set follows it.

### 3.2 First-person killmoves, and where the rule runs out

Most of the other twelve differ by the **first-person counterparts of their paired
killmoves**: for `sharedkillmoves\human&bear\paired_1hmkillmovebeara` the set also lists
`sharedkillmoves\1stperson\human&bear\paired_1hmkillmovebeara`, which the bear's
character file does not name. Two rules for when to add them were measured against
adding none:

    rule                                                 identical   same contents,   differ
                                                         in order    other order
    no first-person entries                                  28            0            12
    add the counterpart wherever the file exists             22            8            10
    add it where FirstPerson's behaviours play it            28            0            12

- **Adding it where the file exists** gets the contents right for 30. It is right for the
  bear, wolf, dragon, frostbite spider, hagraven, sabre cat, spriggan and troll, and wrong
  for the chaurus flyer, gargoyle, benthic lurker, boar, ballista, scrib and giant, whose
  first-person killmoves exist and are not listed. Those are the Dawnguard and Dragonborn
  creatures, and the giant.
- **Asking whether first person plays it** separates nothing: FirstPerson's behaviours
  play none of these, so the rule adds no entry anywhere — the same result as adding none.
  FirstPerson's *character file* lists all of them, the DLC creatures' included, so asking
  that separates nothing either.
- Where the counterparts are right, their **order** is not the creature's own: the bear's
  run 1HM A, 2HM B, 1HM B, 2HM A, 2HW A. That order has not been matched.

§3.3 is why no rule over the final assets settles this.

Three things remain beside the first-person question:

- **The werewolf lists killmoves its own character file does not name.** Its set carries
  `human&werewolf\Paired_1HMKillMove_WWKidneyStab` and four more, in third and first
  person — the human-side werewolf killmoves, named in the player's character file.
- **A few listed animations are left out.** The ballista's `Idle_ LookLtoR`,
  `Idle_Scratch` and `Idle_TurnLeftLook`, the horker's `TurnCannedL180Flee` and
  `TurnCannedL90Flee`, and the wisp's `AmbushIdle`. Their files exist, and dropping
  animations no clip generator plays does not explain them — it removes the chaurus's
  `Idle_SleepExit` and `Idle_SleepStart`, which the set keeps, and keeps the ballista's
  idles.
- **The frostbite spider lists its four first-person killmoves twice.**

### 3.3 The shipped file is a generated base plus later bulk appends

The split form under `animationsetdata/` is a **pre-DLC snapshot**: 39 projects, no
Dawnguard or Dragonborn creature. Its set files are exact counterparts of blocks in the
single file, which makes the snapshot a record of what changed between the two.

    sets present in both                     813
    identical                                609
    different                                204

The differences are not scattered edits. **Within a creature, the same block of
animations was appended to every one of its sets:**

    creature                    sets changed   added to each
    DraugrProject                 23 of 25          6
    DraugrSkeletonProject         23 of 25          6
    FalmerProject                 12 of 15       6 (9 on h2hmagic)
    SteamProject                   7 of 7           4
    BearProject                       1             5    its first-person killmoves
    WolfProject                       1             6
    TrollProject                      1             5
    SabreCatProject                   1             5
    HagravenProject                   1             4
    SprigganData                      1             4
    FrostbiteSpiderProject            1             8
    DragonProject                     1            13
    WerewolfBeastProject              1            11
    DefaultMale, DefaultFemale       49 each      mixed, some shrink
    FirstPerson                      32           mixed

That is the edit `HKSK.Model.ActorProject.AddAnimation` makes — register an animation with
every set that covers the project — done in bulk by a later update. So the first-person
question in §3.2 is a question of **authoring history**: which creatures that update
touched. The final assets cannot answer it, but the snapshot, which ships, records the
state before it.

The player's sets did not only grow. 17 of `DefaultMale`'s have fewer animations in the
single file than in the snapshot — `bow.txt` 123 to 118, the H2H, staff and torch
combinations by four or five each, and `_MTSolo.txt` from 149 to 106 — and first person's
seven weapon sets lost 13 to 16 each. The player's history is more than appends.

## 4. Not yet examined

The nine multi-set projects — the player (374 sets each for `DefaultMale` and
`DefaultFemale`), first person (109), the draugr and skeleton (25 each), the falmer (15),
the riekling (12), the sphere centurion (9) and the dwarven centurion (7):

- **where set names come from.** Some occur as strings in the player's behaviours
  (`1HMDual`, `BedRollFront`, `ActivateDoor`, `CartTravelDriver`); others occur nowhere in
  the extracted files (`ChairEatSoup`, `_MTSolo`). So they are not simply node names.
- **which animations belong to each set,** and whether that follows from the subtree the
  swap events enter.
- **how swap events, hand variables and attacks map to the graphs** — which transition
  each swap event fires, which selector each hand variable range picks, and which state an
  attack event reaches.

The player's furniture and idle sets are the natural place to start: each has one or two
swap events and a handful of animations.
