# speeddatasinglefile.txt — speed sampler database

    Status:    format and semantics CONFIRMED; generator inputs partly unknown (§7)
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
| I3 | `n_records == 19` | 86/88 entries (2 are 0, §6) |
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

Units are those of RACE `MOVT` / `SpeedOverrides` (game units/s).

### 4.4 y — speed out

The speed the actor ends up moving at: the animation the graph plays in answer to
that request, at the rate its generator plays it. What that means for the curve's
shape is §5.

It never exceeds what the project's clips can deliver (I8), and the clip
generator's `PlaybackSpeed` is part of that bound rather than a detail — without
it the bound does not hold:

| Bound | Exceeded by | Hit exactly |
| --- | ---: | ---: |
| fastest clip root-motion speed | 4/46 | 8 |
| that **x `ClipGeneratorEntry.PlaybackSpeed`** | **0/46** | **9** |

## 5. Table contents and regeneration

### 5.1 Data model

One record holds the locomotion response of one `(state, direction)` pair:

    x   requested speed        query key, game units/s, 0.5 grid
    y   delivered speed        root-motion speed of the animation the graph
                               selects, multiplied by its clip generator's
                               PlaybackSpeed

The graph selects from a fixed set of locomotion clips and blends between
adjacent ones. The response is therefore piecewise: linear between clips,
saturated above the fastest, and pinned to a single clip below the slowest. The
record stores that response as a point list, not the clip set it derives from.

Worked example. `ChickenProject` has five clips with non-zero root motion:

    clip                 raw speed   playback   delivered
    Forward_WalkSlow         34.71      0.014         0.486
    Forward_Walk             34.71      1.0          34.71
    TurnCannedL180Flee       74.78      1.0          74.78
    TurnCannedL90Flee       107.00      1.0         107.00
    Forward_Run             251.94      1.6         403.10

Its curve for direction 0.00, in full:

    x        0.0   31.5   33.5   34.0   35.0  101.5  152.0  195.5  233.0  252.5  324.5
    y      0.486   3.99   8.79  12.60  34.03 114.70 190.55 270.56 354.20 403.10 403.10

The two extremes are single clips and match to the precision stored: the first
point is `Forward_WalkSlow` (34.71 x 0.014 = 0.48594) and the last two are
`Forward_Run` (251.94 x 1.6 = 403.104), the final pair being the saturated region
required by I8. Interior points are blends and match no clip individually.

`y` is unordered with respect to `x`. It exceeds `x` wherever the selected clip is
played above its authored rate, and exceeds it by a large factor on actors whose
slowest locomotion clip is fast:

    DragonProject          x   3.0  ->  y 384.00
    Dragon_Priest          x   1.0  ->  y  80.00
    SlaughterfishProject   x   1.0  ->  y 162.06

### 5.2 Point placement

Points are samples of the response, retained at its breakpoints. Two measurements
establish this:

    interior knots                                    15034
      within 0.5% of the chord between neighbours     20.2%
      median offset from that chord                    5.1%

    step-size coefficient of variation, median over the 1321
    records holding 5 or more points
      along y                                         0.654
      along x                                         1.009

Retention follows the output axis, and no point is redundant. There is no closed
form: regeneration requires evaluating the graph.

### 5.3 Generation algorithm

    for each state s:
        for (d = 0.0f; d < 0.95f; d += 0.05f):          /* 19 iterations */
            for (x = 0.0f; x < V(s); x += 0.5f):
                graph.Direction = d
                graph.Speed     = x
                graph.Step()
                y = graph.locomotion_speed()
            retain the (x, y) pairs at response breakpoints
        emit entry { key = s, records = 19 curves }

Constraints on the output, each verified against every entry in the shipped file:

    C1  Accumulate the direction; do not compute it. Stored values are
        bit-identical to d += 0.05f (86/86 entries) and bit-different from
        0.05f * i and i / 20.0f, which diverge from i = 7:

            i    stored        0.05f * i     accumulated
            7    0.350000024   0.349999994   0.350000024
            18   0.900000155   0.900000036   0.900000155

    C2  Both loops are half-open. Direction terminates at 0.90; 0.95 is never
        sampled, and inputs in [0.95, 1.0) resolve against the 0.90 curve.
        Speed terminates at V - 0.5; V is never sampled.

    C3  x lies on the 0.5 grid at every point (18302/18302) and is
        non-decreasing within a record (1634/1634).

    C4  All 19 records of an entry share one exact max(x) (86/86).

    C5  max(y) does not exceed max over the project's clips of
        (root_speed * PlaybackSpeed) (46/46 projects).

x is expressed in the units RACE `MOVT` records use.

### 5.4 Required inputs not present in game data

Two, both to §5.3. Everything else it consumes is in the shipped files or fixed by
C1-C5.

**V(s), the sweep limit.** Integer, per state. The file exposes it only as the
ceiling `V - 0.5`:

    ceiling  189.5  324.5  414.5  424.5  449.5  749.5  832.5  999.5
    V        190    325    415    425    450    750    833    1000
    entries      1     74      1      2      1      2      1      4

V = 325 is the default. Of the seven other values, `GiantProject` V = 415 equals
its fastest clip's raw root-motion speed and `DeerProject` V = 833 equals its
race's `ForwardRun`; the remaining five have no counterpart in game data. No rule
over the shipped files predicts V. Treat it as sampler configuration.

**A behaviour graph evaluator.** Required by the inner loop and by the breakpoint
retention in §5.2.

Without these, a table may be **validated** but not **synthesised**. Validation
checks C1-C5 and the `V - 0.5` ceiling form against the shipped files alone.

## 6. Known corruption

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

## 7. Open

The format is fully specified; what is missing is two of the generator's inputs,
set out in §5.4: the per-state sweep limit V, and a behaviour graph evaluator to
sample. Neither is recoverable from the shipped files, so a table can be validated
from them but not synthesised.

## 8. Method note

§1.2 and §1.3 required unwrapping the SteamStub Variant 3.1 (x64) wrapper on the
retail executable, which encrypts `.text`. Unwrapped with Steamless v3.1.0.5, for
reading only; verified by `.text` entropy falling from 8.000 to 6.354 bits/byte and
by references to the setting object appearing (6, against 0 while packed).

The unwrapped binary is not redistributable and is not in this repository. Every
address quoted is an RVA, re-derivable from a local copy.

## 9. Consequences for tooling

The gate defaults ON, so this data is live for every actor whose graph carries the
modifier. A mod that adds a creature, alters a race's movement speeds, or renumbers
a shared graph's species keys leaves this file stale, and nothing currently reads
or writes it.

A project absent from the table is not an error and will not be reported as one:
the modifier passes `goalSpeed` through unchanged (§1.2), so the actor moves at the
speed requested rather than the speed its animations can deliver. The failure mode
is foot sliding, not a crash.

A project is matched to the RACE records that use it by the stem of the race's
`BehaviorGraph` path: `Actors\Deer\DeerProject.hkx` names `DeerProject`. This
covers 47 of the 49 projects; `DefaultFemale` and `FirstPerson` are not named by
any race, which points its graph field at `Actors\Character\DefaultMale.hkx`.

`MESHES/SPEEDDATA/<...>.SPD` (§1.3) is a supported per-project load path that the
game ships nothing for. A tool that adds one creature can write a single `.SPD`
rather than rewriting the merged file.

Implementing §2 is sufficient to read and rewrite the file losslessly; §4 is
sufficient to interpret it; §5.4 is what stands between that and generating one.
