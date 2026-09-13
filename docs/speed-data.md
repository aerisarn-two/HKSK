# speeddatasinglefile.txt — speed sampler database

    Status:    format and semantics CONFIRMED; generator inputs partly unknown (§8)
    Source:    Skyrim SE, meshes/speeddatasinglefile.txt, 162527 bytes
    Consumer:  BSSpeedSamplerModifier via BSSpeedSamplerDBManager
    Gate:      bUseSpeedSampler:Animation, compiled default 1 (ON)
    Reader:    none. HKSK does not implement this file yet.

Third file of the animation cache, after `animationdatasinglefile.txt` and
`animationsetdatasinglefile.txt`. Named `.txt`; binary after byte 1943.

Per project, a precomputed table answering `(state, direction, goal speed) -> speed`.

## 1. Engine interface

Strings in `SkyrimSE.exe`:

| String | Meaning |
| --- | --- |
| `bUseSpeedSampler:Animation` | INI gate, `[Animation]` section |
| `Meshes/SpeedDataSingleFile.txt` | merged form, as shipped |
| `MESHES/SPEEDDATA/` | split per-project form; **not shipped** |
| `BSSpeedSamplerDBManager` | singleton, `BSTSingletonSDM` w/ static buffer |
| `BSISpeedSamplerDB` | abstract interface over the above |
| `BSSpeedSamplerModifier` | hkbModifier subclass, the caller |
| `goalSpeed` | |

`BSSpeedSamplerModifier` occurs in 42 compiled graphs / 30 distinct graph names.
Its parameters, from `bcbehavior.hkb` (the one authoring file that shipped):

    m_enable      bool            = true
    m_state       hkFloatVariable -> iState
    m_direction   hkFloatVariable -> Direction      range [0, 1]
    m_goalSpeed   hkFloatVariable -> Speed          range [0, 384] (this graph)
    m_speedOut    hkFloatVariable -> out

Variable ranges survive only in `.hkb`: `m_wordMinVariableValues` and
`m_wordMaxVariableValues` are empty in all 8 compiled graphs inspected, while
`m_variableInitialValues` is fully populated.

### 1.1 INI default

No shipped INI names the setting; `Skyrim_Default.ini` and the four quality
presets have no `[Animation]` section, so the value is the compiled default.

Each INI setting is a 32-byte record; the value precedes the name pointer:

    offset  field
    +0      vtable          0x141775178 for all bool settings
    +8      value           <- default
    +16     const char*     name
    +24     pad             0xEFBEADDE

`bUseSpeedSampler` value = **1**.

The offset reads correctly across all 21 `b*:Animation` settings sharing that
vtable: debug and dead-platform ones are 0 (`bDrawAnimPoseInVDB`,
`bDisplayMarkWarning`, `bUseSPUGenerate`, `bEnableHavokHit`,
`bAlwaysDriveRagdoll`, `bInitiallyLoadAllClips`), functional ones are 1
(`bFootIK`, `bAnimInterpEnable`, `bHumanoidFootIKEnable`,
`bMultiThreadBoneUpdate`).

### 1.2 Query path

`BSSpeedSamplerModifier::Update`, RVA 0xb9f000. `rbx` = the modifier:

    140b9f047  mov   0x1431bd160,%rcx    ; DB singleton pointer
    140b9f04e  movss 0x58(%rbx),%xmm0    ; goalSpeed
    140b9f053  test  %rcx,%rcx
    140b9f056  je    0x140b9f070         ; no DB -> skip, xmm0 unchanged
    140b9f058  mov   (%rcx),%rax         ; vtable
    140b9f05b  mov   %rdi,%rdx           ; arg2  context
    140b9f05e  movss 0x54(%rbx),%xmm3    ; arg4  direction
    140b9f063  mov   0x50(%rbx),%r8d     ; arg3  state (int)
    140b9f067  movss %xmm0,0x20(%rsp)    ; arg5  goalSpeed
    140b9f06d  call  *0x8(%rax)          ; -> xmm0
    140b9f070  movss %xmm0,0x5c(%rbx)    ; speedOut = xmm0

Member layout and query signature:

    +0x50  i32  state          float Query(this,
    +0x54  f32  direction                  void  *context,
    +0x58  f32  goalSpeed                  i32    state,      /* -> entry.key   */
    +0x5c  f32  speedOut                   f32    direction,  /* -> record      */
                                           f32    goalSpeed); /* -> record.x    */
                                           /* returns record.y */

The signature fixes the file's three levels: an integer state, a float direction
and a float goal speed, returning one float — `entry.key`, `record.direction`,
`record.points[].x`, returning `record.points[].y`.

**With no database the call is skipped and `speedOut = goalSpeed`.** That is an
identity pass-through, not a fallback computation: the table is the only source of
the mapping, and without it the modifier is inert.

### 1.3 Load path

Two loaders, and the INI gate sits on only one of them.

The merged file is read by an **ungated** function that opens the global
`BSFixedString` for `Meshes/SpeedDataSingleFile.txt` (0x1431c67f8) at RVA 0xbc08af,
then parses a line with radix 10 into a `u16` — the dirlist count.

The gated function is RVA 0xbc0c20:

    140bc0c48  xor   %dil,%dil
    140bc0c4b  cmp   %dil,0x1461a86(%rip)  ; bUseSpeedSampler
    140bc0c52  je    0x140bc0e09           ; off -> return false, do nothing
    ...
    140bc0c71  call  0x140bc1480           ; probe an existing entry
    140bc0c7a  jne   0x140bc0dd7           ; hit -> done
    140bc0cb3  mov   $0x104,%edx           ; else format MAX_PATH:
    140bc0cc0  call  0x14018a900           ;   "%s%s%s/%s%s"
                                           ;   MESHES/SPEEDDATA/ + ... + .SPD

So `bUseSpeedSampler` gates a **lazy per-project `.SPD` load**, and its off path
returns false immediately. The three path fragments are interned as globals:

    0x1431c67e0  "MESHES/SPEEDDATA/"
    0x1431c67f0  ".SPD"
    0x1431c67f8  "Meshes/SpeedDataSingleFile.txt"

## 2. On-disk format

Little-endian. Dirlist is CRLF ASCII; everything after it is 32-bit words.

    file    := dirlist block[count]                  /* blocks in dirlist order */

    dirlist := "<count>\r\n"
               count x "<Project>Data\<Project>.spd\r\n"

    struct block {                                   /* one per project */
            u32     version;                         /* == 1 */
            u32     n_entries;
            entry   entries[n_entries];
    };

    struct entry {                                   /* one per state */
            u32     key;                             /* state id */
            u32     n_records;                       /* == 19, or 0 if malformed */
            record  records[n_records];
    };

    struct record {                                  /* one per direction */
            f32     direction;                       /* 0.00 .. 0.90, see §5.1 */
            u32     n_points;
            struct { f32 x; f32 y; } points[n_points];
    };

`sizeof(record) = 8 + 8 * n_points`. No padding, no alignment beyond 4.

    dirlist                1943 bytes
    binary               160584 bytes  (40146 words)
    total                162527 bytes

    projects (= blocks)      49
    entries                  88
    records                1634        /* 86 valid entries x 19 */
    points                18302

Read-then-write reproduces the file **byte for byte**.

## 3. Invariants

Measured over the whole file. A reader may assert these; a writer must hold them.

| # | Invariant | Observed |
| --- | --- | ---: |
| I1 | `version == 1` | 49/49 blocks |
| I2 | `n_entries in {1,2,3,4,6,14}` | 49/49 |
| I3 | `n_records == 19` | 86/88 entries (2 are 0, §7) |
| I4 | `direction[i]` == float accumulation of `+0.05f` (§5.1), in order, complete | 86/86 entries |
| I5 | `x mod 0.5 == 0` | 18302/18302 points |
| I6 | `x` non-decreasing within a record | 1634/1634 records |
| I7 | all 19 records of an entry share one exact `max(x)` | 86/86 entries |
| I8 | `max(y) <= max over clips of (root_speed * PlaybackSpeed)` | 46/46 projects |

`y` is **not** monotonic; do not assume it.

Observed `max(x)` per entry (the ceiling):

    189.5   414.5   424.5   449.5   749.5   832.5   999.5    324.5
        1       1       2       1       2       1       4       74     entries

Entries within one project may disagree: DefaultMale and DefaultFemale
{324.5, 749.5, 999.5}, DeerProject {449.5, 832.5}, GiantProject {189.5, 324.5, 414.5}.

## 4. Field semantics

### 4.1 key — state id

`m_state` reads `iState`. The player has 14 entries, keys 0-10 and 15-17, forming
a speed ladder:

    key   max y    ceiling
      5    22.56     324.5
     15    29.47     324.5
      2   132.89     324.5
      3   155.21     999.5
     16   155.21     999.5
      0   307.96     324.5
      9   320.84     324.5      \
      8   320.99     324.5       |  one cluster, not one value
      4   321.03     324.5       |  (6 and 17 also 321.03)
      7   321.10     324.5      /
      1   370.37     324.5
     10   395.94     749.5

Species sharing `quadrupedbehavior.hkx` are keyed in multiples of ten, which is
how a shared graph selects its own table:

    Bear 0   Cow 10   Deer 20,21   Dog 30   Goat 40   Horker 50
    Horse 60   Mammoth 70   SabreCat 80   Skeever 90   Wolf 100

### 4.2 direction

The graph's `Direction` variable, range [0,1]. A compass: 0.0 ahead, 0.25 and 0.75
the two sides, 0.5 behind.

Curves are mirror-symmetric about 0.5. Over the 688 pairs (0.10,0.90) (0.15,0.85)
... (0.45,0.55): 127 bit-identical, 499 within 2%, 62 differ — **91.0% mirrored**.
The player's run state is exact:

    direction 0.40 -> 272.96      direction 0.60 -> 272.96
    direction 0.45 -> 259.44      direction 0.55 -> 259.44

### 4.3 x — goal speed

The query signature (§1.2) passes `(i32 state, f32 direction, f32 goalSpeed)` and
returns one float. The first two select entry and record, so `goalSpeed` indexes
within a record and the return is the paired `y`.

Units are those of RACE `MOVT` / `SpeedOverrides` (game units/s), and race speeds
land on x knots far past chance (§6).

### 4.4 y — speed out

`y` is the speed of the **nearest animation the graph can produce** for that
request, at the rate its generator plays it.

It never exceeds what the project's clips can deliver (I8). The clip generator's
`PlaybackSpeed` is part of that bound, not a detail:

| Bound | Exceeded by | Hit exactly |
| --- | ---: | ---: |
| fastest clip root-motion speed | 4/46 | 8 |
| that **x `ClipGeneratorEntry.PlaybackSpeed`** | **0/46** | **9** |

    ChickenProject   max y 403.10   fastest clip 251.94   x playback 403.10   1.000
    HareProject      max y 320.62   fastest clip 200.39   x playback 320.62   1.000

`y` is free to sit either side of `x`, and does. It exceeds `x` where the clip is
played faster than authored, and far exceeds it where the creature has no slow
gait — ask a dragon for 3 and the table answers 384:

    DragonProject          x   3.0  ->  y 384.00
    Dragon_Priest          x   1.0  ->  y  80.00
    SlaughterfishProject   x   1.0  ->  y 162.06

This accounts for the whole curve shape: it saturates at the top because clips run
out; it starts high where there is no slow gait; `y/x` equals a clip's playback
rate at an anchor, where one clip dominates; and it drifts between anchors, where
two are blending.

## 5. How the table was generated

### 5.1 Both axes are half-open sweeps

**Direction.** The 19 values are bit-identical to float accumulation, `d += 0.05f`,
in all 86 entries — and bit-different from `0.05f * i` and from `i / 20.0f`, which
diverge at i = 7:

    i    stored        0.05f * i     accumulated
    7    0.350000024   0.349999994   0.350000024
    18   0.900000155   0.900000036   0.900000155

    bit-identical to 0.05f * i     :  0 / 86
    bit-identical to accumulation  : 86 / 86
    bit-identical to i / 20.0f     :  0 / 86

The sweep is `for (d = 0; d < 0.95f; d += 0.05f)` or equivalent: half-open, 19
samples, **0.95 never sampled**. Directions in [0.95, 1.0) fall past the last knot
and must clamp to the 0.90 curve; the mirror symmetry would make that sample
recoverable from 0.05, but nothing in the file does it.

**A reader that reconstructs the axis as `0.05 * i` mismatches from the seventh
record on.** Compare with a tolerance, or accumulate.

**Speed.** Every x ceiling is `V - 0.5` for an integer V:

    ceiling  189.5  324.5  414.5  424.5  449.5  749.5  832.5  999.5
    V        190    325    415    425    450    750    833    1000

the same half-open sweep on the 0.5 grid, `for (x = 0; x < V; x += 0.5f)`. V is
per entry, default **325** — 74 of the 88 entries. Two of the others are
identifiable: the giant's V = 415 is exactly its fastest clip's raw root-motion
speed, and the deer's V = 833 is exactly its race's `ForwardRun`. The remaining
five are unattributed, and V is sampler configuration rather than anything
recoverable from the shipped files (§8).

### 5.2 The curves are measured, not derived

Two properties of the point spacing:

- **Spacing is more even in y than in x.** Median coefficient of variation of the
  step size, over the 1,321 records with 5+ points: 0.654 along y, 1.009 along x.
- **Knots are load-bearing.** Of 15,034 interior knots, only 20.2% lie within 0.5%
  of the chord between their neighbours; the median sits 5.1% off it. Each marks
  a real bend rather than being the residue of a denser sweep.

### 5.3 Which axis comes from where

`y` is built from the animations; `x` is not. Coincidence with a project's clip
speeds, against a permutation control that shuffles the clip sets between projects
(n=200):

| | real | shuffled | z |
| --- | ---: | ---: | ---: |
| x knots vs raw clip root-motion speed | 16.2% | 6.3% ± 1.7 | +5.9 |
| x knots vs clip speed × `PlaybackSpeed` | 17.0% | 7.2% ± 2.1 | +4.8 |
| y values vs raw clip root-motion speed | 25.8% | 8.0% ± 3.6 | +4.9 |
| **y values vs clip speed × `PlaybackSpeed`** | **28.9%** | 10.5% ± 3.3 | **+5.6** |

All four are significant; `y` matches roughly twice as well as `x`. Set against §6,
where `x` matches *race* speeds at +18.9 sd: **x is anchored on what the race may
request, y on what the animations can deliver.**

The chicken shows it directly, having only five locomotion clips — the curve spans
precisely its slowest to its fastest:

    y min   0.49  = WalkForward 34.71 x 0.014   (clip Forward_WalkSlow)
    y max 403.10  = RunForward 251.94 x 1.6     (clip Forward_Run)

    x  35.0 -> y  34.03   Forward_Walk plays at 1.0,  raw speed 34.71
    x 252.5 -> y 403.10   Forward_Run  plays at 1.6,  raw speed 251.94

An input axis on race thresholds, an output capped by clip capability and equal to
it at the extremes, and knots marking bends in between: the file is the **recorded
response of the behaviour graph** to a swept request, which is what a speed
sampler is named for.

## 6. Cross-check against RACE

Join: a race's `BehaviorGraph` path stem is the project name
(`Actors\Deer\DeerProject.hkx` -> `DeerProject`). 47/49 projects are named this way
by at least one race; `DefaultFemale` and `FirstPerson` are not, because races
point at `Actors\Character\DefaultMale.hkx`.

Speeds taken from `BaseMovementDefault{Walk,Run,Sprint,Sneak,Swim,Fly}` -> `MOVT`
and from the race's own `MovementTypes[].Overrides` (forward/back/left/right x
walk/run).

| Test | Hit | Control |
| --- | ---: | ---: |
| all race speeds within 0.5 of a knot | 36.4% | 13.0% |
| speeds <= entry ceiling, within 0.5 | **48.8%** | 9.8% |
| same, within 1.0 | 56.7% | 13.7% |

Permutation test (keep each project's knots and its number of speeds, shuffle the
values between projects, n=300): null 15.2% +/- 1.8%; real 48.8% = **+18.9 sd**,
0/300 trials reached it. The association is project-specific, not scale-driven.

The relation is one-way. Only 1-40% of inner knots lie near any race speed, so
race thresholds are a subset of the breakpoints; the rest come from the graph's
own response (§5.2). `DeerProject` is the clearest single case: race
`ForwardRun = 833.0`, entry ceiling `832.5`.

## 7. Known corruption

`FalmerProjectData` declares 4 entries, 2 malformed. In file order:

    key            hex           bytes (LE)   n_records
    -2147483648    0x80000000                         0     /* INT_MIN */
    1              0x00000001                        19
    2              0x00000002                        19
    1651406194     0x626E7572    "runb"               0

Both malformed keys carry `n_records == 0`, so they occupy 8 bytes and describe
nothing; no lookup will request state `0x80000000`.

`0x626E7572` is not a number. Its bytes as stored are the ASCII `runb` — the head
of a clip name, and `runbackward`, `runbackwardleft` and `runbackwardright` are
all in the Falmer's own cache. The exporter wrote the start of a string into a
`u32` key field.

**A reader MUST tolerate `n_records == 0`.** A writer SHOULD NOT reproduce these.

## 8. Open

Both remaining questions are about the generator's inputs, not about the format.

**What sets a non-default V** (§5.1). Two of the seven non-default ceilings match
a clip speed and a race speed; the other five are unattributed, and no rule over
the shipped data predicts V for the 74 entries that simply take the default.

**What places the knots between race thresholds** (§6). They mark bends in a
measured response (§5.2), so reproducing them means running the behaviour graph
rather than evaluating a formula.

Consequently a table can be **validated** from the shipped files — bound by I8,
anchored at the race speeds, ceiling of the form `V - 0.5`, direction axis
accumulated — without being **synthesised** from them.

## 9. Method note

§1.2 and §1.3 required unwrapping the SteamStub Variant 3.1 (x64) wrapper on the
retail executable, which encrypts `.text`. Unwrapped with Steamless v3.1.0.5, for
reading only; verified by `.text` entropy falling from 8.000 to 6.354 bits/byte and
by references to the setting object appearing (6, against 0 while packed).

The unwrapped binary is not redistributable and is not in this repository. Every
address quoted is an RVA, re-derivable from a local copy.

## 10. Consequences for tooling

The gate defaults ON, so this data is live for every actor whose graph carries the
modifier. A mod that adds a creature, alters a race's movement speeds, or renumbers
a shared graph's species keys leaves this file stale, and nothing currently reads
or writes it.

A project absent from the table is not an error and will not be reported as one:
the modifier passes `goalSpeed` through unchanged (§1.2), so the actor moves at the
speed requested rather than the speed its animations can deliver. The failure mode
is foot sliding, not a crash.

`MESHES/SPEEDDATA/<...>.SPD` (§1.3) is a supported per-project load path that the
game ships nothing for. A tool that adds one creature can write a single `.SPD`
rather than rewriting the merged file.

Implementing §2 is sufficient to read and rewrite the file losslessly; §4 is
sufficient to interpret it; §8 is what stands between that and generating one.
