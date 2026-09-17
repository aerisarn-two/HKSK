# animationsetdatasinglefile.txt

What the animation set data holds, how much of it can be read back to concrete files,
and how the shipped file was put together. Every number here was measured against the
shipped game; where a question is still open it says so, with what has been ruled out.

This is an investigation in progress. §1–§4 are established; §5 lists what has not been
looked at yet. §4 was read out of the executable; how is `docs/reverse-engineering.md`.

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
set". The executable does not account for that symptom as directly as it sounds: a clip
whose file no set brought in asks for the file itself when it activates, and waits for it
(§4.4). What the set data buys is the file being there before the clip needs it.

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

## 4. The engine side

Addresses are virtual addresses in the retail `SkyrimSE.exe` (image base `0x140000000`),
read from a copy unwrapped for reading. `tools/exe-re` reproduces every one.

### 4.1 Where the file is read

The six path strings the animation text data uses are built into global `BSFixedString`s
by static initialisers (`0x14008fff0`–`0x1400900e0`) and released at exit
(`0x141718bb0`–`0x141718c00`):

    global        string
    0x14315c900   Meshes/AnimationData/
    0x14315c910   BoundAnims/
    0x14315c918   Meshes/AnimationDataSingleFile.txt
    0x14315c920   Meshes/AnimationSetData/
    0x14315c928   DirList.txt
    0x14315c930   Meshes/AnimationSetDataSingleFile.txt

The set data's globals are used by one function, the loader at **`0x14053b000`**:

- it publishes the manager as a singleton at **`0x143138d58`**;
- it asks a gate, **`bLoadCollatedAnimTextData:Animation`** (setting record `0x1420107d8`,
  read by `0x140541fa0`, compiled default **1**). "Collated" is the single file;
- with the gate on it opens `Meshes/AnimationSetDataSingleFile.txt`, reads a line of at
  most 260 characters, converts it to the project count and loops over the projects;
- with the gate off, **or when the single file does not open**, it goes to `0x14053b69b`
  and reads the split form instead: `Meshes/AnimationSetData/` and its `DirList.txt`
  (`0x14053b6f6`, `0x14053b776`).

No shipped INI names the setting, so the single file is what the game reads. The split
form is a fallback, which is also why a snapshot of it could ship out of date unnoticed
(§3.3).

The manager is created unconditionally at start-up, in `0x140541b80`, directly after the
animation data manager (`0x140536ec0`, singleton `0x143138d50`) and in the same routine
that creates the speed database (`docs/speed-data.md` §4). `0x140541e80` destroys it.

### 4.2 What the loader builds, and what reads it

**Each set becomes a 0x88-byte object**, filled by the per-set parser `0x14053d700` in
file order:

    offset   field                     from the file
    +0x00    swap events               an array of string handles, count at +0x10
    +0x18    hand variables            16-byte entries: name, min, max; count at +0x28
    +0x70    attacks                   event name, clips, and the mirrored flag as a byte
    +0x30    checksum triples          written last, when the set is finished

Two details of the parse are not visible in the file's own description. The version line
is optional: a first line starting with `V` is taken as the version, and anything else is
read straight away as the swap event count. And **a set with no swap events is given one**
— the empty string, from `0x14053d650` — so that it can still be selected.

The singleton is referenced by 15 functions. Five are its own life cycle — the loader,
the destructor path, creation and destruction at start-up and shutdown. The other ten are
consumers, in three kinds:

    consumer                     reads through     reads
    7 between 0x1407c52b0 and    0x14053bee0,      sets selected by swap event and hand
      0x1407c5fb0                  0x14053bfa0       variables, turned into file requests
    0x14069a870, 0x1407c5b00     0x14053c080       a set's checksum triples (+0x30)
    0x1403e1b20                  0x14053c0e0       the attacks (+0x70) of the set selected
                                                     by the empty swap event

**How a set is selected** (`0x14053c320`, the core of the first kind). The caller passes a
string key and, optionally, a list of variable values. The sets of the project are tried in
order, and the first that passes is taken:

1. if the key is not empty, the set qualifies only if one of its swap events **is** that
   string — handles are compared, which for interned strings is equality;
2. if the key is empty and the caller passed variable values, the swap events are not
   consulted and the set is selected on its hand variables alone;
3. every hand variable the set has must be in range — `min <= value <= max` — with the
   value taken from the caller's list, or failing that read from the behaviour graph by
   name (`0x140bb6aa0`, a hash lookup in the graph's variable table).

So **the lookup key is the swap event**, and the hand variables are ranges checked against
the graph's live variables. A creature's single set, whose swap events are empty, is the
one the empty key finds.

**How a request is built** (`0x1407c5660`, read whole; the other six share its shape):

1. it asks the reference for its animation graph manager — slot 2 of the
   `IAnimationGraphManagerHolder` vftable at `this+0x38` — and gives up if there is none;
2. **it returns without doing anything if either singleton is missing** — the set data at
   `0x143138d58`, or `AnimationFileManagerSingleton` at `0x143138e90`;
3. it calls `0x14053bfa0`, which takes a lock and walks an array on that object
   (`0x14053c530`). For each element, `0x14053cd80` reads the string the element carries at
   +0x1f0, looks it up in the manager's per-project table, selects a set as above, and adds
   what it finds to a list;
4. it hands the list to the file manager and returns the result.

Two of the seven, `0x1407c52b0` and `0x1407c5480`, take the actor and an action instead of a
key, and work the key out first (§4.3).

The file manager's three entry points, as far as they were read:

- `0x140bca970` **builds a request** — it allocates a 0x68-byte request object — and
  **does nothing when the list is empty**: it checks the list's count first and returns
  without allocating.
- `0x140bcab30` builds the same request through the same helpers, taking **one more
  pointer argument**. The consumers that call it are byte for byte the ones that call
  `0x140bca970`, except for passing that argument. It is not a release; what the extra
  argument carries was not established.
- `0x140bcad40` resolves the list, tests it (`0x140bcd5b0`), and then **calls a virtual on
  an object it was passed** — the shape of the `IAnimationSetCallbackFunctor` interface the
  executable names, a check-then-notify.

### 4.3 What sends a key to the lookup

No caller passes a literal. **Every non-empty key traced is the animation event of an idle
record** — the `ENAM` string `TESIdleForm` keeps at +0x50 — reached in one of two ways (for
idle selection and the movement controller, that the record is an idle is read from the
offsets and the idle manager's involvement, not from a type check):

- **directly**: code that has already chosen an idle passes that idle's event;
- **by resolving an action without performing it.** `0x1407c66b0` fills a
  `TESActionData` (vftable `0x14178c478`) with the actor and an action, and hands it to
  `0x14065cd10`. If the action data carries no event yet, that asks the idle manager
  (`0x1403b1f80`, on the global at `0x1420f8980`) which idle the action would pick for
  this actor now; the idle's event is copied into the action data at +0x28 and returned
  as the key. Nothing is sent to the graph. A null action falls back to default object
  0x40, `Action Idle`.

Actions are default objects, addressed by index: the manager at `0x1420f5600` holds the
objects from +0x20 and a loaded flag per index from +0xb90, and their names are a table of
24-byte records at `0x141fd8f50`, name pointer first. The indices below are read from that
table.

    who                                        key                                    through
    equipping: 0x14070c9c0, 0x14070cbe0,       Action Draw (0x43) or, with the          0x1407c52b0, 0x1407c5480
      from 0x1406e97b0 and 0x1406e0070           weapon out, Action Force Equip (0x51)    → 0x140bca970, 0x140bcab30
    the same equip path, 0x1406e97b0           empty                                    0x1407c59f0 → 0x140bcab30
    action handling: 0x1406f0790 (from         the event already resolved into the      0x1407c5660 → 0x140bca970
      0x1406ddc50, 0x1406dde70), 0x1406dfbc0     action data, +0x28
      (from 0x140666230)
    idle selection: 0x1407128c0, nine callers  the chosen idle's event, +0x50           0x1407c57a0 → 0x140bcab30
    MovementControllerNPC (vftable             +0x58 of each 0x68-byte record in a      0x1407c5660 → 0x140bca970
      0x1418b14b0, slot 1: 0x140782510)          list built from the idles resolved for
                                                 Action Path Start (0x53), Path End
                                                 (0x54), Large Movement Delta (0x55)
                                                 and Move Stop (0x62)
    Actor's graph holder, slot 3: 0x1406a3520  empty, then Action Draw                  0x1407c5fb0, 0x1407c5ea0
      (vftable 0x14189e1e8, subobject +0x38)                                              → 0x140bcad40

**The equip path selects on the hands being equipped.** `0x14070c9c0` builds the variable
list from its own arguments: the value for `iLeftHandType` and the value for
`iRightHandType` (the interned names at +0x388 and +0x390 of the struct `0x14014ef60`
returns). `0x1406e97b0` reads the two values from +0x304 and +0x306 of an object it holds,
and picks Force Equip over Draw when the actor's weapon-state field (+0xcc, bits 5–7) is 3
or more. Because the caller supplies both values, the selector does not consult the graph's
own hand variables (§4.2, rule 3).

**The checksum readers key the same way.** `0x1407c5b00` is called from slot 25 of
`QueuedActor`, `QueuedCharacter` and `QueuedPlayer` (`0x140193280`) — the background
load of an actor's 3D — once with Action Draw and once with index 0x16e, one past the last
default object. What that index resolves to was not settled: the flag it reads lies past
the table, and the null-action fallback applies only if that byte is zero.

Two consumers, `0x1407c58f0` and the wrapper `0x1407c5e00`, have no direct callers in the
disassembly. Tail jumps and tables of function pointers were not searched for them.

### 4.4 How an animation file gets loaded

There are three ways, and the set data drives one of them.

**Ahead of need, from the set data** — §4.2 and §4.3: a set is selected, and its checksums
become a request to `AnimationFileManagerSingleton`.

**When a clip activates.** The executable names the interface the file manager implements:
`IAnimationClipLoaderSingleton` (vftable `0x141989cd8`, all slots pure), implemented by
`AnimationFileManagerSingleton` (`0x141989d10`). Start-up copies the file manager pointer
to a second global, **`0x1431b2820`**, which the behaviour runtime reads — this is how the
clip generators reach it without naming it. `hkbClipGenerator::activate` (`0x140acd750`,
named by its profiling string), when the clip's `userData` (+0x30) is 0, calls slot 1
(`0x140bcbb90`), which:

1. maps the clip's `animationBindingIndex` (+0x70) to a file ID through the character's
   per-animation table (`0x140bb3c20`: 32-byte entries, the 12-byte ID first);
2. if the file is already loaded, or the entry holds no ID, binds what there is and sets
   `userData` to 0xc;
3. if the resource lookup `0x140d08da0` fails, sets it to 4 and stops;
4. otherwise demands the file — one at a time directly (`0x140bcc950` → `0x140ba8800`),
   the rest into a queue ordered by how many clips are waiting for each (`0x140bcc490`, a
   heap of 16-byte {count, ID} entries) — and sets `userData` to 8.

`update` (`0x140acde40`) calls slot 2 while `userData` is 8 and skips its own update until
the file is bound; `0x140ace1e0`, two slots further in the same vftable, calls slot 3.
`BSSynchronizedClipGenerator` (`0x140b93ef0`, `0x140b94170`, `0x140b94260`) and
`BSOffsetAnimationGenerator` (`0x140b97e80`, `0x140b97f20`, `0x140b98150`) make the same
calls.

The per-animation table is built when an actor's graph is constructed (`0x140bc26d0`).
For each entry of the character file's animation list — `hkbCharacterStringData`, names
at +0x30 with the count at +0x38, file names at +0x40 with the count at +0x48 — the path
is resolved under the character's folder and turned into a resource ID (`0x140d0f4c0`),
the same folder, file and extension parts the set data's checksums hold (§2). **The file a
clip demands is named by the character file, not by the set data.**

**Up front, when the graph is built.** Graph construction (`0x140bb0800`, from
`0x14054ba10` and `0x14054c4d0`) asks the reference, through slot 6 of its
`IAnimationGraphManagerHolder` vftable, whether to load clips now, and passes the answer to
`0x140bc21e0`, which picks the Havok asset loader: `BSResourceAssetLoader` (vftable
`0x1419892a8`) for yes, `NullAssetLoader` (`0x1419892d8`, whose load slot returns null) for
no. Linking then calls the loader's slot 3 for every entry of the character's animation list
(`0x140bc8a50`), and again from the pass over the graph's `hkbClipGenerator`s
(`0x140bc9310`) for clips that need it; with the real loader that loads each file there and
then. The answer is a constant of the class:

    slot 6        returns   classes
    0x1402f3d80   1         TESObjectREFR, Projectile and its kinds, Explosion,
                            ChainExplosion, Hazard
    0x14069d7a0   0         Actor, Character, PlayerCharacter

So **an animated object loads every clip up front, always; an actor never does**, and gets
all of its files through the file manager — the set data's requests ahead of need, and each
clip's own demand as it activates. Neither choice reads any setting.

### 4.5 Is it necessary

**Not strictly, under the default settings: it decides what is loaded ahead of need, and a
clip it misses loads when it first plays, late.**

A second setting, **`bInitiallyLoadAllClips:Animation`** (record `0x142015620`, compiled
default **0**), is read in one place, `0x1407c50d0`, which caches its negation at
`0x1431a939c`. Start-up (`0x1407c4d80`) creates `AnimationFileManagerSingleton` only when
that cached flag is set, and shutdown (`0x1407c4f90`) mirrors it. `0x1407c50d0` has no other
callers, so the setting's whole effect is whether the file manager exists.

- **`bInitiallyLoadAllClips = 0`, the default.** The file manager exists. Its request
  entry `0x140bca970` has four callers: three set data consumers, and a wrapper `0x140bcb710`
  whose only caller, `0x1407c6170`, feeds it an `AnimationStreamLoadGame` — a reader over
  the save game, every one of whose virtuals calls the same stream read. The second request
  entry `0x140bcab30` is the same: three consumers, and a wrapper `0x140bcb820` whose only
  caller, `0x1407c6290`, builds the same save-game reader. Beside those requests, every clip
  that activates without its file demands it (§4.4).
- **`bInitiallyLoadAllClips = 1`.** The file manager is never created. The set data
  consumers return before selecting anything, and the clip generators skip their demand —
  both reach the file manager, and both test for it. Graph construction still gives actors
  the null loader (§4.4). No other code that loads an actor's clips was found, so read
  literally the setting leaves actors without animations and changes nothing for animated
  objects. That is a reading of the code, not a test. Two consumers, `0x14069a870`
  (checksums) and `0x1403e1b20` (attacks), do not test for the file manager, so the set
  data is still read in that mode for its attacks and checksums.

**What that means for the modding symptom in §0.** Under the default settings, an animation
whose checksum is missing from the set the game looks up is not requested ahead of need.
When a clip that plays it activates, the clip demands the file, holds its update until the
file is bound, and plays from then on; if the file has not arrived by the time the state is
left, it never showed. That delay fits a transition or an attack that seems not to play;
it does not by itself explain an animation that never plays however long its state lasts.
Whether that happens in game is open (§5).

**With neither the single file nor the split folder**, the loader builds the `DirList.txt`
path, reads it into a list, and when the list is empty jumps straight to its end
(`0x14053bd86`) — no message, no error path; the function references no string at all. The
manager is still created (§4.1) and holds no projects. Every lookup then fails at the
project table: `0x14053cd80`'s not-found path adds nothing, the request list stays empty,
and `0x140bca970` returns without building a request. Nothing is loaded ahead of need; by
§4.4 each clip's first activation still demands its file.

## 5. Not yet examined

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

In the executable:

- **what `0x140bcab30`'s extra argument carries.** Its callers are one of the two equip
  entries, the equip path's empty-key request, idle selection and one save-game restore;
  the other equip entry, action handling and movement call `0x140bca970`.
- **what index 0x16e resolves to** in the queued 3D load (§4.3), and who reaches
  `0x1407c58f0` and `0x1407c5e00`.
- **which field the movement controller's records hold at +0x58.** The records come from
  the idle manager; that the field is the idle's event is likely, not shown.

In game, the test that decides §4.5: remove one travelling clip's checksum from a creature's
set, and see whether that clip plays late, plays after the first time, or never plays.
