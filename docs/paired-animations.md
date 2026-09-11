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

## What exporting one properly needs

The combined skeleton has to be rebuilt, because nothing ships it:

1. **Identify the halves** — split the track names on the `2_` prefix. The two
   subtree roots are `NPC` and `2_`, and `PairedRoot` is track 0.
2. **Identify the participants** — the projects whose file lists name this
   animation. Where that is ambiguous (a human-on-human file is listed by one
   project and used for both halves), fall back to matching each half's bone set
   against the rigs.
3. **Rebuild the skeleton** — from the participants' **skeleton meshes** rather
   than their Havok rigs, because the meshes carry the nodes the rigs lack. Parent
   actor 1's tree under `NPC` and actor 2's under `2_`, prefix every bone of the
   second, and put both under a `PairedRoot` node.
4. **Bind** — the rebuilt skeleton's bone order has to match the animation's
   track order, which is per-file and not alphabetical: in `Human&Draugr` the
   `2_` half comes first, in `Human&Dragon` it comes second. Build the order from
   the annotation track names, not from the two skeletons.

Step 3 is the one that reaches outside this library: a skeleton mesh is a NIF, and
HKSK does not read NIFs. The split that keeps each library to its own format is

- **HKSK** exposes the pairing — the halves, the prefix, the participants, and
  the track order — from the animation and the cache, which is what it already
  holds;
- **whoever owns the NIF** builds the combined skeleton from two skeleton meshes
  and that description. `SKAssets.Content` already resolves a race to its
  skeleton mesh and its project, which is the join this needs.

## The six that are not combined

`Paired_OffsetBoundStandingCut.hkx` has 99 tracks, no `2_` half, and a partner
file beside it called `Paired_OffsetBoundStandingCutNPC.hkx`. That is the other
convention: two ordinary single-skeleton animations, one per actor, kept in step
by the behaviour graph rather than by sharing a skeleton.

They need nothing new to export — each half is an ordinary animation — but they
do need to be recognised, or a tool that assumes `Paired_*` means a `2_` half
will look for one that is not there.
