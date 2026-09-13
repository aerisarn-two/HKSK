# The third cache file: speeddatasinglefile.txt

The animation cache is three files, not two. Beside `animationdatasinglefile.txt`
and `animationsetdatasinglefile.txt` the game ships
**`meshes/speeddatasinglefile.txt`** — 162,527 bytes, named `.txt`, and **binary
after the first 1,943 bytes**. HKSK does not read it and nothing else appears to
either.

This is what it holds. The grammar below was verified by reading the whole file
and writing it back: **the result is byte-identical to the shipped file**, so the
structure is not a guess that happens to fit the start.

## Where the engine says it comes in

Four strings in `SkyrimSE.exe` settle what this is for:

```
bUseSpeedSampler:Animation          the INI setting that turns it on, [Animation]
Meshes/SpeedDataSingleFile.txt      the merged file, as shipped
MESHES/SPEEDDATA/                   a split per-project form the game never ships
BSSpeedSamplerDBManager             a singleton database, behind BSISpeedSamplerDB
BSSpeedSamplerModifier              the behaviour node that queries it
goalSpeed
```

Two things worth taking from that list. The engine knows a **split form** under
`MESHES/SPEEDDATA/`, the same arrangement as `animationdata/` and
`animationsetdata/` — and unlike those two, no split file ships: searching every
BSA for `.spd` or any path containing `speed` returns the merged file and nothing
else.

And the setting is **on by default**, so this data is live. That is worth stating
carefully, because it is easy to assume the opposite of a file nothing reads. No
INI shipped with the game mentions `bUseSpeedSampler` — none of `Skyrim_Default.ini`,
`Low/Medium/High/Ultra.ini` even has an `[Animation]` section — so the value is the
one compiled into the executable, and that is **1**.

Read out of `SkyrimSE.exe` rather than assumed. Each INI setting is a 32-byte
record `{vtable, value, name, pad}`, so the default sits eight bytes before the
pointer to the name string. The offset is not a guess either: all 21 `b*:Animation`
settings share one vtable, and their values split the way defaults should — the
debug and dead-platform ones are 0 (`bDrawAnimPoseInVDB`, `bDisplayMarkWarning`,
`bUseSPUGenerate`, `bEnableHavokHit`, `bAlwaysDriveRagdoll`) and the working ones
are 1 (`bFootIK`, `bAnimInterpEnable`, `bHumanoidFootIKEnable`,
`bMultiThreadBoneUpdate`). `bUseSpeedSampler` is among the 1s.

This is static analysis, not a runtime observation: the sampler also needs a
`BSSpeedSamplerModifier` wired into the graph and enabled, which 42 of the game's
graphs have.

The consumer is a behaviour node. `BSSpeedSamplerModifier` appears in 42 of the
game's compiled behaviour graphs (30 distinct names, several shared between
actors), and the authoring form — `bcbehavior.hkb`, the one text
copy that shipped by accident — gives its parameters outright:

```
m_enable     bool
m_state      float variable  ->  iState
m_direction  float variable  ->  Direction
m_goalSpeed  float variable  ->  Speed
m_speedOut   float variable  ->  (the answer)
```

So the node is a lookup: **(state, direction, goal speed) → speed**. The file is
that lookup table, and its three levels of nesting are those three inputs.

## The format

```
file    := dirlist, block × count                      blocks are in dirlist order
dirlist := "<count>\r\n", count × "<Project>Data\<Project>.spd\r\n"
block   := int32 version, int32 nEntries, entry × nEntries
entry   := int32 key, int32 nRecords, record × nRecords
record  := float direction, int32 nPoints, (float x, float y) × nPoints
```

Everything after the dirlist is little-endian 32-bit words. `version` is 1 in all
49 blocks.

| | |
| --- | ---: |
| Projects (dirlist entries, and blocks) | 49 |
| Entries | 88 |
| Records | 1,634 |
| Points | 18,302 |
| Dirlist / binary split | 1,943 / 160,584 bytes |

Entries per project are 1, 2, 3, 4, 6 or 14. Records per entry are **always 19**,
except two malformed entries that declare 0 — see the last section.

### direction

The 19 records of an entry are tagged `0.00, 0.05, 0.10 … 0.90`, always in that
order, always complete. That this is the graph's `Direction` variable is not a
guess:

- the `.hkb` declares `Direction` as a float with **MinValue 0, MaxValue 1**, so
  the 19 records sample its range at 0.05;
- and the curves are **mirror-symmetric about 0.5**. Over the 688 pairs
  (0.10, 0.90), (0.15, 0.85) … (0.45, 0.55) in the file, 127 are bit-identical
  and 499 more agree within 2% — **91% mirrored**. The player's run state is the
  clearest case, where the halves are exactly equal:

```
key=0   direction 0.40 → 272.96      direction 0.60 → 272.96
        direction 0.45 → 259.44      direction 0.55 → 259.44
        peaks at 0.25 and 0.75, trough across 0.45–0.55
```

A left-right symmetry about 0.5 with peaks at the quarter points is a compass:
0.0 ahead, 0.25 and 0.75 the two sides, 0.5 behind. It also explains why the
sampling stops at 0.90 rather than 0.95 — 0.95 would mirror 0.05.

Nothing yet explains why 0.05 is sampled and its mirror 0.95 is not.

### key — the state

`m_state` reads a variable called `iState`, and the keys behave like state
numbers. The player has **14 entries**, keyed 0–10 and 15–17, and their ceilings
differ by an order of magnitude, which is what a set of locomotion states looks
like:

| key | max speed reached | reads as |
| ---: | ---: | --- |
| 5 | 22.56 | barely moving |
| 15 | 29.47 | |
| 2 | 132.89 | walk |
| 3, 16 | 155.21 | |
| 0 | 307.96 | run |
| 4, 6, 7, 8, 9, 17 | ~321 | |
| 1 | 370.37 | |
| 10 | 395.94 | fastest |

For creatures the keys do something different and more interesting: the ten
species that **share `quadrupedbehavior.hkx`** get distinct keys in multiples of
ten — cow 10, deer 20 and 21, dog 30, goat 40, horker 50, horse 60, mammoth 70,
sabre cat 80, skeever 90, wolf 100 — while the bear, which shares the same graph,
keeps 0. A shared graph needs a way to ask for its own species' table, and that
is what the key is.

### x and y — the curve

Each record is a monotonic lookup curve. Two properties hold across all 18,302
points in the file without exception:

- **x is always a multiple of 0.5.** Every point, every record, every project.
- **x is non-decreasing** within a record.

x therefore reads as a swept input — the table was built by stepping a speed in
half-unit increments — and y as what came back. y is not constrained: it is an
ordinary float, it is usually increasing, and it flattens into plateaus where
asking for more delivers no more. The bear has a long one:

```
x   172.5  173.0  173.5  174.0  174.5  175.0  175.5
y   108.36 108.36 108.37 108.37 108.38 108.38 108.38
```

The x ceiling is **per entry, not per project**: 324.5 on 74 of the 88 entries,
and 189.5, 414.5, 424.5, 449.5, 749.5, 832.5 or 999.5 on the rest. Four projects
hold entries that disagree with each other — the player (324.5, 749.5, 999.5),
the deer (449.5, 832.5) and the giant (189.5, 324.5, 414.5). Whatever sets the
ceiling is a property of the state, not of the creature.

## Checked against the RACE records

A race names its behaviour graph, and the graph's file stem is the project name,
so the join is exact: `Actors\Deer\DeerProject.hkx` → `DeerProject`. **47 of the
49 projects** are named by at least one race this way.

The speeds a race configures — `MOVT` records through `BaseMovementDefault*`,
plus the race's own `SpeedOverrides` with forward, back, left, right × walk, run
— land on the curve's knots far more often than chance:

| | knots hit | control |
| --- | ---: | ---: |
| All race speeds, within 0.5 | 36.4% | 13.0% |
| Race speeds at or below the entry's ceiling, within 0.5 | **48.8%** | 9.8% |
| …within 1.0 | 56.7% | 13.7% |

The control draws the same number of speeds uniformly from each project's own
range and measures them against the same knots, so the **5× enrichment** is not
an artefact of dense knots. The clearest single case is the deer, whose race says
`ForwardRun = 833.0` and whose curve ends at **832.5** — one half-unit step below.
The chicken's `ForwardWalk = 34.71` sits between its knots at 34.0 and 35.0, and
the y value jumps from 12.60 to 34.03 across that pair, which is where a walk
blend would hand over.

**The converse is false and worth stating.** Only 1–40% of the knots are near any
race speed, so the race thresholds are a *subset* of the breakpoints rather than
their source. Something finer determines the rest, and it is not the animations'
own root-motion speeds either: the chicken's clips run at 34.71, 74.78, 107.00 and
251.94, and 74.78 and 107.00 are nowhere near a knot.

So: x is a speed in the same units the RACE records use, and the race's
thresholds are among the points the table keeps. What generates the others is open.

## What is still open

- **Which of x and y is the input.** The lookup is goal speed in, speed out, and
  x is the swept axis, which makes x the goal. But the player's fastest state
  answers 395.94 to a 324.5 ceiling, so y exceeds x and "the speed you will get"
  cannot be the whole story. It may be a playback rate, or a speed in a second
  frame of reference.
- **What sets an entry's ceiling** (324.5 on most, up to 999.5 on the player).
- **What generates the knots** between the race thresholds.
- **Why direction stops at 0.90.**

## The file is not clean

`FalmerProjectData` declares four entries and two of them are junk:

| key | as hex | records |
| ---: | --- | ---: |
| 1 | | 19 |
| 2 | | 19 |
| -2147483648 | `0x80000000` | 0 |
| 1651406194 | `0x626F6172` — ASCII `boar` | 0 |

Both carry `nRecords = 0`, which is why the file still parses and why the game
never notices: they cost eight bytes, describe nothing, and no lookup will ever
ask for a state numbered `0x80000000`. A key holding the letters of `boar` is an
exporter writing a string where an integer belongs. **A reader must tolerate an
entry with no records**, and a writer should not reproduce these.

## Why it matters

A mod that adds a creature, changes a race's movement speeds, or renumbers a
shared behaviour graph's species keys has a third cache file to keep consistent,
and nothing currently reads or writes it.

Since the setting is on by default, a project whose speed data is missing or stale
is asking the sampler questions the table cannot answer — for every creature whose
graph carries the modifier. That is the opposite of the conclusion this document
first reached, and the reason it was worth reading the default out of the binary
instead of inferring it from a file nobody parses.
