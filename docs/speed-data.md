# speeddatasinglefile.txt — speed sampler database

    Status:    format CONFIRMED (byte-exact round trip); semantics CONFIRMED
               four generator inputs unknown (§5.4)
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
| I2 | `n_entries` = one per sampled locomotion state (§4.1); in `{1,2,3,4,6,14}` | 49/49 |
| I3 | `n_records == 19` | 86/88 entries (2 are 0, §6) |
| I4 | `direction[i]` is the float accumulation of `+0.05f`, in order, complete | 86/86 entries |
| I5 | the direction sequence ends at 0.90; 0.95 is never present | 86/86 entries |
| I6 | `x mod 0.5 == 0` | 18302/18302 points |
| I7 | `x` non-decreasing within a record | 1634/1634 records |
| I8 | all 19 records of an entry share one exact `max(x)` | 86/86 entries |
| I9 | `max(y) <= max over the project's clips of (root_speed * PlaybackSpeed)` | 46/46 projects |

Not invariant, and not to be assumed: `y` is not monotonic; the 19 records of an
entry do not share a minimum `x` (21 distinct minima across the file, 62 of 86
entries holding at least two); `y` may exceed `x` (§5.1).

**I4 is exact and a reader must match it exactly.** The stored values are
bit-identical to accumulation and bit-different from `0.05f * i` and `i / 20.0f`,
which diverge from i = 7:

    i    stored        0.05f * i     accumulated
    7    0.350000024   0.349999994   0.350000024
    18   0.900000155   0.900000036   0.900000155

**I5** means directions in [0.95, 1.0) have no record and resolve against the 0.90
curve.

**I9** requires the playback factor. Against raw root-motion speed alone the bound
fails on 4 of 46 projects; with each clip's `ClipGeneratorEntry.PlaybackSpeed`
applied it holds on all 46 and is met exactly by 9.

`max(x)` per entry:

    189.5   324.5   414.5   424.5   449.5   749.5   832.5   999.5
        1      74       1       2       1       2       1       4     entries

Entries within one project may differ: `DefaultMale` and `DefaultFemale`
{324.5, 749.5, 999.5}, `DeerProject` {449.5, 832.5}, `GiantProject`
{189.5, 324.5, 414.5}.

## 4. Field semantics

### 4.1 key — locomotion state id

`m_state` reads the graph variable `iState`. The key is not opaque: it decomposes
into a species slot and a locomotion state index, and both halves are readable off
the behaviour graph.

#### Species slot

Eleven actors share one locomotion graph, `quadrupedbehavior.hkx`. Each is given a
slot of ten, and the slots are the species in **alphabetical order**:

    #   species     key      #   species     key
    0   Bear          0      6   Horse        60
    1   Cow          10      7   Mammoth      70
    2   Deer         20      8   SabreCat     80
    3   Dog          30      9   Skeever      90
    4   Goat         40     10   Wolf        100
    5   Horker       50

`key = 10 x alphabetical index`, exact for all eleven. A shared graph needs a way
to ask for its own species' table, and this is it. The horse is in the numbering
though its graph is `horsebehavior.hkx` rather than the shared one.

**`BoarProject` is the exception and shows the scheme is static.** The boar
(Dragonborn) shares `quadrupedbehavior.hkx` and would take slot 10 if inserted
alphabetically, displacing Cow through Wolf. It has key 0 instead, colliding with
the bear. The slots were fixed before the DLC and the late species was not fitted
in.

Every actor that does not share a locomotion graph uses base 0.

#### Locomotion state index

Within a species slot, the offset is the state index in the actor's forward
locomotion state machine. The deer is the clear case, holding two entries where
most actors hold one. Its `forwardlocomotion.hkx` carries
`ForwardLocomotionBehavior` with exactly two states, and their clip sets separate
the gaits:

    id 0   ForwardState_Deer     WalkForward, TrotForward (+L/R)     -> key 20
    id 1   RunForwardState       RunForward (+L/R)                   -> key 21

The two tables differ as the gaits do: key 20 tops out at 431.92 and key 21 at
832.25, which is the deer's run clip at its authored rate.

Sampling is per state and **not every state is sampled**. The canines carry the
same two-state machine and have one entry each — dog 30, wolf 100, no 31 or 101 —
so the walk and trot gait has no table of its own and resolves against the run
table. Reading a state machine's state count does not predict the entry count.

#### Reading a state's clip set

Walking the graph's state machines is also how a state's animations are obtained:
each `hkbStateMachineStateInfo` carries `m_stateId` and a generator subtree, and
the `hkbClipGenerator` leaves under it give `m_animationName` and
`m_playbackSpeed`. That is the route from a key to the root motion behind it, used
in §5.4.

#### Other actors

Where the graph is not shared, keys are a plain state list. The player has 14,
keyed 0-10 and 15-17, and their tables form a speed ladder:

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

The speed the actor moves at: the root-motion speed of the animation the graph
selects, times that clip generator's `PlaybackSpeed`. Curve shape is §5.1.

`y` is bounded by what the project's clips can deliver (I9). The playback factor is
required for the bound to hold, not a refinement:

    bound                                        exceeded by   hit exactly
    fastest clip root-motion speed                    4 / 46             8
    that x ClipGeneratorEntry.PlaybackSpeed           0 / 46             9

## 5. Construction

### 5.1 Data model

A record is the locomotion response of one `(state, direction)` pair.

    x   requested speed    query key; game units/s; 0.5 grid
    y   delivered speed    root-motion speed of the animation the graph selects,
                           times that clip generator's PlaybackSpeed

The graph selects from a fixed clip set and blends adjacent clips, so the response
is piecewise: pinned to one clip below the slowest, linear between clips, saturated
above the fastest. The record stores the response, not the clip set behind it.

`ChickenProject` has five clips with non-zero root motion:

    clip                 raw speed   playback   delivered
    Forward_WalkSlow         34.71      0.014         0.486
    Forward_Walk             34.71      1.0          34.71
    TurnCannedL180Flee       74.78      1.0          74.78
    TurnCannedL90Flee       107.00      1.0         107.00
    Forward_Run             251.94      1.6         403.10

Its direction-0.00 record in full:

    x        0.0   31.5   33.5   34.0   35.0  101.5  152.0  195.5  233.0  252.5  324.5
    y      0.486   3.99   8.79  12.60  34.03 114.70 190.55 270.56 354.20 403.10 403.10

Endpoints are single clips, to stored precision: 34.71 x 0.014 = 0.48594 and
251.94 x 1.6 = 403.104, the repeated last pair being the saturated region (I9).
Interior points are blends and match no clip individually.

`y` is unordered with respect to `x`. It exceeds `x` where the selected clip plays
above its authored rate, and by large factors where the actor's slowest locomotion
clip is fast:

    DragonProject          x   3.0  ->  y 384.00
    Dragon_Priest          x   1.0  ->  y  80.00
    SlaughterfishProject   x   1.0  ->  y 162.06

### 5.2 Series structure

**`x` is a consequence of `y`.** Points are retained at response breakpoints, so
each direction keeps its own positions. Overlap of x positions across an entry's 19
records:

    Jaccard      entries
    0.0 - 0.1         53
    0.1 - 0.4         11
    0.5 - 0.9         19
    1.0                3

`RieklingProject` key 0: 400 distinct x positions across 19 records, 1 in common.
The three at 1.0 are `AtronachStormProject`, `WispProject` and `WitchlightProject`,
where every y is zero. Only `max(x)` is shared (I8); the minimum is not (§3).

**Retention is by breakpoint, not by grid position.** Two measurements:

    interior points                                   15034
      within 0.5% of the chord between neighbours     20.2%
      median offset from that chord                    5.1%

    step-size coefficient of variation, median over the
    1321 records holding 5 or more points
      along y                                        0.654
      along x                                        1.009

No point is redundant and spacing follows the output axis. Median 9.8 points per
record against a sweep of up to 650 positions.

### 5.3 Generation algorithm

    for each sampled state s:
        for (d = 0.0f; d < 0.95f; d += 0.05f):        /* 19 iterations */
            for (x = 0.0f; x < top(s); x += 0.5f):
                graph.Direction = d
                graph.Speed     = x
                graph.Step()
                y = graph.locomotion_speed()
            retain (x, y) at response breakpoints
        emit entry { key = s, records = 19 curves }

`top(s)`, the retention rule, the evaluator and the set of sampled states are
inputs, not derivable: §5.4.

Output must satisfy I1-I9 (§3). x is in the units RACE `MOVT` records use.

### 5.4 Missing inputs

§2 and §4 suffice to read and rewrite a table. Producing one needs four inputs, none
present in the game files.

    #  input                        shape              status
    1  behaviour graph evaluator    code               blocker
    2  top(s), sweep upper bound    1 float per entry  authored; default 324.5
    3  point-retention rule         1 algorithm        closed experiment
    4  set of sampled states        list of state ids  authored

**1 — Evaluator.** The inner loop is "step the graph, read the resulting locomotion
speed". The table is that measurement and has no closed form (§5.2). Requires
driving `hkbBehaviorGraph` far enough to resolve which animation plays at a given
`(state, direction, speed)` and at what rate.

**2 — Sweep upper bound.** Exposed only as `max(x)`, distribution in §3. The seven
non-default values were searched against `MOVT` speeds, race records,
every numeric field of both, clip root motion, root motion times playback rate,
clip travel distances, durations, behaviour-graph float literals, and a regression
on each state's own animations. Every test is enriched on the twelve non-default
entries and at chance across all 86 — a large candidate pool, not a mechanism.

**3 — Retention rule.** Unknown, and the only gap with a checkable answer. Given y
on the full grid, candidates (Douglas-Peucker at a tolerance, curvature threshold,
error-bounded decimation) are scored against the shipped file by whether they
reproduce its exact point sets in all 1,634 records.

**4 — Sampled states.** One entry per sampled locomotion state. Not every state is
sampled: the canines carry a two-state forward locomotion machine and hold one
entry each (§4.1). A state count does not give an entry count.

With those four, the rest follows: key from §4.1, direction values from I4, grid and
shared ceiling from I6-I8, output bound from I9.

### 5.5 Authoring

The table is measured, so it is changed by changing what is measured:

    input                              controls
    locomotion clip root motion        the y range: what the actor can deliver
    ClipGeneratorEntry.PlaybackSpeed   scales each clip's contribution to y
    RACE MOVT / SpeedOverrides         what the game requests at runtime, in x
    top(s)                             how far along x the table covers

To make an actor faster: add or replace a faster clip, or raise its generator's
`PlaybackSpeed`, then regenerate. Editing `y` in the file desynchronises the table
from the animations it describes and can violate I9.

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

Format and semantics are settled. Four generator inputs are not: see §5.4. A table
can be read, rewritten and validated from the shipped files; it cannot be
synthesised from them.

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
