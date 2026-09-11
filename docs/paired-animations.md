# Paired animations

A paired animation drives **two skeletons from one file**. Skyrim's killmoves,
mounts and executions are all of them, and HKSK currently exports every one of
them wrongly — silently, which is worse than failing.

Everything below was measured against the shipped game: 302 animations that a
project lists or that live under `SharedKillMoves`, read through HKX2.

## What is in the file

One skeleton, named `PairedRoot`, holding both actors:

```
PairedRoot                      the shared root, track 0
├── NPC                         actor 1's subtree root
│   ├── NPC Root [Root]
│   └── ... 99 bones for a human
└── 2_                          actor 2's subtree root
    ├── 2_NPC Root [Root]
    └── ... every bone of actor 2, each prefixed
```

The binding says so outright: `hkaAnimationBinding.m_originalSkeletonName` is
`PairedRoot`, and `m_transformTrackToBoneIndices` is empty, so tracks map to
bones one for one. The bone names are in the animation's annotation tracks, one
per track, which is the only place they exist at all.

**296 of the 302 carry a `2_` half.** The other six are single-skeleton
animations that happen to be named `Paired_*` — of which more below.

### The first-person layout is upside down

Three of the game's own do not look like that at all. A first-person killmove is
rooted at the **viewer**, and `PairedRoot` hangs inside the tree rather than at
the top of it:

```
NPC                             track 0, and what the binding names
├── NPC Root [Root] …           the arms, the camera, 82 bones
└── PairedRoot                  track 83
    └── 2_ …                    the victim
```

Two of them bind to `NPC` rather than `PairedRoot`, so **the binding alone is not
a reliable test** — trusting it hands those back as ordinary animations, which is
the bug this exists to remove. What catches them is that an animation carrying
tracks its own skeleton has no bones for is not rigged to that skeleton either:
the sabrecat one has 149 tracks against a 99-bone rig.

A third places the partner's root bone with no `2_` subtree root above it at all,
which is why that bone is treated as optional rather than indexed blindly.

**The prefix is always `2_`, and only ever `2_`**: 23,824 prefixed tracks across
the game, not one of them `3_`. A paired animation is two actors, never three.

**No `PairedRoot` skeleton file ships.** The string appears in the paired
animations and nowhere else in the game's 7,699 Havok files. The combined
skeleton is implied by the animation and reconstructed by the engine; there is
nothing to load it from, which is exactly why exporting one needs the two halves
to be rebuilt from somewhere else.

## How the game reaches one

Not through a file path anywhere in Havok, and not from a record: **no IDLE
record in any master names a paired animation** — all 4,343 were checked.

They come from the animation cache. A paired animation is listed in the
character file of **each** participant, and 164 of the 283 listed files are named
by two projects or more:

```
..\SharedKillMoves\Human&Bear\Paired_1HMKillMoveBearA.hkx
      <- BearProject, DefaultMale, DefaultFemale
```

So the file list is what says who is in it. The path is relative to the project
folder, and `..\SharedKillMoves\<A>&<B>\` reaches sideways out of one actor's
folder into the shared one. A human-on-human killmove has no partner project and
lives in the human's own `Animations\` folder instead.

## Which half is which

Not from the folder name. `SharedKillMoves\Human&Falmer\` holds an animation
whose unprefixed half matches the **draugr** rig at 89%, because draugr, falmer
and humans share the `NPC *` naming and the folder is only a filing convention.

Matching bone sets against the rigs is what works. Of the 296 combined animations, 278 resolve to a pair of projects with both
halves matching above 80%, across 46 distinct pairings:

| Pairing | Files |
| --- | ---: |
| DefaultFemale + DefaultFemale | 56 |
| FirstPerson + DefaultFemale | 50 |
| DefaultFemale + DragonProject | 9 |
| …and 43 more pairings | |

The first-person rig appearing as actor 1 in 50 of them is worth noting: those
are the killmoves seen down the player's own arms.

The 18 that do not resolve cleanly are all cases where **the `2_` half is a
superset of the partner's Havok rig**. The draugr's is 129 bones against a
84-bone rig, and the 46 extras are `NPC LPlatform`, `NPC LIKTarget`,
`NPC RPlatform` and their like — nodes that live in a skeleton **mesh** and never
in the Havok rig. The rig is not the full skeleton, and for these the mesh is
what the animation was authored against.

## What HKSK does with one today

`AnimationExchange.Export` rigs every animation to `project.SkeletonPath`, which
is one skeleton. Exporting `Paired_1HMKillMoveBearA` from `BearProject` produces
an FBX that:

- carries **76 bones** — the bear's, and only the bear's;
- contains **no `2_` bone at all**, so the human half is gone;
- maps 177 tracks onto 76 bones by position, so the bear's bones are driven by
  tracks belonging to `PairedRoot`, `NPC` and the human's first 74 bones.

It does not throw. It writes a file that looks like an export and is a bear
playing a human's motion with three quarters of the animation discarded.

## How it is handled

The combined skeleton is rebuilt, because nothing ships it. All of it happens
here, in Havok: the rig a paired animation is rigged to is the one in the Havok
project, and a skeleton mesh is somebody else's business.

**The animation decides the bone order, and the hierarchy.** Its annotation tracks
name every bone in the order the binding expects, so that order is the bone order
and track-to-bone is the identity. The order is per-file and not tree order:
`Human&Draugr` lists the partner first, `Human&Dragon` second, and in the bear
killmove the FBX's own depth-first order parts company with it at bone 13. Track 0
is the tree root, which reproduces the first-person layout as faithfully as the
ordinary one — and a hierarchy invented instead would move every bone, because the
transforms are local to their parent.

**Detection is two cheap signals, not one.** The binding usually says `PairedRoot`
outright; where it does not, an animation whose track count does not match the
project's rig is not rigged to that rig, and its names are read to find out why.
Neither costs a second parse of an animation that turns out to be ordinary.

**The rigs supply what the animation does not carry** — the hierarchy and the rest
pose. `PairedRig.Build` takes the pairing and up to two rigs, and every track gets
a bone whether a rig accounts for it or not: exporting a killmove from the bear
when nobody has said who the other actor is still has to produce all 177 tracks,
so an unaccounted bone is created under its own half's root at rest. That is not
only a mod's problem — 18 of the game's own pairings name more bones on one side
than that actor's rig declares, because a rig is not the whole skeleton.

**Which half a project is, is matched rather than assumed.** The folder is a
filing convention: `SharedKillMoves\Human&Falmer` holds one whose unprefixed half
is the draugr's. `PairedRig.DriverIs` compares the rig against both halves, and
the answer is never close -- a rig covers one half almost entirely and the other
by a percent or two.

```csharp
var cache = SkyrimCache.Load(@"Data\meshes");
var bear = cache.OpenActor("BearProject")!;
var human = cache.OpenActor("DefaultMale")!;
var exchange = new AnimationExchange();

// Out. Both skeletons, in one FBX, with the partner's rest pose where it is known.
exchange.Export(bear, slot, "killmove.fbx", new ExportOptions { Partner = human });

// ...and back, into every project that plays it.
exchange.ImportPaired(cache, "killmove.fbx", animationPath, [bear, human]);
```

Import reorders the FBX's tracks into the binding's order before compressing, and
refuses a rig whose bones are not the ones the binding already names: a packfile
carries its binding from the template rather than rebuilding it, so a bone added
in Blender has no track to live in. It also refuses an FBX with no `2_` bone,
which is an ordinary animation and should be imported as one.

Consistency across the projects is `SkyrimCache.RegisterPaired`, which gives every
participant a slot — each storing the path its own way, relative to its own folder
— and optionally a clip, and keeps a slot a project already has rather than
renumbering its cache. Root motion goes to every participant, because the cache
keeps a movement block per project and all 164 shared pairings in the game record
the same travel in each.

## What is checked

`PairingReport` covers what spans projects, which `ConsistencyReport` cannot see
from inside one. Against the shipped game it reports exactly one finding — a
listed animation that holds no animation at all.

| Rule | Holds in vanilla |
| --- | --- |
| A project that lists a pairing has a clip over it | 294 / 294 |
| A combined animation has both subtree roots | 294 / 294 |
| A participant's rig accounts for one of the halves | 294 / 294 |

The rule that is **not** checked is the one that looks obvious: that both actors
list the file. 139 of the 294 are listed by a single project — nearly all the
first-person killmoves, whose partner half is applied to an actor that never names
the file — so an unshared pairing is normal and reporting it would bury the
findings that matter under 139 that do not.

## The six that are not combined

`Paired_OffsetBoundStandingCut.hkx` has 99 tracks, no `2_` half, and a partner
file beside it called `Paired_OffsetBoundStandingCutNPC.hkx`. That is the other
convention: two ordinary single-skeleton animations, one per actor, kept in step
by the behaviour graph rather than by sharing a skeleton.

They need nothing new to export — each half is an ordinary animation — but they
do need to be recognised, or a tool that assumes `Paired_*` means a `2_` half
will look for one that is not there.
