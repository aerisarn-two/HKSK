# speeddatasinglefile.txt — speed sampler database

    Status:    format CONFIRMED (byte-exact round trip); semantics CONFIRMED
    Source:    Skyrim SE, meshes/speeddatasinglefile.txt, 162527 bytes
    Consumer:  BSSpeedSamplerModifier via BSSpeedSamplerDBManager
    Gate:      bUseSpeedSampler:Animation, compiled default 1 (ON)
    Reader:    none. HKSK does not implement this file yet.

Third file of the animation cache, after `animationdatasinglefile.txt` and
`animationsetdatasinglefile.txt`. Named `.txt`; binary after byte 1943.

Holds, per project, a precomputed table answering
`(state, direction, goal speed) -> speed`.

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

### 1.1 INI default

No shipped INI names the setting; `Skyrim_Default.ini` and the four quality
presets have no `[Animation]` section. Value is the compiled default.

Each INI setting is a 32-byte record; the value precedes the name pointer:

    offset  field
    +0      vtable          0x141775178 for all bool settings
    +8      value           <- default
    +16     const char*     name
    +24     pad             0xEFBEADDE

`bUseSpeedSampler` value = **1**.

Offset validated over all 21 `b*:Animation` settings sharing that vtable:
debug/dead-platform read 0 (`bDrawAnimPoseInVDB`, `bDisplayMarkWarning`,
`bUseSPUGenerate`, `bEnableHavokHit`, `bAlwaysDriveRagdoll`, `bInitiallyLoadAllClips`),
functional read 1 (`bFootIK`, `bAnimInterpEnable`, `bHumanoidFootIKEnable`,
`bMultiThreadBoneUpdate`).

### 1.2 Query path (from disassembly)

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

**With no database the call is skipped and `speedOut = goalSpeed`** — an identity
pass-through, not a fallback computation. The table is the only source of the
mapping; without it the modifier is inert.

The signature fixes the file's three levels: an integer state, a float direction
and a float goal speed, returning one float. That is `entry.key`, `record.direction`
and `record.points[].x` returning `record.points[].y`.

### 1.3 Load path (from disassembly)

Two loaders, and the INI gate sits on only one of them.

The merged file is read by an ungated function that opens the global
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

So `bUseSpeedSampler` gates the **per-project `.SPD` lazy load**, and its off path
returns false immediately. The three path fragments are interned as globals:

    0x1431c67e0  "MESHES/SPEEDDATA/"
    0x1431c67f0  ".SPD"
    0x1431c67f8  "Meshes/SpeedDataSingleFile.txt"

Parameters, from `bcbehavior.hkb` (the one authoring file that shipped):

    m_enable      bool            = true
    m_state       hkFloatVariable -> iState
    m_direction   hkFloatVariable -> Direction      range [0, 1]
    m_goalSpeed   hkFloatVariable -> Speed          range [0, 384] (this graph)
    m_speedOut    hkFloatVariable -> out

Three inputs; the file nests three deep. Variable ranges exist only in `.hkb`:
`m_wordMinVariableValues` / `m_wordMaxVariableValues` are empty (count 0) in all
8 compiled graphs inspected, while `m_variableInitialValues` is fully populated.

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
            f32     direction;                       /* 0.00 .. 0.90 step 0.05 */
            u32     n_points;
            struct { f32 x; f32 y; } points[n_points];
    };

Sizes: `sizeof(record) = 8 + 8 * n_points`. No padding, no alignment beyond 4.

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
| I3 | `n_records == 19` | 86/88 entries (2 are 0, §6) |
| I4 | `direction[i]` == float accumulation of `+0.05f` (§4.5.2), in order, complete | 86/86 entries |
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

### 4.1 direction — CONFIRMED

The graph's `Direction` variable, range [0,1], sampled at 0.05.

Evidence: curves are mirror-symmetric about 0.5. Over the 688 pairs
(0.10,0.90) (0.15,0.85) ... (0.45,0.55): 127 bit-identical, 499 within 2%,
62 differ — **91.0% mirrored**. Player run state, exact:

    direction 0.40 -> 272.96      direction 0.60 -> 272.96
    direction 0.45 -> 259.44      direction 0.55 -> 259.44

Peaks at 0.25 / 0.75, trough at 0.45-0.55: a compass with 0.0 ahead, 0.5 behind.
Consistent with sampling stopping at 0.90 (0.95 would mirror 0.05).

### 4.2 key — state id, CONFIRMED by behaviour

`m_state` reads `iState`. Player: 14 entries, keys 0-10 and 15-17, forming a
speed ladder:

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

### 4.3 x — goal speed, CONFIRMED

The query signature (§1.2) passes `(i32 state, f32 direction, f32 goalSpeed)` and
returns one float. The first two select entry and record, so `goalSpeed` indexes
within a record and the return is the paired `y`.

Corroborating: race movement speeds fall on x knots far past chance (§5).

Units: same as RACE `MOVT` / `SpeedOverrides` (game units/s).

### 4.4 y — precomputed speed out, CONFIRMED by bound I8

`y` never exceeds what the project's clips can deliver at their authored rate:

| Bound | Exceeded by | Hit exactly | Worst ratio |
| --- | ---: | ---: | ---: |
| fastest clip root-motion speed | 4/46 | 8 | 1.600 |
| that **x `ClipGeneratorEntry.PlaybackSpeed`** | **0/46** | **9** | **1.000** |

The playback factor is required, not cosmetic:

    ChickenProject   max y 403.10   fastest clip 251.94   x playback 403.10   1.000
    HareProject      max y 320.62   fastest clip 200.39   x playback 320.62   1.000

A value capped by `root_motion * playback_rate`, equal to it where the state's
fastest clip is locomotion and below it elsewhere, is a sample of the graph's
output — i.e. the file stores answers, not source data.

## 4.5 How the table relates to root motion

`y` is built from the animations; `x` is not. The two axes anchor on different
things, and the asymmetry is consistent.

Coincidence with a project's clip speeds, against a permutation control that
shuffles the clip sets between projects (n=200):

| | real | shuffled | z |
| --- | ---: | ---: | ---: |
| x knots vs raw clip root-motion speed | 16.2% | 6.3% ± 1.7 | +5.9 |
| x knots vs clip speed × `PlaybackSpeed` | 17.0% | 7.2% ± 2.1 | +4.8 |
| y values vs raw clip root-motion speed | 25.8% | 8.0% ± 3.6 | +4.9 |
| **y values vs clip speed × `PlaybackSpeed`** | **28.9%** | 10.5% ± 3.3 | **+5.6** |

All four are significant; `y` matches roughly twice as well as `x`. Set against
§5, where `x` matches *race* speeds at +18.9 sd, the split is: **x is anchored on
what the race may request, y on what the animations can deliver.**

The chicken shows it exactly, because it has only five locomotion clips:

    y min   0.49  = WalkForward 34.71 x 0.014   (clip Forward_WalkSlow)
    y max 403.10  = RunForward 251.94 x 1.6     (clip Forward_Run)

The curve spans precisely its slowest to its fastest locomotion clip. And at the
anchors, `y/x` is that clip's playback rate:

    x  35.0 -> y  34.03   ratio 0.97    Forward_Walk plays at 1.0,  raw speed 34.71
    x 252.5 -> y 403.10   ratio 1.596   Forward_Run  plays at 1.6,  raw speed 251.94

### 4.5.1 y is the nearest available animation, not x times a gain

A tempting model is `y = raw_animation_speed * node_gain`, with the gain being the
clip generator's `PlaybackSpeed`. It predicts that `y/x` stays inside the envelope
of playback rates the project's clips use. Tested:

| Range of the curve | `max(y/x) <= max playback` | `min(y/x) >= min playback` |
| --- | ---: | ---: |
| whole curve | 29/45 | 38/45 |
| `x >= 10%` of ceiling | 36/45 | 38/45 |
| `x >= 25%` of ceiling | 38/45 | 38/45 |
| `x >= 50%` of ceiling | **40/45** | **41/45** |

The failures are not scattered: every one is the **first driven sample**, where x
is under 1% of the ceiling and y is already large.

    DragonProject          x   3.0  ->  y 384.00    ratio 128.0
    Dragon_Priest          x   1.0  ->  y  80.00    ratio  80.0
    SlaughterfishProject   x   1.0  ->  y 162.06    ratio 162.1
    RieklingProject        x   3.0  ->  y  37.02    ratio  12.3

Ask a dragon to move at 3 and it moves at 384, because it has no slow locomotion
to play. So the gain relates **y to its clip**, not y to x:

> `y` is the speed of the nearest animation the graph can actually produce for that
> request, at the rate its generator plays it. `x` is the query key, not a factor.

That accounts for the rest of the shape without further assumptions: the curve
saturates (I8) because clips run out at the top; it starts high on creatures with
no slow gait; `y/x` equals a playback rate at an anchor because one clip dominates
there; and it drifts between anchors because two clips are blending.

### 4.5.2 Both axes are half-open sweeps

The two axes stop one step short of a round bound, and it is the same construction
twice.

**Direction.** The 19 values are **bit-identical to float accumulation**,
`d += 0.05f`, in all 86 entries — and bit-*different* from `0.05f * i` and from
`i / 20.0f`. They diverge at i = 7:

    i    stored        0.05f * i     accumulated
    7    0.350000024   0.349999994   0.350000024   <- accumulation
    18   0.900000155   0.900000036   0.900000155

    bit-identical to 0.05f * i     :  0 / 86
    bit-identical to accumulation  : 86 / 86
    bit-identical to i / 20.0f     :  0 / 86

So the sweep is `for (d = 0; d < 0.95f; d += 0.05f)` or equivalent: a half-open
range, 19 samples, **0.95 never sampled**. Directions in [0.95, 1.0) fall past the
last knot and must clamp to the 0.90 curve. The mirror symmetry (§4.1) would make
0.95 recoverable from 0.05, but nothing in the file does that — it is simply absent.

A reader that reconstructs the axis as `0.05 * i` will mismatch from the seventh
record on. Compare with a tolerance, or accumulate.

**Speed.** Every x ceiling is `V - 0.5` for an integer V:

    ceiling  189.5  324.5  414.5  424.5  449.5  749.5  832.5  999.5
    V        190    325    415    425    450    750    833    1000

which is the same half-open sweep on the 0.5 grid: `for (x = 0; x < V; x += 0.5f)`.
V is a per-entry limit with **325 as the default** — 74 of the 88 entries. Two of
the others are identifiable: the giant's V = 415 is exactly its fastest clip's raw
root-motion speed (415.00), and the deer's V = 833 is exactly its race's
`ForwardRun` (833.0).

V is not derivable in general. Tested over every entry, "largest 0.5-grid value
strictly below V" matches 1/86 for V = max raw clip speed, 0/86 for max effective
clip speed and 1/40 for max race speed — because for 74 entries V is just the
default. **V is sampler configuration, per state.**

### 4.6 The table was measured, not derived

Two properties of the point spacing say the curves are sampled output, not a
closed form:

- **Spacing is more even in y than in x.** Median coefficient of variation of the
  step size over the 1,321 records with 5+ points: 0.654 along y, 1.009 along x.
- **Knots are load-bearing.** Of 15,034 interior knots, only 20.2% lie within 0.5%
  of the chord between their neighbours; the median knot sits 5.1% off it. These
  are not the leftovers of a dense sweep that was simplified — each one marks a
  real bend.

A curve whose input axis lands on race thresholds, whose output is capped by clip
capability and equals it at the extremes, and whose knots mark bends in between,
is the **recorded response of the behaviour graph** to a swept request. Which is
what a "speed sampler" is named for.

This is why O1 and O2 resist derivation from the shipped files: reproducing the
knots means running the graph, not evaluating a formula. It also says an
implementation can *validate* a generated table — bound by I8, anchored at race
speeds — without being able to synthesise one.

## 5. Cross-check against RACE

Join: a race's `BehaviorGraph` path stem is the project name
(`Actors\Deer\DeerProject.hkx` -> `DeerProject`). 47/49 projects are named this
way by at least one race. The two that are not are `DefaultFemale` and
`FirstPerson`: races point at `Actors\Character\DefaultMale.hkx`, and no RACE
record names the female or first-person graph in that field.

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

Converse is FALSE: only 1-40% of inner knots lie near any race speed. Race
thresholds are a subset of the breakpoints. Clip root-motion speeds do not explain
the remainder either (chicken clips at 74.78 and 107.00 are not near a knot).

Notable: DeerProject race `ForwardRun = 833.0`, entry ceiling `832.5`.
But the ceiling is not `min(cap, race_max)`: 23 entries keep 324.5 while their
race allows more (bear 638, chaurus flyer 725, dragon 7400).

## 6. Known corruption

`FalmerProjectData` declares 4 entries, 2 malformed:

In file order:

    key            hex           bytes (LE)   n_records
    -2147483648    0x80000000                         0     /* INT_MIN */
    1              0x00000001                        19
    2              0x00000002                        19
    1651406194     0x626E7572    "runb"               0

Both malformed keys carry `n_records == 0`, so they occupy 8 bytes and describe
nothing; no lookup will request state `0x80000000`.

`0x626E7572` is not a number. Its bytes as stored are the ASCII `runb` — the first
four characters of a clip name, and `runbackward`, `runbackwardleft` and
`runbackwardright` are all in the Falmer's own cache. The exporter wrote the head of
a string into a `u32` key field.

**A reader MUST tolerate `n_records == 0`.** A writer SHOULD NOT reproduce these.

## 7. Rejected: x as a time axis

The curves rise from near zero and flatten, which resembles an acceleration
profile. It is not one.

| # | Against |
| --- | --- |
| R1 | I7: all 19 direction records of an entry share one exact ceiling. Different directions are different clips of different lengths. |
| R2 | Ceilings do not track clip length. Chicken/hare/bear all stop at 324.5; longest clips 6.67 / 6.67 / 3.83 s (10.8 s each if x were frames @30). |
| R3 | 43 of 49 unrelated projects share exactly 324.5. |
| R4 | Race speeds land on knots at +18.9 sd (§5). A speed has no reason to fall on a time axis. |
| R5 | The modifier has no time input (§4.3). |

y is also unsuited to being an acceleration: per-state maxima are speed magnitudes
matching a sneak/walk/run/sprint ladder (§4.2), and the output variable is
`m_speedOut`.

## 8. Open

| # | Question | Ruled out |
| --- | --- | --- |
| O1 | What sets an entry's ceiling. *Narrowed, §4.5.2:* it is `V - 0.5` for an integer per-state sweep limit V, default 325; what sets a non-default V is sampler configuration. | Not a general rule over race max (23 counterexamples) or clip max (1/86). Not the graph's `Speed` bound — stripped from compiled `.hkx`; the one shipped `.hkb` says 384 where its curve stops at 324.5. |
| O2 | What generates the knots between race thresholds. *Narrowed, §4.6:* they mark bends in a measured response, so reproducing them needs the graph run rather than a formula. | Not clip root-motion speeds alone (§4.5 puts the match at 16%). |
| O3 | *Resolved, §4.5.1.* `y` is the speed of the nearest producible animation at its played rate, not `x` scaled by anything, so it may sit either side of `x` — above where a clip is played faster than authored, far above where the creature has no slow gait. | Not a unit error; not a bounded gain on x (29/45 over the whole curve). |
| O4 | *Resolved, §4.5.2.* Half-open sweep `d < 0.95f` over float-accumulated steps, 19 samples; 0.95 is never reached. | |
| O5 | *Resolved, §1.2.* There are two paths, but the non-DB one is `speedOut = goalSpeed`, an identity pass-through. No runtime computation exists. | |

## 9. Method note

§1.2 and §1.3 required unwrapping the SteamStub Variant 3.1 (x64) wrapper on the
retail executable, which encrypts `.text`: entropy 8.000 bits/byte packed against
6.354 unpacked, and 0 RIP-relative references to the setting object against 6.
Unwrapped with Steamless v3.1.0.5 for reading only. The unwrapped binary is not
redistributable and is not in this repository; every address above is an RVA that
can be re-derived from a local copy.

## 10. Consequences for tooling

The gate defaults ON, so this data is live for every actor whose graph carries the
modifier. A mod that adds a creature, alters a race's movement speeds, or
renumbers a shared graph's species keys leaves this file stale, and nothing
currently reads or writes it.

A project absent from the table is not an error and will not be reported as one:
the modifier passes `goalSpeed` through unchanged, so the actor moves at the speed
requested rather than the speed its animations can deliver. The failure mode is
foot sliding, not a crash.

`MESHES/SPEEDDATA/<...>.SPD` (§1.3) is a supported per-project load path that the
game ships nothing for. A tool that adds one creature can write a single `.SPD`
rather than rewriting the merged file.

Implementing §2 is sufficient to read and rewrite the file losslessly; §4 is
sufficient to interpret it; O1 and O2 are required to *generate* one from scratch.
