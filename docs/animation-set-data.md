# animationsetdatasinglefile.txt

What the animation set data holds, how much of it can be read back to concrete files,
and how the shipped file was put together. Every number here was measured against the
shipped game; where a question is still open it says so, with what has been ruled out.

This is an investigation in progress. §1–§4 are established, §5 rebuilds the file from the
other assets, and §6 lists what has not been looked at yet. §4 was read out of the
executable; how is `docs/reverse-engineering.md`.

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
        <attack count>, then per attack: event, moving-attack flag, clip count, clips
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
  685 attacks name one clip, 50 several, 2 none. The flag after the event is only ever 0 or
  1, set on 124: it marks a moving attack (§4.6), and was long misnamed "mirrored".
- **Checksums**: 77 of the 990 sets name no animations at all, which is normal.

The `V3` line is not decoration: the parser reads the number after the `V` and compares it
with 3 (`0x14053de18`). At 3 and above each checksum entry is the three numbers above;
below 3 it is a single line, read and thrown away. Every shipped set says `V3`.

## 2. The checksums decode completely

Each animation is three lines: the checksum of its folder, the checksum of its file name,
and `7891816` — `0x786B68`, the bytes of `hkx` reversed, a constant rather than a
checksum. The checksum is CRC-32 over the lower-cased text, polynomial `0x04C11DB7`,
reflected in and out, **with no initial value and no final xor** — not the zlib variant,
which is why a stock CRC-32 does not reproduce these numbers (`HKSK.Cache.HavokCrc`).

The three land in a 12-byte record in a different order from the file's:
`0x14053de91`–`0x14053debf` writes the **name** checksum at +0x00, the `hkx` constant at
+0x04 and the **folder** checksum at +0x08. That is `BSResource::ID` — file, extension,
directory — which is what §4.4's loader takes, and it settles which of the first two lines
is the folder and which the stem.

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

§3.3 is why no rule over the final assets settles this: the counterparts were **moved** into
these creatures' sets from first person's, and the creatures they were not moved for are
the ones that still have them in first person's.

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

### 3.3 The shipped file is a generated base plus later bulk edits

The split form under `animationsetdata/` is a **pre-DLC snapshot**: 39 projects, no
Dawnguard or Dragonborn creature. Its set files are exact counterparts of blocks in the
single file, which makes the snapshot a record of what changed between the two.

    sets present in both                     813
    identical                                609
    different                                204

Decoding what changed, file by file, shows edits of three kinds rather than scattered ones.

**First-person killmoves moved from first person to the victim.** 53 first-person killmove
files are in first person's sets in the snapshot, in the creature's in the shipped file,
and no longer first person's:

    creature                    moved   sets they are in
    DraugrProject                 6     23 of 25 -- copied into every set
    DraugrSkeletonProject         6     23 of 25 -- the same six
    FalmerProject                 6     12 of 15
    BearProject                   5     its one set
    WolfProject                   5
    DragonProject                 5
    SabreCatProject               5
    TrollProject                  5
    SteamProject                  4     7 of 7
    FrostbiteSpiderProject        4     listed twice
    HagravenProject               4
    SprigganData                  4

The wolf's `Paired_ExtractWerewolfSpirit` and the werewolf's own were copied rather than
moved: first person still lists them.
(`TheFirstPersonKillmovesMovedFromFirstPersonToTheVictim` holds the bear's.) The creatures
added by Dawnguard and Dragonborn -- boar, riekling, scrib, lurker, chaurus flyer,
gargoyle, ballista -- keep theirs in first person's sets: 33 first-person killmove files
are still first person's in the shipped file, against 60 in the snapshot. Two conventions
coexist, and which one a creature has is when it was made, not anything in its assets.

The copy into every set is the edit `HKSK.Model.ActorProject.AddAnimation` makes --
register an animation with every set of the project -- which is why the draugr carries the
same six killmoves in its taunt and furniture sets.

**The player gained the DLC killmoves and lost the werewolf pairs.** The same bulk edit on
the third-person player: the boar, riekling, scrib and lurker killmoves and
`Paired_DLC02RipHeartOut` added to 8 sets (28 for the heart), and the four
`human&werewolf` feeding and mauling pairs removed from 17 -- `bow.txt` 123 to 118 is those
four and one dragon pair. `_MTSolo.txt` 149 to 106 is a different edit: 38 horse-riding
animations and 7 others left the base set, with 2 Dragonborn animations added. First person's seven weapon sets lost the
moved killmoves, 13 to 16 each.

**The dragon gained its Dawnguard animations** -- the summon and water-dive specials and the
mount and dismount pairs, 8 files beside its five moved killmoves -- and the falmer two
staff animations.

None of this is in the behaviour graphs, which is why a rebuild cannot reproduce it.

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
    +0x70    attacks                   0x18-byte entries: event name, clip list, and the
                                       moving-attack flag as a byte at +0x10 (`atoi > 0`)
    +0x30    checksum triples          written last, when the set is finished

Two details of the parse are not visible in the file's own description. The version line
is optional: a first line starting with `V` is taken as the version, and anything else is
read straight away as the swap event count. And **a set with no swap events is given one**
— the empty string, from `0x14053d650` — so that it can still be selected.

**The set names are read and discarded.** The project loop at `0x14053b200`–`0x14053b243`
reads each name with the readline helper into one scratch buffer on the stack, overwriting
it every time, and only then allocates the `count` sets of 0x88 bytes. Nothing keeps them,
and no field of a set holds one: a set is addressed by its index, so **only the order
matters**. It is in the split form, where the names are file names, that they are
load-bearing.

The singleton is referenced by 15 functions. Five are its own life cycle — the loader,
the destructor path, creation and destruction at start-up and shutdown. The other ten are
consumers, in three kinds:

    consumer                     reads through     reads
    7 between 0x1407c52b0 and    0x14053bee0,      sets selected by swap event and hand
      0x1407c5fb0                  0x14053bfa0       variables, turned into file requests
    0x14069a870, 0x1407c5b00     0x14053c080       a set's checksum triples (+0x30)
    0x1403e1b20                  0x14053c0e0       the attacks (+0x70) of the set the hand
                                                     types select, for a race (§4.6)

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
Whether that happens in game is open (§6).

**With neither the single file nor the split folder**, the loader builds the `DirList.txt`
path, reads it into a list, and when the list is empty jumps straight to its end
(`0x14053bd86`) — no message, no error path; the function references no string at all. The
manager is still created (§4.1) and holds no projects. Every lookup then fails at the
project table: `0x14053cd80`'s not-found path adds nothing, the request list stays empty,
and `0x140bca970` returns without building a request. Nothing is loaded ahead of need; by
§4.4 each clip's first activation still demands its file.

### 4.6 Who reads the attacks

`0x1403e1b20` is called from one place, a `TESRace` virtual (`0x1403de860`), and fills the
race's `AttackAnimationArrayMap` (vftable `0x1417e7e60`, named by RTTI) for each sex's
behaviour graph. It asks the set data once per weapon combination (loop at `0x1403e1e00`):
right hand type 0 to 12, and for each, left hand type 0 to 12 -- except for the types a byte
table marks as two-handed, 5, 6, 7 and 12, where the left hand is asked as the same type.
That is 9 x 13 + 4 = 121 combinations, each stored under `right << 16 | left`.

Each question goes through `0x14053c0e0`: an empty key, and the two hand types as values.
By the selector's rules (§4.2) **a set with no hand variables never qualifies** for that
question -- with values given, a set is taken only on hand variables it has. When nothing
qualifies and the project has exactly one set, that set is used (`0x14053c131`). So:

- a creature's single set carries its attacks, hand variables or not;
- in a project with several sets, only sets with hand variables supply attacks, and the
  attacks on its base set are never read.

**The flag after an attack's event is "moving attack", not "mirrored".** It was named
mirrored in HKSK's first reader and never checked, and it follows no clip's mirror bit: 122
shipped attacks set it with no mirrored clip, 19 clear it with every clip mirrored. Its
reader is the melee combat context:

- `0x1406b7810` builds the actor's key, `right << 16 | left` from its equipped items, and
  `0x1406b7870` takes the race's attack array for it.
- `0x1408a3d30` (reached from `CombatBehaviorTreeCreateContextNode2<CombatBehaviorContextMelee>`)
  walks the combat attacks the actor can make, finds each one's set data entry by event name,
  and hands its clip names to `0x140442a80`. That asks the animation data manager
  (`0x143138d50`, the `animationdatasinglefile.txt` side) for the clips' movement and the
  time of their `HitFrame` annotation, and returns the time, the translation at the end of
  the clip and the translation at the hit frame.
- It then stores a reach at the entry's `+0x20`: **-1 when the flag is set** (the constant at
  `0x141769578`), otherwise `sqrt(x^2+y^2+z^2)` of the final translation, scaled by the actor.
  The hit frame's time goes to `+0x24`, the two translations to `+0x08` and `+0x14`, both
  already scaled. The actor's longest reach, kept with `maxss` at `+0x30` of the context,
  therefore rises with a cleared flag and never with a set one.
- **What a cleared flag costs, in the check** (`0x1408a2ee0`): `comiss` against zero, and a
  reach of zero or more **skips the attacker's own speed entirely** -- the call that fetches
  it (`0x14069c550`) is inside the branch. What is left is the clip's root motion, plus the
  base the context carries, plus one combat setting from `+0x1a8`, plus the **target's**
  speed times the time to the hit frame; squared and compared with the distance between the
  two actors (`0x140855660`), and the function returns false when the actors are farther
  apart than that. For an attack whose clip does not travel, the first term is 0: the actor
  will not swing until it is already within the base reach, and its own approach counts for
  nothing. Where the flag is set, the reach **replaces** that term with the attacker's speed
  times the same time -- the clip's travel is not added to it, it is the alternative to it.
- **The flag chooses a predictor twice.** Past the early-out, the hit frame's translation is
  rotated into the actor's frame (`0x1402e8840`) and added to its position to say where the
  blow lands, whatever the flag. Then the check tests the reach against zero a second time
  (`0x1408a31bd`) and predicts **where the attacker will be** when it lands, two ways:

      flag set    0x140853fb0 projects the actor forward along its own motion for that time
      flag clear  the end translation, rotated by the actor's matrix, is added to its
                  position -- but only when the reach is more than 5.0 units; at or below
                  that the branch is skipped and the actor is modelled as not moving at all

  The target's position is predicted the same way, and the distance between the two
  *predicted* positions is what the rest of the check works on: against 32.0 first, then
  against a squared base-plus-setting, with 128.0, 2.0 and 0.9 in the arc tests
  (`0x1408dc090`, `0x1408544a0`). So the flag is not a reach bonus, it is which of two
  estimates of the attacker's future position the AI trusts -- and a cleared flag on a clip
  that travels nothing fails the 5.0 gate, which is the state 21 shipped attacks are in.
- The check is reached from `CombatBehaviorAttack` and `CombatBehaviorBash`, and the
  attacker's speed it uses on the set-flag path comes from the actor's process,
  `+0xf8 -> +0x8 -> +0x2a8`. The `fCombatAttackMovingAttackDistance`,
  `fCombatAttackMovingAttackReachMult` and `fCombatAttackMovingStrikeAngleMult` settings were
  tied to this function by an earlier search; **what the instructions show is one settings
  float read from `+0x1a8` of the combat settings structure, on the path both flags take** --
  the settings are reached by offset into a structure rather than as three globals, and which
  of them `+0x1a8` is has not been established. Do not repeat the claim that the flag gates
  them without checking that.

So the flag says where an attack's travel comes from, and the 38 events it is set on say it
plainly: **it marks an attack the character makes while it is still moving.** It says so only
where it was used, though -- 6 of the 45 projects with attacks, the rest 0 throughout (§6).

- The **netch** has three and is the whole argument in one project: `attackStartLeft` and
  `attackStartRight`, the swipes it makes drifting, are set; `attackStartPowerStanding` is
  the one it stops for, and is clear.
- The **witchlight** has one attack and it is set; the **storm atronach** has three and all
  three are. Neither ever stands still -- a hovering creature is moving whatever it plays.
- The **werewolf** sets it on its plain left and right attacks, whose states hold
  `LeftAttackRunningDirectionalBlend` and its eight directional running-attack clips; on the
  dual and backhand attacks, whose states blend `LocomotionCombatBlend` and the locomotion
  `DirectionBlend` under the attack; and on the run-power and side attacks. It is clear on
  the two power combos and the howl, whose states hold their own clips and nothing else.
- The **player** sets it on the plain `attackStart` of every weapon type, the sprint
  attacks, both hand-to-hand hooks and the shield bash of the hand-to-hand sets. It is clear
  on every directional and standing power attack, every other bash, the dual-wield specials
  and the mounted attacks -- the ones that plant the character and carry it by root motion.

An attack written without it is judged by its clip's own travel; for an in-place swing made
while running, that is a reach of zero where the game expects the run's.

`animationdatasinglefile.txt` is therefore read by combat through the set data: the `HitFrame`
annotation and the root motion of the clips an attack lists are what the AI measures
attacks with. The lookup's result is not checked: a clip the cache has no movement or
`HitFrame` for leaves the time at zero and the translations at their defaults.

## 5. Rebuilding the file

`HKSK.SetData.SetDataGenerator` writes the file from the other assets, and `tools/setgen`
runs it with the masters. It does not try to reproduce the shipped file, which records how
that file was edited (§3.3); it builds what the executable reads the file for (§4), and the
shipped file is the check.

### 5.1 Inputs

- **The behaviour graphs and the animation cache**, from the extracted meshes.
- **The idle events**, from the masters: every idle record's animation event. They are the
  only keys the lookup is ever given (§4.3) -- 1,246 of them.
- **The equip events**: the events of idle records under the idle roots named for drawing
  and equipping (`DrawSheathRoot`, `ForceEquipRoot`, `WerewolfDrawRoot`, ...), 36 of them.
  The equip path resolves Action Draw and Action Force Equip through that part of the tree.
  This is the one heuristic input: the roots are picked by name.
- **Each project's attack events**, from the attack data of the races whose behaviour graph
  it is. 330 of the shipped file's attack events are there, 11 are not.

The shipped set data is never read; `setgen` empties it before generating.

### 5.2 A file is named by the character, not by the clip

A clip generator stores an animation path, and it is **not** the file the clip plays. The
engine binds a clip to the character's animation list by the clip's name, through the
animation cache, and the file is what the character lists at that index (§4.4). The male
and female characters share one behaviour whose clips say `Animations\male\...`;
`defaultfemale.hkx` lists `Animations\female\...` at the same indices. Taking the stored
path gives every female idle set files that are not hers. The stored path is used only
for a clip the cache does not list.

### 5.3 One set, or several

**A project that never chooses by hand type gets one set**: its whole character list, in
order, with its attacks. That is what the shipped file does for 40 creatures, and by §4.6 it
is also what keeps their attacks readable. 38 of the 49 projects come out this way; 26 of
them are identical to the shipped set, and the other twelve differ by the shipped file's
history (§3.2). The atronach storm and the dragon priest become multi-set: their graphs
choose by hand type, which the shipped file ignored.

A project that does choose by hand type gets a base set, weapon sets, and a set per event.

### 5.4 A set per event: until the graph is home again

A behaviour is a graph, not a tree, and a cyclic one: an idle's exit returns to the
default state, from which every other idle is entered. So the question "which files does
an event lead to" needs a boundary, and four were measured (the idle-key column is the
measure in §5.7: files of a shipped idle set that its own keys load):

    rule                                                    idle keys   listed    size
    trigger    -- states reached from the event's targets
                  through transitions on no key                58.5%     31,723    1.6 MB
    dominance  -- states only reachable through the
                  event's targets                              72.7%     85,041    3.3 MB
    follow     -- trigger, plus what the key transitions
                  leaving those states trigger                 73.6%     51,773    2.2 MB
    until home -- every transition, until a state the graph
                  reaches with no key                          79.8%     67,034    2.7 MB
    until home or another idle's door                          79.8%     57,495    2.4 MB

The generator uses **until home, or another idle's door**. The graph (`HKSK.SetData.StateGraph`) has a node per
state of a machine; a state's own clips are those below its generator down to the machines
nested there; edges are a nested machine's entry, a state's own transitions, a machine's
wildcards and its random-transition event from every state, and the nested state a
transition names. **Home** is what the root reaches through nesting and transitions on no
key. An event's set is every state reachable from the states it enters without passing
through home, less home's files -- and without entering another idle's **door**, a state a
key enters straight from a home state. A chair's entry is a door, entered from standing; its
exit and its next clip, entered from inside the chair, are not. Without doors the walk from
a standing drink went on into every piece of furniture a drink can be carried to: 40 of the
player's idle sets held over 100 files, `idleDrinkingStandingStart` 414 of them. With doors
the idle measure loses 2 files of 2,147 and the file 9,500 animations; the sets still over 100
files are hubs by nature -- `moveStart` and `moveStop`, sneaking, sprinting, and
`00NextClip`, the key that advances every idle loop.

Why the others fall short:

- **trigger** stops at every key, and an idle's exit, its next clip and its variants hang
  off keys (`IdleChairExitStart`, `00NextClip`). Shipped idle sets hold them with the entry.
- **dominance** gives an event what no other event reaches. A chair's exit is reachable from
  all four chair entries, so no entry gets it; and everything below a state is dominated by
  it, so the eight keys that enter the default state (`IdleStop`, `Ragdoll`,
  `bleedOutStop`, ...) each got all of locomotion, 1,300 files.
- **follow** by key was worse still: following `00NextClip` or `IdleChairExitStart` as a key
  takes every exit that key has, anywhere -- measured on the player, following
  `00NextClip` recovered 166 shipped files at the cost of 21,963 the shipped sets do not
  hold. Following the *transitions* that leave the event's own states fixes that, and
  still stops one step short of a loop's third variant.

**Home only bounds anything once random starts are edges.** The player has 105 machines
that start in a random state and 71 that start from a sync variable, and 4 with a
random-transition event. Taken as starting in their `m_startStateId`, the root reached 32
clips with no key, and every "until home" walk ran into most of the behaviour; taken as
entering any state, home is 222 states on the player and the rule holds.

A transition that names a nested state enters that nested state, not its container: the
container is entered by every event naming one of its states.

**The inverse question** -- start from each animation and ask which events trigger it --
is the same graph read backwards, and it is how the rule was chosen. On the player, with a
one-handed weapon out, the files by how many keys trigger them directly:

    keys   where the shipped file lists them
    1      idle 580   weapon 159   base 11
    2      idle 119   weapon 76    base 2
    3-4    idle 61    weapon 36    base 4
    5-8    weapon 19  idle 14      base 4
    9-16   idle 22    base 16      weapon 11
    home   weapon 73  idle 31      base 2  (and base with weapon sets)

The shipped idle sets are files one to four keys trigger; home files are the base's and the
weapons'. And of the 1,139 files of the player's shipped idle sets: 670 are triggered by one
of the set's keys, 256 by a key transition leaving the states those trigger, 129 belong to
keys no transition takes, 74 are only triggered by unrelated keys, and 10 are not played by
the graph at all.

### 5.5 Weapons

The graph is built for each of the 121 combinations the race asks about, and choices bound
to a hand type are resolved against it (a selector's index, a machine's start state, a
condition that reads only hand types). Graphs that come out the same are built once.

- **A weapon set** holds what a combination reaches that not every weapon a character can
  hold reaches: its reach less `common`, the reach every holdable combination shares. The
  "holdable" matters. The game also asks about
  a two-handed weapon in the left hand alone, `(1, 12)`, which nobody can equip, and the
  player's behaviour sends those nowhere: taken over all 121, `common` shrank to almost
  nothing and every weapon set held about 1,100 files. The equip events' regions and the
  combination's attacks go on the same set, and its keys are the equip events.
- **An event whose region depends on the weapon** gets a set per group of combinations.
- **Groups are covered by ranges**: a set states one inclusive range per variable, so a
  group is written as rectangles of (right, left), one set each. A combination nobody is
  asked about may fall inside a rectangle.
- **Groups are merged within a slack**: cells whose union stays within 1.5 times the
  largest become one set (`--slack`). Without it the player's `moveStart` alone was 60 sets
  of 150 files each; its union is 247. Attacks are never merged across cells that differ,
  because the race reads them per combination.

**The base set** holds every file the character lists that no other set holds. It is what
an actor's graph loads when it is built (§4.3).

**Order matters for attacks.** The race takes the first set whose hand ranges hold, and a
set split by weapon for an idle event holds no attacks. Sorted by name, `IdleStop_R0-4_L0-11`
came before `Weapon_R0-0_L0-0` and answered the empty hands with nothing: the player, the
draugr, the falmer, the riekling and the sphere centurion lost their hand-to-hand attacks.
Sets with attacks come first.

**Grouping, not coverage, is where weapons differ from the shipped file.** Replaying the
equip lookup -- an equip key with a shipped weapon set's hand types -- the chosen set and the
base load 68.3% of that shipped set's files. But 98.1% of them are in *some* set that applies
under that weapon's hand types: the shipped file loads a weapon's sneak, sprint and shout
variants on equip, and the rebuild loads them on `SneakStart`, `sprintStart` and
`shoutStart` for that weapon. What is in no applicable set is the moved first-person
killmoves (§3.3) and the bulk edit's own oddities: `2HW_AttackForwardSprint` is listed under
bows, crossbows and every magic combination, `MLh_Unequip` under staves.

Sync start states were tried as "where the variable's writers are" rather than "any state",
to stop a greatsword reaching the one-handed first-person idles. It changed nothing that
matters: the sync variables on the player (`iSyncIdleState`, `iSyncSprintState`,
`iIsInSneak`) mirror a mode, not a weapon, and the greatsword's readied state genuinely sits
inside the one-handed behaviour file.

### 5.6 Attacks

An attack's clips follow the transition the event takes from each state (a state's own
before the machine's wildcards), into the state it enters, narrowed by the nested state the
transition names, or by the event's own transitions in the machine below, or else its start
state. The nested state is what matters: the chaurus's eleven attacks all enter
`AttackState`, and each transition names the state inside it. Against the shipped attacks,
over the 121 combinations the race asks about, 22,949 of 27,039 (84.9%) event-to-clips
entries are identical, and every one of the 4,538 combinations the shipped file gives a race
attacks for gets attacks. Clip names are listed once each: two branches can hold a clip of the
same name.

Of the entries that differ, 855 are events the races do not list (the dog's and mammoth's
`attackStart_ForwardPowerShort`, the goat's `attackStart_Attack2`) or that no transition
takes (the werewolf's `AttackStartHowlExplode`) -- nothing asks for them. The rest are
follow-on clips the shipped file lists with the attack (the lurker's and giant's `x2`, the
spider centurion's head layer) and choices made by variables other than hand types, which
are left open: the draugr's `bashStart` lists every weapon's bash.

### 5.7 What comes out

    projects 49     sets 2,241     animations listed 57,495     attacks 3,819     2.4 MB

(shipped: 990 sets, 20,807 animations, 737 attacks, 0.8 MB)

    every file the shipped data lists, in some set of the project      6,758 of 6,829
      not: first-person killmoves moved or copied to the victim (§3.3)    66
      not: the werewolf's human-side killmoves, which its character
           file does not list                                              5
    files of a shipped idle set that its own keys load                 2,145 of 2,689 (79.8%)
    files of a shipped weapon set in some set applicable to the weapon 15,248 of 15,538 (98.1%)
    attack entries identical over the 121 combinations                 22,949 of 27,039 (84.9%)

Of the 542 not loaded, most are not derivable from the graphs:

- **296 belong to keys no transition takes.** The dialogue idles `idle_A_left_longTrans`,
  `idle_A_sigh_var1Trans` and the others name their animation (`MT_idle_A_left_long`), but
  no event of that name exists anywhere in the player's behaviour -- not in a transition, not
  in any other field. The behaviour now plays those clips from a random dialogue pool on
  `MotionDrivenDialogueNextClip`. `IdleDrunk` and `IdleNeutralLeft` are the same.
- **192 are in no set**: the draugr's, skeleton's, falmer's and steam centurion's moved
  first-person killmoves, copied into every set (§3.3).
- **54 are in another key's set.** The shipped file lists them with this one: the carry
  offsets with the stone and wood pick-ups, each horn blow's animation in the other's set,
  dialogue expressions shared by several dialogue idles.

That puts the derivable part at 2,145 of about 2,200.

`SetDataRebuildTests` holds these numbers.

The moving-attack flag is derived **in part** (§6), by `GraphReach.TravelChosenBySpeed`: the
attack's travel is the actor's when a blender above its clips is parametric on speed, or when the
graph raises `IsSprinting` over the state. That yields 32 (project, event) pairs -- **28 of
vanilla's 38** -- and four vanilla does not have, all of which look like vanilla's own omissions:
the Vampire Lord's two, which carry the player's speed-parametric blend in a project that flags
nothing, and the werewolf's `AttackStartLeftSprinting` and `AttackStartRightSprinting`, which it
leaves clear while flagging `AttackStartDualSprinting` beside them. The 10 not derived are the storm
atronach's three and the witchlight's one -- projects where *every* attack is flagged, so the
fact is about the creature and not the attack -- the werewolf's side and running-power attacks,
and the player's `bashStart`, which vanilla flags only in the hand-to-hand sets.
For those, `setgen --flags-from <shipped file>` takes the flag, and nothing else, from a shipped
file:

    attacks flagged as moving              1,180 of 3,819
    attacks the shipped file answered      3,305; the other 514 nobody shipped
    flagged events reproduced              38 of 38, and no event flagged that vanilla does not

The 38 become 1,180 entries because a rebuilt project states an attack once per set that can
make it, where the shipped file states the same 38 across its own 124.

### 5.8 Traps

- **Comparing Havok objects by value.** HKX2's objects compare and hash by value, and a
  state machine's value is most of the behaviour below it -- through cycles. A set of
  (machine, state) keyed that way made a single event's walk take eight seconds. Every set
  and dictionary over graph nodes has to compare by reference.
- **A wildcard is offered once per state.** Collecting an event's transitions state by
  state gives a wildcard once for every state it leaves; walking each one repeats the same
  walk that many times.
- **Counting the weapons the game asks about as weapons someone holds** (§5.5).
- **Reading only a machine's `m_startStateId`.** Random and sync starts enter every state,
  and a random-transition event connects every state to every other; without them the
  graph's home is a handful of states and every bounded walk is unbounded (§5.4).
- **Sorting sets by name.** The race takes the first set whose hand ranges hold (§5.5).
- **Following a key instead of a transition.** A generic key -- `00NextClip`,
  `IdleChairExitStart` -- leaves hundreds of states; what belongs with an idle is the
  transition that leaves *its* states.

## 6. Not yet examined

The nine multi-set projects — the player (374 sets each for `DefaultMale` and
`DefaultFemale`), first person (109), the draugr and skeleton (25 each), the falmer (15),
the riekling (12), the sphere centurion (9) and the dwarven centurion (7):

- **where set names come from.** Some occur as strings in the player's behaviours
  (`1HMDual`, `BedRollFront`, `ActivateDoor`, `CartTravelDriver`); others occur nowhere in
  the extracted files (`ChairEatSoup`, `_MTSolo`). The engine never reads them (§4.2), so
  the rebuild names its own (§5).
- **how the shipped file grouped keys into sets.** The rebuild merges keys that load the
  same files; the shipped groups follow the idle tree's authoring, an entry's variants
  together.
- **the attacks that differ** (§5.6): 15.1% of the event-to-clips entries.
- **the moving-attack flag** (§4.6) is derived for two of its three families -- 28 of vanilla's
  38 -- and copied for the rest; `setgen --flags-from` is still the only way to reproduce all 38.
  What it means is read, and §4.6's creatures agree with the name. What decides it is not in
  the assets, and the reason is **coverage**: of the 45 projects that carry attacks at all,
  **6 use the flag** — the player's two, the werewolf, the netch, the witchlight and the
  storm atronach. Four of those six are single-set creatures, whose one set holds every
  attack they own and no hand variables, so the race reaches them only through the
  single-set fallback (§4.6): the werewolf flags 10 of its 15, the storm atronach 3 of 3,
  the netch 2 of 3, the witchlight its only one. Of the nine multi-set projects only the
  player's two use it -- the draugr and the skeleton carry 56 attacks each across 25 sets
  and flag none. The other 39 projects are 0 throughout, and they include creatures that
  plainly attack on the move:

      bear        attackStart_AttackLeft1, a bite made charging, travel 99.6   0
      sabrecat    eight attacks, two of them lunges                            0
      wolf        attackStart_Attack1 and the two skeever lunges               0
      dragon      attackStartBite, blended over the flight tree                0
      chaurus flyer, horker, troll                                             0

  So no rule that matches the player can hold everywhere, because the shipped file does not:
  any predicate strong enough to flag `attackStart` for the player flags the bear's run-bite
  too. It was applied to six projects and to no other, which is the mark of tuning rather
  than of a property.

  What those six are was looked for and not found. They are not the DLC projects -- four of
  them are in the pre-DLC snapshot under `animationsetdata/`, and nine creatures that came
  with a DLC flag nothing. They are not a later edit: **no flag differs between the split
  pre-DLC files and the merged one**, the only attack entries added since being the player's
  two mounted-combat attacks, both 0. They are not the creatures that float, nor the ones
  whose attacks have no root motion to measure -- 164 of the 289 clear attack events name
  clips that travel nothing at all, the same as 19 of the 34 flagged ones. Two pairs put it
  past argument:

      flame atronach      4 attacks, named clips travel 0      all clear
      frost atronach      6 attacks, named clips travel 0      all clear
      storm atronach      3 attacks, named clips travel 0      all flagged

      wisp                4 attacks, floats, travel 0          all clear
      witchlight          1 attack,  floats, travel 0          flagged

  **What the six have in common was looked for in all three places a project is described,
  and it is not there.**

  *The races* (`RACE`, as the masters have them). The six span everything: unarmed reach 0
  (one of the witchlight's races) to 256 (the netch, the player's two-handed entries),
  unarmed damage 0 to 70, sizes small to large. No race flag is shared by all six -- the
  only ones unique to them, `Playable`, `FaceGenHead`, `Child`, `OverlayHeadPartList`, are
  the player's humanoid races and say nothing about the werewolf or the netch -- and no
  attack-data flag either: `PowerAttack`, `LeftAttack`, `BashAttack` and `RotatingAttack`
  occur on both sides. The attack and strike angles do not separate them.

  *The character files*. Nothing is 6 of 6 against 0 of 43. Foot IK is driven on 2 of the
  six and 14 of the rest; 3 of the six declare character properties and 22 of the rest do.
  The one property that looked like an answer, **`bAnimationDrivenAttacks`, is on 12
  projects and none of the six** -- but those 12 are the quadrupeds, who share
  `quadrupedbehavior.hkx` and its whole property set (`IsBear`, `IsCow`, `IsSabreCat`), so
  it is a family trait and not a discriminator.

  *The behaviours*. Of 639 distinct variables, 3,741 events, 99 character properties and 89
  node classes across the 49 projects, **not one is present in all six and absent from all
  43**, nor the reverse -- and not even loosely: allowing four exceptions on either side
  still finds nothing.

  Editing marks do not separate them either. The share of nodes still carrying the
  behaviour tool's own name (`Behavior16`, `ModifierGenerator07`) averages 3.9% over the six
  and 3.6% over the rest; the hare, the mudcrab and the slaughterfish lead the table and the
  witchlight is at zero. The werewolf is the one that looks heavily revised -- 214 of its
  426 named nodes end in digits -- and it is also the creature with the most flags.

  One thing holds without exception, though it decides nothing on its own. **No flagged
  attack's clips travel a middling distance.** The 34 flagged events sit at exactly 0 (19 of
  them) or at 300.4 and above (15: the sprint and running attacks, up to 625), and not one
  lands in between, where 98 clear attacks do. The longest travel in the file belongs to a
  clear attack, the wolf's and the dog's `attackStart_SkeeverLungeLong` at 670.7.

      named clips' travel     flagged   clear
      exactly 0                    19     164
      0 < travel < 300              0      98
      travel >= 300.4              15      27

  So `travel == 0 or travel >= 300` is **necessary** for the flag and nowhere near
  sufficient: it admits 191 clear attacks. Paired with `bAnimationDriven` it gets no better
  -- `not A and not R and travel <= 5` mislabels 103 of the 323 attacks where assuming 0
  mislabels 34.

  The gap is a **decision and not an absence**, which is worth separating out. Inside the six
  projects that use the flag there are 71 attacks, and they fall like this:

      named clips' travel     flagged   clear
      exactly 0                    19      14
      0 < travel < 300              0      20
      travel >= 300                15       3

  Those 20 mid-travel attacks are the player's directional power attacks, 58.3 to 297.1 units
  -- `attackPowerStartDualWield`, `attackPowerStartBackward`, `attackPowerStartLeft`, the two
  hand-to-hand forwards and the rest. The flag was there to use on every one of them and was
  used on none, so the empty band is a choice.

  Outside that band travel decides nothing. Large travel is flagged 15 times of 18, the
  exceptions being the player's `attackPowerStartForward` at 484.5 and the netch's
  `attackStartPowerStanding` at 443.4. Zero travel is flagged 19 times of 33; the 14 that are
  clear are `attackPowerStartInPlace` and `attackPowerStartInPlaceLeftHand`,
  `attackStartDualWield`, `bashStart` and `bashPowerStart` for both sexes, and the werewolf's
  `AttackStartLeftPower`, `AttackStartRightPower`, `AttackStartLeftSprinting` and
  `AttackStartRightSprinting`. In that band the clear ones are the power attacks and the
  bashes and the flagged ones are the ordinary attacks, which is the name-shape correlation
  below arriving from another direction rather than a rule about travel.

  **What the flagged attacks are, in three families.** The blend above an attack is not a
  static fact about mixing -- a blender's arms are weighted at runtime, and the ones over the
  player's and the werewolf's attacks are **parametric on the actor's speed**, so whether the
  locomotion arm contributes is `SpeedDamped` or `SampledSpeed` at that instant:

      1HM_Forward_AttackLeft_Blend   blendParameter <- SpeedDamped
         axis  25  1HM_AttackLeft          the standing swing, travels nothing
         axis  82  1HM_WalkFwdAttackLeft
         axis 232  1HM_RunFwdAttackLeft    travels a long way
      LeftAttackForwardBlend (werewolf)   blendParameter <- SampledSpeed
         axis  50  LeftAttackStandingBehavior
         axis 325  LeftAttackRunningDirectionalBlend   (itself parametric on Direction)

  The axis positions are speeds in units per second. So **the attack has no single root
  motion**: the clip that plays is interpolated by how fast the actor is going, from a
  standing swing that travels nothing to a run attack that travels far. The set data has one
  `reach` per attack entry and there is no right number to put in it, which is what the flag
  is for -- it tells combat to compute the distance from the same speed the graph is blending
  by.

  Tested as a rule, "an ancestor blender whose `blendParameter` is bound to a variable named
  for speed" fires on **14 attacks in the whole game and 12 of them are flagged**. It needs no
  depth limit -- the only depths that occur are 2 and 4 -- and inside the six projects it is
  never wrong. The two exceptions are the **Vampire Lord's** `AttackStartLeft` and
  `AttackStartRight`, built on the same speed-parametric pattern in a project that flags none
  of its four attacks: the graph was templated and the set data was never tuned.

  **The rule's exceptions were examined and nothing is missing from it.** The two it invents
  are the Vampire Lord's `AttackStartLeft` and `AttackStartRight`, and their blends are the same
  pattern to the letter -- `Forward_BlendAttackLeft`, parametric on `SampledSpeed`, the standing
  attack at axis 5 travelling nothing and `MT RunForwardAttackLeft` at axis 303 travelling 444.
  No condition separates them from the player's; vanilla flagged none of that project's four
  attacks, and the cost is real, since its entry names `MT AttackLeft` and combat therefore
  measures a vampire lord as standing still while it swings in flight.

  Of the ones it misses, **14 have nothing in the graph that chooses their clip at all** -- a
  single clip in a state: the sprint power attacks, the werewolf's side and running-power
  attacks and its dual sprint, the storm atronach's two power attacks, the netch's and the
  witchlight's. The storm atronach's swipe sits under a blend with `CombatIdle` whose parameter
  is constant. And the sprint attacks are chosen by a **selector bound to the weapon**:
  `AttackForwardSprintMSG`, `selectedGeneratorIndex <- iRightHandType`, over clips travelling
  333, 333, 333, 333, 333, 494 and 494 -- the same "no single reach" situation in a different
  node. Adding that as a second rule was measured and **rejected: it catches 2 flagged attacks
  and 8 clear ones**, because the player's `attackPowerStartBackward`, `attackPowerStartLeft`,
  `attackPowerStartRight` and `attackPowerStartDualWield` have exactly that shape and are clear.

  That accounts for 12 of the 34 flags. The rest fall into two more families, and every flag
  in the file belongs to exactly one:

      family                                                          flagged  shown by
      the clip is chosen by a speed-parametric blend                       12  the behaviour
the graph says the character is sprinting over that state:
        single-direction locomotion, so there is no blend, but the
        actor is carried at sprint speed anyway                           16  the behaviour
      the creature is simply always moving -- the storm atronach's
        three and the witchlight's one, whose projects flag every
        attack they own; the werewolf's side and running-power
        attacks; the player's bashStart                                   10  nothing

  All three say the same thing in different vocabularies: this attack's travel is the actor's,
  not this clip's. **The first two are decidable and are derived** (§5.7); only the third is
  not, and it is what `--flags-from` is for. The second was found by asking why the sprint
  attacks have no blend: sprinting has one direction, so there is nothing to interpolate --
  the graph states the condition in a variable instead, `IsSprinting` where the player and
  the werewolf read it and `iSyncSprintState` near the clip where the netch does.

  The third family was examined for a condition of the same kind and has none. The storm
  atronach and the witchlight **flag every attack they own** -- 3 of 3 and 1 of 1 -- so there is
  no clear attack to contrast with and nothing per-attack to find; what those projects record is
  a fact about the creature. The werewolf's `AttackStartLeftSide`, `AttackStartRightSide` and its
  two running-power attacks touch `IsAttacking` and nothing else, exactly as its clear power
  combos do, and every werewolf attack -- flagged or clear -- is entered by an unconditioned
  wildcard in `Behavior16`. The side attacks match the clear combos in transition flags
  (`0x0d00`) and effect (`QuarterSecondBlend`, 0.25 s) as well. The running powers differ in
  three ways and none generalises: `0x0200` in the flag word, Havok's permission to re-enter from
  any state rather than anything about movement; a shorter `PowerAttackBlend`; and the race's
  attack type, **`PowerAttackTypeForward`** where the combos have `PowerAttackTypeStanding`.
  That keyword is the engine's own "chosen while moving forward", but the player's
  `attackPowerStartForward` family and the bear's, scrib's and vampire brute's forward powers
  carry it too and are clear -- 6 flagged against 10. The side attacks' ±45 attack angle is
  shared with some 40 clear attacks. No transition syncs an attack to locomotion: every
  transition effect has flags 0. The player's `bashStart` touches `iIsInSneak`, `iLeftHandType` and `iRightHandType`,
  the same three as the clear `bashPowerStart`, and vanilla flags it **only in the hand-to-hand
  sets** -- a per-set value, which no per-attack rule can express.

  **The best rule found, and why it is still not one.** Read the flag as "this attack's reach
  is the locomotion's, not the animation's" and two things stand for it: the attack is blended
  into locomotion, or its clip has the locomotion baked into its own travel. Scored inside the
  six projects that use the flag, with a blender (`hkbBlenderGenerator`, over every parent) no
  more than two levels above the clip:

      rule                                                   caught   false   wrong of 71
      assume clear                                                0       0            34
      any blender <= 2 levels, or travel >= 300                  25       3            12
      locomotion-blender <= 5..10 levels, or travel >= 300       21       3            16
      locomotion-blender at any depth, or travel >= 300          27      33            40
      any blender <= 2 levels, or clip speed ~ locomotion        12       0            22

  Over all 49 projects the first is 45 wrong against 34 for assuming clear, since the other 43
  never use the flag -- so it describes those six and generalises to nothing.

  **The depth limit is doing real work, and only the second row's is untuned.** A blender's
  distance above the clip ought not to matter, and it stops mattering once the blender is
  required to have another arm holding a movement tree -- four or more travelling clips. The
  nearest such blender, inside the six, sits at depth 2, 4 or 5 for six attacks, all flagged;
  at 11 and beyond for fifty, eighteen flagged and thirty-two clear; and **nowhere between 6
  and 10**. The far ones are the graph-wide blends every attack passes through, the player's
  `PlayerStaggerStandingBlend` and `PlayerStaggerMovingBlend` at 12 and 13, which is why
  dropping the limit costs 28 more errors. Any cutoff in that hole gives the identical 16, so
  that rule has no fitted parameter. The 12-error row buys its four fewer errors with a limit
  of 2 that cannot be moved: at 4 it costs five more.

  Its three false positives are the lunges whose travel is their own: the player's
  `attackPowerStartForward` at 484.5 and the netch's `attackStartPowerStanding` at 443.4.
  Telling a lunge from baked-in locomotion should be what the clip's *speed* settles, and the
  third row is that attempt -- **inconclusive, not refuted**, because the denominator used
  there, the fastest non-attack clip, is 3,529 u/s for the player and plainly not a locomotion
  speed. The raw figures do separate: the player's flagged sprint attacks run at 370 to 417
  u/s and every clear player attack at 323 or below. Doing it properly wants the run and
  sprint speeds from the `MOVT` records, which `docs/speed-data.md` already reads.

  Its nine false negatives are the cases where the character is moving for a reason the graph
  does not state: the werewolf's `AttackStartLeft`, `AttackStartRight` and `AttackStartBackHand`
  (blended, but four levels up), its `LeftSide`, `RightSide` and `DualSprinting`, the netch's
  `attackStartLeft`, the witchlight's `attackStart_Attack1` and the storm atronach's
  `attackPowerStart_StandingAttack`. The netch drifts and the witchlight hovers always: there
  is no blend and no travel to find, because their motion is not in the animation at all.

  One correlation is worth recording because it is the strongest there is, and because it
  shows what kind of thing the flag is. The **shape of the event's name** tracks it, inside
  the six projects almost exactly:

      prefix                          flagged   clear     all 49 projects
      attackPowerStart_ (underscore)       10       0      10 / 27
      bashStart                             2       0       2 / 15
      attackStart   (no underscore)        24       8      24 / 118
      attackStart_  (underscore)             2       8       2 / 149
      attackPowerStart (no underscore)      0      26       0 / 28
      bashPowerStart                        0       2       0 / 4

  The player is the cleanest: `attackPowerStart_` 4 of 4 flagged and `attackPowerStart` 0 of
  13, `attackStart` 6 of 7 and `attackStart_` 0 of 4. Note that the underscore means opposite
  things on either side of `Power`, which is the tell. As a predictor the shape gets 10 of 82
  wrong inside those six projects, against 38 for assuming 0 -- a real gain -- and 126 of 341
  wrong over the whole file, because creatures name their attacks `attackStart_Something` and
  147 of those 149 are clear. It cannot be a mechanism either: the name is an event declared
  in a behaviour and the flag is a byte in a text file, with nothing between them but whoever
  wrote both. What it records is which events the player's animators revisited, and that pass
  is where the flags came from.

  Every rule tried, and what it scored against the 38 flagged events:

  | tried | why it fails |
  | --- | --- |
  | the clip's `hkbClipGenerator` mirror bit | 122 flagged with no mirrored clip, 19 clear with every clip mirrored — the name "mirrored" came from HKSK's first reader and was never checked |
  | the race's `ATKD` flags on the matching attack | flagged and clear attacks share every combination of them |
  | `bAnimationDriven` while the attack plays | it is 0 for 33 of the 34 flagged attacks and for 258 of the 289 clear ones, so it agrees with the flag's meaning and separates nothing. The one flagged attack that plays animation-driven is the storm atronach's `attackPowerStart_StandingAttack`, inside `StandingPowerAttackBehavior`, whose `isActive` is bound to the variable. Within the player's project nothing raises it for any attack at all. The variable starts at 0 in all 49 projects, is written only by the graph (`BSIsActiveModifier`, a machine's `isActive`) and is **read** by the engine, which turns it into `StartMotionDriven` / `StartAnimationDriven` / `StartAllowRotation` (`docs/reverse-engineering.md` §4). The 31 clear attacks that do play animation-driven split in two: 10 carry real root motion -- the sphere centurion's `attackStartForwardPowerChop` travels 192, the troll's `bashStart` 181, the spriggan's forward power attacks 134 -- where a clear flag is exactly right, and **21 travel nothing at all**, so they are measured at a reach of zero and still carry 0. The sphere centurion's `attackStartChop` and the storm atronach's `attackPowerStart_StandingAttack` are both animation-driven with a clip that does not move; one is clear and the other is the single flagged attack in the file that plays animation-driven. `bAllowRotation` was checked with it, in every combination: the two are never raised over the same clip, and of the eleven booleans of the pair the best score belongs to `A and R`, which is never true -- the constant "always clear". Every non-trivial combination predicts the flag worse than guessing 0 everywhere. The engine's own "the controller carries me" condition, `not A and not R`, holds for 28 of the 34 flagged attacks and for 169 clear ones |
  | the attack blended into locomotion — a state parallel to the movement machine, a partial-bone or layered generator, a bone switch | separates the werewolf's running attacks and nothing else; the player's and the floating creatures' flagged attacks are ordinary states |
  | a travelling clip in the innermost state the event reaches | 28 of 38 flagged events have one — and so do 108 of the 293 clear ones, because a lunge's single clip is exactly the case the flag is *not* for |
  | that, and more than one clip, for a state holding movement variants | worse: 15 of 38, against 27 clear. The multi-clip states it finds are the player's directional power attacks, whose several clips are weapon variants, not movement ones |
  | the clip's own `hkbClipGenerator` flags and mode | no bit separates them: `IGNORE_MOTION` (32) appears on no attack clip at all, and `MIRROR` (4) only on 28 clear ones |
  | a blender above the named clip — `hkbBlenderGenerator` and `hkbPoseMatchingGenerator` under it, by the Havok hierarchy — over every parent | **the player alone is clean.** Its four non-sprint flagged events sit at depth 2 under directional blends (`1HM_Forward_AttackLeft_Blend`, `2HW_Backward_AttackRightNPC_Blend`, `H2H_Right_AttackLeft_Blend`) and no clear player event has a blender within 8. Everywhere else it breaks: 9 of the 34 flagged events have **no** blender above them at all — the werewolf's side and running-power attacks, its dual sprint, the netch, the witchlight, two of the storm atronach's — while 72 of 289 clear events have one, 9 of them at depth 2 (dwarven spider centurion, vampire lord). The player's own sprint attacks reach their first blender at depth 12, the `PlayerStagger*Blend` that everything in that graph reaches |
  | the variables the clip's path writes (`bAllowRotation`, `bAnimationDriven`, `IsSprinting`, `Direction`, `SampledSpeed`) | `bAllowRotation` is written by the player's flagged sprint attacks *and* by every clear power attack. Nothing else reaches both halves |

  What §4.6 shows is that the flag means what its name says, wherever it was used. What no
  rule can give is a predicate, because the flag is not a function of the assets: the same
  bear that charges and bites carries 0, and the werewolf contradicts itself outright --
  `AttackStartDualSprinting` is flagged and `AttackStartLeftSprinting` is not, with the same
  shape and sibling clips. Six projects were tuned and the rest were left. `setgen
  --flags-from` copies it rather than deriving it, and an attack outside those six gets what
  the whole of vanilla outside those six gets, which is 0.

In the executable:

- **what `0x140bcab30`'s extra argument carries.** Its callers are one of the two equip
  entries, the equip path's empty-key request, idle selection and one save-game restore;
  the other equip entry, action handling and movement call `0x140bca970`.
- **what index 0x16e resolves to** in the queued 3D load (§4.3), and who reaches
  `0x1407c58f0` and `0x1407c5e00`.
- **which field the movement controller's records hold at +0x58.** The records come from
  the idle manager; that the field is the idle's event is likely, not shown.
- **what the global list of `BSResource::ID`s at `0x14315c938` is.** It is an array of the
  same 12-byte records the checksums parse into, created and destroyed with the singleton
  (`0x140090110`, `0x141718c10`). The loader searches it (`0x14053b3c6`–`0x14053b426`) and
  the per-request collector reserves room for all of it on top of the set's own
  (`0x14053ce58`), so every request carries it. Neither function fills it; what does was
  not found.

In game, the test that decides §4.5: remove one travelling clip's checksum from a creature's
set, and see whether that clip plays late, plays after the first time, or never plays. The
same test is the one for the rebuilt file: it is complete by §4.4's reading and untested in
play.
