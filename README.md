# HKSK

Reads and edits Skyrim's Havok animation projects as one thing.

A project's definition is scattered across files that restate each other — some
Havok packfiles, some plain text, none of them self-describing — and nothing in
them says how they line up. HKSK gathers them, exposes them as a single model,
and keeps them consistent across an edit. It uses
[HKX2](https://github.com/aerisarn-two/HKX2Library) for the Havok side.

```csharp
var cache = SkyrimCache.Load(@"Data\meshes");
var chicken = cache.Open("ChickenProject");

foreach (var clip in chicken.Clips)
    Console.WriteLine($"{clip.Name} plays {clip.Slot?.StoredName}, travels {clip.Slot?.Motion?.Travel}");

var slot = chicken.AddAnimation(@"Animations\NewIdle.hkx");
chicken.AddClip("NewIdle", slot);
chicken.SetRootMotion(slot, motion);

cache.Save();
```

## What a project is made of

The **Havok side** is authored in the Creation Kit:

| File | Holds |
| --- | --- |
| `<name>project.hkx` | the character files — and in Skyrim, nothing else |
| `characters/<name>.hkx` | the skeleton, the behaviour graph, and the animation list |
| `behaviors/*.hkx` | clip generators: name, animation, speed, crop, triggers |

The **cache side** is generated, and is what the game actually reads:

| File | Holds |
| --- | --- |
| `animationdatasinglefile.txt` | every project's clips, and its root motion |
| `animationsetdatasinglefile.txt` | each creature's attack and idle sets |
| `animationdata/`, `animationsetdata/` | the same content, split per project |

The cache exists so the game can answer "how far does this animation travel?"
and "what events does this clip fire?" without loading the animation. That
duplication is the whole difficulty: the two sides can disagree, and the game
believes the cache.

## The rule everything rests on

> **A clip's cache index is the position of its animation in the character
> file's animation list.**

Nothing states this in either file. It is what ties a clip to its root motion,
and it is why editing by hand goes wrong.

Two consequences:

- **Root motion belongs to an animation, not a clip.** The chicken's
  `Forward_Walk` and `Forward_WalkSlow` share one `WalkForward.HKX` and one
  motion block.
- **Inserting an animation renumbers the cache.** Every clip and every motion
  block above the insertion point silently repoints one animation off.
  `AddAnimation` therefore appends, and `RemoveAnimation` renumbers clips and
  motion blocks in the same step.

The numbering is positional, not a dense sequence: the chaurus lists 42
animations and uses 40 indices, leaving 27 and 29 unused because no clip plays
its two sleep animations.

This was derived from the shipped game rather than from documentation, and is
checked against it. Of the 49 projects that carry a cache, 44 agree on every one
of their clips; across all of them the rule holds for 10,318 of 10,556 clips.
The five that disagree are Bethesda's own drift — the horse and the werewolf are
numbered against animation lists longer than the ones their character files now
carry, at a constant offset — which is why `ConsistencyReport` separates
`Drift` from `Error`, and why the numbering is preserved rather than
recomputed. Recomputing it would "fix" those five into disagreeing with the game.

## The split files shipped with the game are stale

`animationdatasinglefile.txt` is the source of truth. The per-project files under
`animationdata/` are a pre-DLC snapshot and **must not be merged as they stand**:

- the listing names 328 projects where the merged file carries 429 — the 96
  Dawnguard and Dragonborn creatures are missing outright
- nine more, including the dragon, draugr, falmer, horse and both player
  characters, carry fewer clips and indices numbered against older animation lists
- `ShoutImod.txt` is listed with no file behind it

318 of the 329 that are present are byte-identical to their merged counterpart,
so the staleness is confined — but merging the shipped copy would roll the game
back.

`SplitCache` still handles the split form as a first-class representation, so it
can be written out, edited or authored fresh, and merged back:

```csharp
cache.Split(@"out");                                  // merged -> split
var rebuilt = SkyrimCache.FromSplit(@"out", out var issues);
```

Order follows the two `dirlist.txt` files, which is the only record of it, and
`issues` reports anything unaccounted for rather than dropping it quietly.
Splitting the shipped cache and rebuilding it reproduces both merged files
byte-for-byte.

## Fidelity

Reading and writing are byte-exact: loading and saving with no edits reproduces
Bethesda's own bytes, which the test suite checks against the shipped files. That
matters because a save rewrites the whole merged file, and anything less would
churn thousands of unrelated lines.

Getting there needs three details the format never states:

- **Floats carry six significant digits**, with three-digit lowercase exponents —
  MSVC's `printf`, so `2.39327e-006` rather than .NET's `2.39327E-06`. Checked
  against all 11,517 distinct numeric tokens in the shipped merged file.
- **Lines end CRLF**, including the last.
- **Block line counts** are recomputed from the content actually written, not
  from a formula that could drift from it.

The animation set data names animation files by a CRC-32 that is *not* the common
one — polynomial `0x04C11DB7`, reflected in and out, but with no initial value
and no final xor, over the lowercased text. Each animation contributes its folder
checksum, its name checksum, and the constant `7891816`, which is `0x786B68`:
the bytes of `hkx` reversed.

## Tests

The suite runs without game data; the tests that need it skip.

```bash
dotnet test
HKSK_CORPUS=~/path/to/extracted/meshes dotnet test   # includes the corpus tests
```

## Installing

Published to GitHub Packages. Add the feed and a PAT with `read:packages`:

```xml
<packageSources>
  <add key="github" value="https://nuget.pkg.github.com/aerisarn-two/index.json" />
</packageSources>
```

```bash
dotnet add package HKSK
```

## Licence

GPL-3.0. The cache format was worked out with reference to
[ck-cmd](https://github.com/aerisarn/ck-cmd), which is GPL-3.0.
