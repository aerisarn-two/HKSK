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

## Checked against the behaviour graphs too

The character file and the behaviour files are independent witnesses, so the
cache is checked against both. Against the behaviours, across all 10,556 clips
that have a generator:

| What the cache restates | Agreement |
| --- | --- |
| playback speed | 10,556 / 10,556 |
| crop start and end | 10,556 / 10,556 |
| every generator is cached | 10,550 / 10,550 |
| event list | ~92% reproduce exactly |

Speed and crop are copied verbatim, so `ConsistencyReport` treats any difference
as drift worth reporting. Nothing goes missing in the direction that would
matter — every generator the behaviours define is in the cache. The 41 cached
clips with no generator are clips from graphs shared with other projects.

`m_animationBindingIndex` is `-1` on every generator in the game, which rules out
the obvious alternative explanation for where the cache index comes from.

### The event list is derived, not copied

A clip's events come from two places at once — the animation's annotation track
and the generator's triggers — merged in time order:

- annotation times are kept as-is but **clamped to the clip's playing length**,
  `(duration - crops) / playbackSpeed`. The bear's walk has a `FootBack`
  annotation at 1.4 in an animation that, at speed 1.5, finishes at 1.1111 — and
  the cache says 1.1111.
- a trigger marked relative to the end of the clip lands at that same playing
  length plus its local time (the chicken's `clipEnd` at `-0.009` becomes
  6.65767).
- an annotation's text is stored as **the longest prefix that names a behaviour
  event**. The chicken keeps `SoundPlay.NPCChickenScratch` in full because that
  is an event of its graph; the atronach's `SoundPlay.NPCAtronachFrostAttack` is
  not, so it is stored as `SoundPlay`.

Those rules reproduce 92% of the game's clips exactly. The remainder needs finer
rules still, and some of it is drift. So event lists, like cache indices, are
**preserved rather than regenerated** — the library will not overwrite generated
data it cannot reproduce.

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

## FBX, with events and root motion

[HKFBX](https://github.com/aerisarn-two/HKFBX) converts a single animation
between hkx and FBX. It deliberately does not know where root motion comes from —
Havok keeps it apart from the skeleton, and `SampledAnimation.RootMotion` is
supplied by the caller. In Skyrim it comes from the cache, which is what this
library knows, so the two halves fit:

```csharp
var exchange = new AnimationExchange();

// Out, carrying the cache's root motion and the animation's events.
exchange.ExportAll(chicken, @"out\chicken");

// ...and back, as new animations with clips over them.
var results = exchange.ImportAll(chicken, Directory.GetFiles(@"out\chicken", "*.fbx"),
    path => new ImportOptions
    {
        StoredName = $@"Animations\{Path.GetFileNameWithoutExtension(path)}.hkx",
        ClipName   = Path.GetFileNameWithoutExtension(path),
    });

chicken.SaveCharacter();   // the animation list lives in the character .hkx
cache.Save();              // the clips and root motion live in the cache
```

Both saves are needed, and they are separate files. `AddAnimation` changes the
character packfile's animation list; the clips and motion blocks are in the
cache. Saving one without the other leaves the cache pointing at slots the
character file does not have, so `ConsistencyReport` reports an
`unsaved-animation-list` error while that is true.

Importing **replaces** when the stored name matches an existing animation, which
keeps the slot and therefore every clip and motion block already pointing at it.
Otherwise it **appends**, for the same reason `AddAnimation` does.

Batches report per file rather than throwing, so eighty imports with three bad
files still import seventy-seven and name the three. `StoredName` and `ClipName`
describe one animation, so passing them to a batch is refused rather than applied
to an arbitrary member of it — use the overload that takes a function.

### Where root motion lives

**In an animation, the root bone does not move.** Of 1,200 animations sampled
from the game, 1,196 carry no extracted motion at all, and the root track of a
run that travels 251 units sits at the origin for every frame. The travel is in
the cache, and the game applies it.

FBX has nowhere to put that, so exporting drives the root bone with the cache's
motion — an animator has to see the travel. Importing therefore finds it twice,
once as the root's animation and once as root motion, so the exchange takes it
back off the root before compressing. The imported animation looks like a
Skyrim one: root at the origin, travel in the cache, counted once.

`ImportRootMotion` controls whether the **cache** is updated. It does not decide
whether the animation is left carrying motion it should not have — that is
always removed. A reference frame inherited from the template animation is
dropped for the same reason.

Two more things worth knowing:

- **Events default to the animation's own annotation track**, which is what
  round trips. `EventSource.CachedClip` exports what the game actually fires —
  annotations *merged with the behaviour's triggers* — which is useful to look
  at but must not be imported back, or the triggers become annotations and fire
  twice.
- **Re-importing densifies root motion.** FBX stores per-frame curves, so a
  motion block that went out as one endpoint key comes back with one key per
  frame. The displacement is unchanged — a chicken turn of exactly π/2 comes back
  as π/2 — but the file grows.

Conversion needs `mopper.exe`, because Havok's spline *encoder* is proprietary
and this is the only credible implementation of it. It is a Win32 binary and runs
under Wine off Windows.

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
