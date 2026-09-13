# speeddatasinglefile.txt — speed sampler database

    Status:    format CONFIRMED (byte-exact round trip); semantics CONFIRMED
               key set derivable from the behaviour graph (§4.1)
               y range derivable from clip root motion (§4.4)
               three generator inputs unknown (§5.4)
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
the mapping, and without it the modifier is inert — the actor blends gaits on the
speed requested rather than the speed it can reach (§6.4).

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

### 4.1 key — state id

`m_state` reads the graph variable `iState`. **The key set is declared by the
graph.** Each sampled state has an `iState_<MOVT>` variable whose initial value is
the state id and whose name suffix names a RACE `MOVT` record:

    variable                 initial value   movement type
    iState_DeerDefault                  20   Deer_Default_MT
    iState_DeerDefaultRun               21   Deer_DefaultRun_MT
    iState_NPCBowDrawn                   3   NPC_BowDrawn_MT
    iState_GiantCombatRun                2   GiantCombatRun_MT

Read them from `hkbBehaviorGraphData.m_stringData.m_variableNames`, paired **by
index** with `m_variableInitialValues.m_wordVariableValues`.

#### Scope: the root graph, not the closure

A project reaches many graphs; only its root one governs. Keys against declarations,
all 49 projects, the Falmer's two corrupt keys (§7) excluded:

    scope                     keys == declared   keys subset   violations
    root behaviour graph                    24            25            0
    full reference closure                  21            28            0

Both are violation-free, so a key is always a declared state id. The root graph is
the tighter statement and is the rule; **scope a generator to it.**

The difference is not academic. `quadrupedbehavior.hkx` is a shared template holding
the union of all quadruped constants, reachable from eleven projects, and it
disagrees with two of them:

    declaration                quadrupedbehavior.hkx   project root graph   key
    iState_DeerDefault                            10   deerbehavior     20   20
    iState_HorkerSwimDefault                      50   horkerbehavior   51    -

Under closure scope the deer has both 10 and 20 and the horker has 50 twice. The
cache follows the root graph: the deer is keyed 20 and 21, never 10. The template's
two colliding constants are inert.

#### Reaching the root graph

The project file is `hkbProjectData`, not a character:

    <Project>.hkx  hkbProjectStringData.m_characterFilenames  ->
    character      hkbCharacterStringData.m_behaviorFilename  ->  ROOT GRAPH
                   hkbBehaviorReferenceGenerator.m_behaviorName -> sub-graphs

Resolve each path against the project file's own directory, case-insensitively.
`m_behaviorFilenames` on the project holds the root only; every other graph is
reached through `hkbBehaviorReferenceGenerator`, so a traversal that reads the
project list alone sees one graph per project and misses the sub-behaviours.

Two traps, each of which silently empties the result:

- Havok members are modelled as **auto-properties** in HKX2. Reflecting over
  `GetFields()` descends into nothing; use the properties.
- the character file and its graphs are **siblings**, not nested
  (`characters/defaultmale.hkx` against `behaviors/0_master.hkx`), so matching
  graphs by path prefix under the character's directory finds none for the seven
  projects shaped that way.

#### Species slot

The declared numbers are not arbitrary. The eleven actors sharing
`quadrupedbehavior.hkx` each hold a slot of ten, in **alphabetical order**:

    #   species     key      #   species     key
    0   Bear          0      6   Horse        60
    1   Cow          10      7   Mammoth      70
    2   Deer         20      8   SabreCat     80
    3   Dog          30      9   Skeever      90
    4   Goat         40     10   Wolf        100
    5   Horker       50

`key = 10 x alphabetical index`, exact for all eleven, with the offset within a slot
distinguishing gaits (deer 20 walk/trot, 21 run; horse 60 default, 61 sprint, 62
fall, 63 swim). A shared template needs a way to name its own species' table and
this is it. The horse is in the numbering though its root graph is
`horsebehavior.hkx`.

**`BoarProject` shows the scheme is static.** The boar (Dragonborn) would take slot
10 if inserted alphabetically, displacing Cow through Wolf. Its root graph declares
`iState_BoarDefault = 0` instead. The slots were fixed before the DLC and the late
species was not fitted in; nothing collides, because each project reads its own root
graph.

#### iState is engine-written, and still declared

The runtime value comes from game code: in every graph declaring `iState` the sole
binding is `BSSpeedSamplerModifier.state` — the modifier's own input — and **no state
machine syncs to it**:

    file                     iState vars   machines syncing to it
    0_master.hkx                      94                        0
    giantbehavior.hkx                 37                        0
    quadrupedbehavior.hkx             45                        0
    draugrbehavior.hkx                33                        0
    ... 16 graphs checked, none

The mechanism exists and is used elsewhere — `hkbStateMachine.m_syncVariableIndex`
writes a machine's state id into a variable, as for `iSyncDefaultState`,
`iSyncSprintState`, `currentDefaultState`, `iIsInSneak` and `iCrossbowState`.
`iState` is not among them.

These two facts are not in conflict, and the distinction matters. The value `iState`
*takes* at runtime is engine-side, which is why matching key sets against state
machine ids finds nothing:

    DefaultMale     keys [0..10,15,16,17]   0 of 5 machines match
    DraugrProject   keys [0,3,4,5,6,7]      0 exact; nearest [0,1,3,4,5,6,7,9]
    GiantProject    keys [0,1,2]            1 exact -- BleedOutBehavior, whose
                                            states are BleedOut_Start/Idle/Getup

But the set of values it *can* take is enumerated, once per movement type, as the
initial values of the `iState_*` variables. The key set is therefore recoverable
from the shipped files, and a generator need not be told it.

#### Resolution to MOVT

104 distinct `iState_*` declarations are reachable from the 49 projects; 103 name a
`MOVT` record that exists. All 86 populated entries resolve. The full join is §11.

Five naming disagreements between variable and record must be handled; without them a
match on normalised names alone scores 93 of 104:

    DLC prefix on the record, never on the variable
        iState_NetchDefault      -> DLC2Netch_Default_MT
    word order reversed
        iState_DefaultChaurus_MT -> ChaurusDefault_MT
    Swim/SwimDefault on the variable is Swimming on the record
        iState_BearSwimDefault   -> Bear_Swimming_MT
    one variable abbreviated
        iState_ChaFlyerDefault   -> ChaurusFlyer_Default_MT
    one record misspelt in the shipped data
        iState_CowSiwmDefault    -> CowSwimDefault_MT

Do not substitute a token-set or camelCase matcher for these. Consecutive capitals
(`NPCDefault`) do not split, so such a matcher scores **worse** than plain
normalisation, not better:

    matcher                                      resolved
    token-set / camelCase split                  75 / 104
    normalised names only                        93 / 104
    normalised + the five rules above           103 / 104

**`iState_CombatSpider_MT` is a dangling reference.** Both spider root graphs declare
it at state 1, and no such `MOVT` exists — the only spider records are
`DwarvenSpider_Default_MT` and `SpiderDefault_MT`. It is never a key, so it costs
nothing; a resolver must tolerate a declaration the ESM does not back.

Not every declared state is sampled. 25 of the 49 projects hold fewer entries than
they declare states — the dragon declares Flying, Hovering and Perching and ships
only state 0 — so a state count does not give an entry count.

#### Reading a state's clip set

A key is not a state machine id, so a key does not index a state machine's subtree.
The route from an entry to the root motion behind it runs through the consumer
instead: find the `hkbBlenderGenerator` whose `m_blendParameter` is bound to
`SpeedSampled` (§6.1), and take its children. Each child gives an authored weight
and an animation; the animation's travel and duration give a speed. That ladder is
what §4.4 evaluates.

Clip rate comes from the animation cache `ClipGeneratorEntry.PlaybackSpeed`, not
from `hkbClipGenerator.m_playbackSpeed` alone.

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
selects, times that clip generator's `PlaybackSpeed`. Curve shape is §5.1; where
the value goes is §6.

`y` is bounded by what the project's clips can deliver (I9). The playback factor is
required for the bound to hold, not a refinement:

    bound                                        exceeded by   hit exactly
    fastest clip root-motion speed                    4 / 46             8
    that x ClipGeneratorEntry.PlaybackSpeed           0 / 46             9

#### The y bounds are the blend ladder's endpoints

Root motion carries a travel and a duration, so a clip has a speed. Order the
`SpeedSampled` blender's children by weight and pair each with its animation's
`travel / duration x PlaybackSpeed`. That ladder reproduces both bounds of the
entry. Measured on the 13 entries whose locomotion blender is identifiable:

    entry              min y  slowest child     max y  ladder at max x   max x
    Chicken:0           0.49           0.49    403.10           403.10   324.5
    Hare:0              3.64           3.64    320.62           320.62   324.5
    Bear:0              3.59           3.59    285.23           285.23   324.5
    Dog:30              4.99           4.99    288.73           288.95   424.5
    Wolf:100            4.99           4.99    288.73           288.95   424.5
    HighlandCow:10      4.93           4.93    324.25           324.27   324.5
    Deer:20             4.92           4.92    431.92           449.50   449.5
    Deer:21           391.50         416.67    832.25           832.83   832.5
    Goat:40             4.97           4.97    324.49           324.50   324.5
    Horker:50           3.31           3.31     83.41            83.41   324.5
    Mammoth:70          2.50           2.50    290.34           324.50   324.5
    SabreCat:80         4.98           4.98    288.79           324.50   324.5
    Skeever:90          5.15           5.15    308.37           325.24   324.5

**`min y` is the root-motion speed of the blend's slowest child** — 12 of 13, to
better than 0.5% and usually to the stored decimals. It is not a floor or an
epsilon: it is what the actor still travels at when the requested speed is 0.

**`max y` is that ladder evaluated at `max x`** — 9 of 13, and the split is clean:

    max x reaches the top rung   4 / 4    max y is exactly the top clip's speed
    max x lands mid-ladder       5 / 9    linear interpolation of the two speeds

The four mid-ladder misses fall short of linear interpolation, never over: deer 20
-3.9%, skeever -5.2%, mammoth -10.5%, sabrecat -11.0%.

#### y follows the clip, not the weight

Where the authored weight and the animation disagree (§6.3), the table records the
animation:

    entry        top weight   top clip's speed   table max y
    Chicken:0        251.94             403.10        403.10
    Dog:30           425.00             289.30        288.73

The chicken's blend claims its fastest gait runs at 251.94 and the clip travels at
403.10; asking for 324.5 delivers 403.10. **That is why `max y` can exceed `max x`**
— on 24 of the 86 populated entries it exceeds every `MOVT` translation speed in its
own row (§11) — and it is the reason the file has to exist: the engine knows the
request, the blend knows the claim, and only this table knows the delivery.

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

The sampled states come from the root graph's `iState_*` declarations (§4.1).
`top(s)`, the retention rule and the evaluator are inputs, not derivable: §5.4.

Output must satisfy I1-I9 (§3). x is in the units RACE `MOVT` records use.

### 5.4 Missing inputs

§2 and §4 suffice to read and rewrite a table. Producing one needs three further
inputs. The key set was a fourth and is now derivable.

    #  input                        shape              status
    1  behaviour graph evaluator    code               blocker; endpoints derivable
    2  top(s), sweep upper bound    1 float per entry  authored; default 324.5
    3  point-retention rule         1 algorithm        closed experiment
    -  set of sampled states        list of state ids  DERIVABLE, §4.1

**1 — Evaluator.** The inner loop is "step the graph, read the resulting locomotion
speed". The interior of a curve is that measurement and has no closed form (§5.2).
Requires driving `hkbBehaviorGraph` far enough to resolve which animation plays at a
given `(state, direction, speed)` and at what rate.

Its **endpoints no longer need it.** `min y` is the slowest blend child's root-motion
speed and `max y` is the ladder evaluated at `top(s)` — 12 of 13 and 9 of 13 on the
measurable entries (§4.4). So the evaluator is needed for the interior points, not
for the range.

**2 — Sweep upper bound.** Exposed only as `max(x)`, distribution in §3, and it
bounds the sweep at both ends: `min(x)` is 0 on 84 of the 86 populated entries and
has an authored floor on two (§11).

The seven non-default values were searched against `MOVT` speeds, race records, every
numeric field of both, clip root motion, root motion times playback rate, clip travel
distances, durations, behaviour-graph float literals, and a regression on each state's
own animations. Every test is enriched on the twelve non-default entries and at
chance across all 86 — a large candidate pool, not a mechanism.

One lead outranks those, and it is the first to predict a non-default value from a
shipped file. `max(x) = V - 0.5` for `V` in {190, 325, 415, 425, 450, 750, 833, 1000},
and for three entries `V` is exactly the top weight of the `SpeedSampled` blender:

    entry       top blend weight    V    max x
    Dog:30                425.00  425    424.5
    Wolf:100              425.00  425    424.5
    Deer:21               833.00  833    832.5

It fails on the other ten, where `V` is the 325 default while the top weight is
anything from 83.41 to 638.07. **3 of 13 is a lead, not a rule.** Do not implement it.

**3 — Retention rule.** Unknown, and the only gap with a checkable answer. Given y
on the full grid, candidates (Douglas-Peucker at a tolerance, curvature threshold,
error-bounded decimation) are scored against the shipped file by whether they
reproduce its exact point sets in all 1,634 records.

With those three, the rest follows: keys from §4.1, direction values from I4, grid and
shared ceiling from I6-I8, output range from §4.4 and I9.

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

## 6. Consumer

### 6.1 Output binding

The modifier's four parameters bind to named graph variables. Read from the
compiled graphs, not inferred:

    member       variable            
    state    <-  iState              engine-written, graph-declared (§4.1)
    direction<-  Direction           
    goalSpeed<-  Speed               
    speedOut ->  SpeedSampled        HorseSpeedSampled in horsebehavior.hkx

This is independent confirmation of §4: the record tag is `Direction`, the point x
is `Speed`, and the point y is what lands in `SpeedSampled`.

No node in the root graph reads `SpeedSampled`. The consumer sits in the referenced
sub-behaviour `forwardlocomotion.hkx`:

    hkbBlenderGenerator 'ForwardWalkBlend_Dog' . blendParameter  <- SpeedSampled
    hkbBlenderGenerator 'ForwardRunBlend_Dog'  . blendParameter  <- SpeedSampled

Full path:

    engine --> iState, Direction, Speed
                  |
                  v
           BSSpeedSamplerModifier --query--> speeddatasinglefile.txt
                  |
                  v
              SpeedSampled                  (= the table's y)
                  |
                  v
           hkbBlenderGenerator.blendParameter
                  |
                  v
              gait mix

### 6.2 Two families of blender

A locomotion sub-behaviour holds two kinds of `hkbBlenderGenerator`, distinguished
by which variable drives them and by the sign pattern of their child weights.
`forwardlocomotion.hkx` for the deer:

    kind    generator                      blendParameter      child weights
    SPEED   ForwardLocomotionBlend_Deer    SpeedSampled        5, 169.8, 371.4, 557.1
    SPEED   RunForwardBlend                SpeedSampled        416.5, 833
    TURN    WalkSlowBlend_Deer             TurnDeltaDamped     90, 0, -90
    TURN    WalkBlend_Deer                 TurnDeltaDamped     90, 0, -90
    TURN    TrotBlend_Deer                 TurnDeltaDamped     135, 0, -135
    TURN    FastTrotBlend_Deer             TurnDeltaDamped     135, 0, -135
    TURN    SlowRunBlend_Deer              TurnDeltaDamped     270, 0, -270
    TURN    RunBlend_Deer                  TurnDeltaDamped     270, 0, -270

**`SpeedSampled` drives only the speed family.** The turn family takes
`TurnDeltaDamped`, an unrelated variable, and its weights are angles in degrees
with left/centre/right children. The two are independent axes:

    gait    speed position      turn authority
    walk         169.8               +/- 90
    trot         371.4               +/- 135
    run          833.0               +/- 270

Turn authority grows with gait. The sampler supplies the speed axis and nothing
else; direction of travel is the table's own `Direction` input, and turning is a
separate variable the table never sees.

### 6.3 Blend weights

A speed blender is parametric (`flags=17`): each child sits at the blend-parameter
value where it is fully active, so the weights are speeds in `SpeedSampled` units.
The intent is `clip root motion x PlaybackSpeed`, exactly as the table's y is
defined (§4.4):

    ForwardWalkBlend_Dog                     clip raw x playback
      weight   5.000   WalkForward @0.067     74.54 x 0.067 =   4.994
      weight  74.540   WalkForward @1         74.54 x 1     =  74.540   exact
      weight 104.356   WalkForward @1.4       74.54 x 1.4   = 104.356   exact

Measured over every blender in the game, one test per child against the best clip
under it:

    blender kind   children   exact (<0.01)   within 2%   combined
    speed                75        27 (36%)    19 (25%)     61.3%
    turn                 98         0            0           0.0%

The turn result is definitional, not a failure: those weights are angles.

The 29 speed-family deviations fall into three authored classes, none random:

    1  floor constant     the slowest child is pinned at exactly 5.000 while its
                          clip delivers 0.486 to 3.854 -- skeever, bear, horker,
                          mammoth, boar, chicken, hare

    2  playback omitted   weight equals the clip's raw speed while the generator
                          under it plays at another rate:
                            ChickenProject Forward_Blend   251.938, played at 1.6
                            BearProject    ForwardWalkBlend 59.820, played at 1.5

    3  RACE value used    weight equals the actor's MOVT ForwardRun rather than any
                          clip product: HareProject 244.444, BoarProject 638.070

The dog's trot children are internally consistent at 186.758 / 287.320 / 425.000,
being 0.65 / 1 / 1.5 times 287.32, so 287.32 is the authored trot speed; the
animation cache records 192.866 for `TrotForward`. Where the two disagree the
weight is authored and the cache is measured.

**Consequence.** Blend weights are hand-authored numbers expressing "the speed this
child delivers". They are usually `raw x playback` and are not reliably so. A tool
must not derive one from the other in either direction.

**Where they disagree, the table sides with the cache.** The chicken's top weight is
251.94 and its clip delivers 403.10; `max y` is 403.10. The dog's top weight is 425
and its trot delivers 289.30; `max y` is 288.73. So the weight is a claim and the
table is the measurement, which is what makes the table the correct index for the
blender rather than a redundant copy of its weights (§4.4, §6.4).

### 6.4 Why the table exists

A parametric blender must be indexed by a quantity its children are positioned on.
The clips sit at irregular speeds — the dog's walk family at 5, 74.5, 104.4, 186.8,
287.3, 425 — so interpolating on the raw request would give the wrong gait mix
wherever the request and the achievable speed diverge, which is everywhere outside
the anchors.

The table converts request into achievable speed. That is the same quantity the
blender's children are placed on, so the blend parameter is dimensionally correct
by construction. It also explains the output bound (I9): above the fastest clip
there is no further child to blend toward, so `SpeedSampled` pins and the curve
saturates.

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

Format and semantics are settled. Three generator inputs are not: see §5.4. A table
can be read, rewritten and validated from the shipped files, and its keys and its y
range can be derived from them (§4.1, §4.4); the interior of each curve still cannot,
because it is the output of a graph evaluation.

Two measured facts are unexplained rather than merely unimplemented:

- `max y` falls 4-11% below the blend ladder's linear interpolation on 4 of the 13
  measurable entries. Synchronised blending does not account for it: all 13 blenders
  carry identical flags (`SYNC | PARAMETRIC_CYCLIC`, no sync master). The untested
  candidate is that each rung is itself a sub-blend of straight and turning variants.
- `DeerProject` key 21 has `min y` 391.50 against a slowest child of 416.67, sitting
  below the bottom rung rather than clamping to it. It is also the entry with the
  largest authored `min x` (400).

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
the modifier passes `goalSpeed` through unchanged (§1.2), so the gait blender is
indexed by the requested speed instead of the achievable one (§6.4). The failure
mode is a wrong gait mix and foot sliding, not a crash.

A project is matched to the RACE records that use it by the stem of the race's
`BehaviorGraph` path: `Actors\Deer\DeerProject.hkx` names `DeerProject`. This
covers 47 of the 49 projects; `DefaultFemale` and `FirstPerson` are not named by
any race, which points its graph field at `Actors\Character\DefaultMale.hkx`.

For movement types the race is not needed and should not be used: the project's own
root graph names them (§4.1), which covers 49 of 49 and distinguishes states within a
project as a race link cannot. Resolving through the race also picks the wrong record
for the player, whose highest `ForwardRun` is `NPC_Horse_MT` — the mounted type, at
double the on-foot speed.

`MESHES/SPEEDDATA/<...>.SPD` (§1.3) is a supported per-project load path that the
game ships nothing for. A tool that adds one creature can write a single `.SPD`
rather than rewriting the merged file.

Implementing §2 is sufficient to read and rewrite the file losslessly; §4 is
sufficient to interpret it; §5.4 is what stands between that and generating one.

## 11. Entry census

Every entry in the file, against the movement type its key resolves to. One row per
entry, in file order.

The movement type is resolved by the rule of §4.1: the key is a state id declared by
an `iState_<MOVT>` variable in the project's **root** behaviour graph, and the
variable's suffix names a `MOVT` record. 86 of the 88 entries resolve; the two that
do not are the Falmer's corrupt keys (§7), which carry no points and so have no
bounds either.

Speeds are the `MOVT` translation speeds in game units/s. The three rotation fields
(`RotateInPlaceWalk`, `RotateInPlaceRun`, `RotateWhileMovingRun`) are degrees/s and
are omitted: they are not commensurable with `x`, and mixing them into a speed
comparison is what made an earlier scale-factor search score below its own control.

Reading the bounds columns:

- **`max x`** takes 8 distinct values across the file (§3) and is shared by all 19
  records of an entry (I8). It is authored; §5.4 lists it as an unresolved input.
- **`min x` is 0 on 84 of the 86 populated entries.** The exceptions are both run
  states with a floor below which the sampler is never asked: `DeerProject` 21
  (`Deer_DefaultRun_MT`) starts at 400, `GiantProject` 2 (`GiantCombatRun_MT`) at
  150. So the sweep has an authored lower bound as well as an upper one.
- **`min y` is above 0 on 82 of the 86** — a standing actor still has the residual
  speed of its idle. Of the four that reach 0, three are zero throughout:
  `AtronachStormProject`, `WispProject` and `WitchlightProject` return 0 for every
  direction and every speed. All three hover, and their locomotion carries no root
  motion, so there is nothing to report; a generator must reproduce the all-zero
  curve rather than treat it as a gap.
- **`max y` is bounded by what the project's clips can deliver** (I9), not by any
  `MOVT` field. On 24 of the 86 it exceeds every translation speed in its own row,
  which is why `MOVT` is not the y axis: see §4.4.

| character | state | movement type | min x | max x | min y | max y | fwd walk | fwd run | back walk | back run | left walk | left run | right walk | right run |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ChickenProject | 0 | `Chicken_Default_MT` | 0 | 324.50 | 0.49 | 403.10 | 34.71 | 251.94 | 0 | 0 | 0 | 0 | 0 | 0 |
| HareProject | 0 | `Hare_Default_MT` | 0 | 324.50 | 3.64 | 320.62 | 89.18 | 244.44 | 0 | 77.84 | 0 | 0 | 0 | 0 |
| AtronachFlame | 1 | `AtronachFlame_Default` | 0 | 324.50 | 7.77 | 499.50 | 112.50 | 500 | 112.50 | 500 | 112.50 | 500 | 112.50 | 500 |
| AtronachFrostProject | 0 | `AtronachFrost_Default_MT` | 0 | 324.50 | 3.56 | 324.05 | 70.88 | 325.38 | 57.49 | 194.82 | 69.07 | 277.46 | 71.46 | 275.36 |
| AtronachStormProject | 0 | `AtronachStorm_Default` | 0 | 324.50 | 0 | 0 | 100 | 500 | 100 | 500 | 100 | 500 | 100 | 500 |
| BearProject | 0 | `Bear_Default_MT` | 0 | 324.50 | 3.59 | 285.23 | 59.82 | 638.07 | 51.89 | 51.89 | 0 | 0 | 0 | 0 |
| DogProject | 30 | `Dog_Default_MT` | 0 | 424.50 | 4.99 | 288.73 | 74.54 | 500.14 | 74.54 | 74.54 | 0 | 0 | 0 | 0 |
| WolfProject | 100 | `Wolf_Default_MT` | 0 | 424.50 | 4.99 | 288.73 | 74.54 | 555.56 | 74.54 | 74.54 | 0 | 0 | 0 | 0 |
| DefaultFemale | 0 | `NPC_Default_MT` | 0 | 324.50 | 7.55 | 307.02 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 1 | `NPC_Sprinting_MT` | 0 | 324.50 | 370.37 | 370.37 | 500 | 500 | 270.84 | 270.84 | 0 | 0 | 0 | 0 |
| DefaultFemale | 2 | `NPC_Sneaking_MT` | 0 | 324.50 | 3.91 | 132.89 | 47.20 | 222 | 43.38 | 150 | 41.44 | 200 | 41.44 | 200 |
| DefaultFemale | 3 | `NPC_BowDrawn_MT` | 0 | 999.50 | 6.35 | 155.21 | 120 | 135 | 65.11 | 98 | 76.81 | 115 | 74.89 | 115 |
| DefaultFemale | 4 | `NPC_Blocking_MT` | 0 | 324.50 | 3.80 | 321.03 | 81 | 81 | 71 | 71 | 81 | 81 | 81 | 81 |
| DefaultFemale | 5 | `NPC_Bleedout_MT` | 0 | 324.50 | 12.74 | 22.56 | 20.11 | 20.11 | 16.49 | 16.49 | 22.01 | 22.01 | 17.59 | 17.59 |
| DefaultFemale | 6 | `NPC_1HM_MT` | 0 | 324.50 | 3.80 | 321.03 | 80.10 | 370 | 45.45 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 7 | `NPC_2HM_MT` | 0 | 324.50 | 4.20 | 321.10 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 8 | `NPC_Bow_MT` | 0 | 324.50 | 4.21 | 320.99 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 9 | `NPC_Magic_MT` | 0 | 324.50 | 3.80 | 320.84 | 80.10 | 370 | 71.93 | 170.84 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 10 | `NPC_MagicCasting_MT` | 0 | 749.50 | 3.80 | 395.94 | 80.10 | 370 | 71.93 | 170.84 | 80.09 | 370 | 79.75 | 370 |
| DefaultFemale | 15 | `NPC_Drunk_MT` | 0 | 324.50 | 29.47 | 29.47 | 29.47 | 29.47 | 0 | 0 | 0 | 0 | 0 | 0 |
| DefaultFemale | 16 | `NPC_BowDrawn_QuickShot_MT` | 0 | 999.50 | 6.35 | 155.21 | 120 | 370 | 65.11 | 205.25 | 76.81 | 370 | 74.89 | 370 |
| DefaultFemale | 17 | `NPC_Blocking_ShieldCharge_MT` | 0 | 324.50 | 3.80 | 321.03 | 81 | 370 | 71 | 205.25 | 81 | 370 | 81 | 370 |
| DefaultMale | 0 | `NPC_Default_MT` | 0 | 324.50 | 7.17 | 307.96 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 1 | `NPC_Sprinting_MT` | 0 | 324.50 | 370.37 | 370.37 | 500 | 500 | 270.84 | 270.84 | 0 | 0 | 0 | 0 |
| DefaultMale | 2 | `NPC_Sneaking_MT` | 0 | 324.50 | 3.91 | 132.89 | 47.20 | 222 | 43.38 | 150 | 41.44 | 200 | 41.44 | 200 |
| DefaultMale | 3 | `NPC_BowDrawn_MT` | 0 | 999.50 | 6.35 | 155.21 | 120 | 135 | 65.11 | 98 | 76.81 | 115 | 74.89 | 115 |
| DefaultMale | 4 | `NPC_Blocking_MT` | 0 | 324.50 | 3.80 | 321.03 | 81 | 81 | 71 | 71 | 81 | 81 | 81 | 81 |
| DefaultMale | 5 | `NPC_Bleedout_MT` | 0 | 324.50 | 12.74 | 22.56 | 20.11 | 20.11 | 16.49 | 16.49 | 22.01 | 22.01 | 17.59 | 17.59 |
| DefaultMale | 6 | `NPC_1HM_MT` | 0 | 324.50 | 3.80 | 321.03 | 80.10 | 370 | 45.45 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 7 | `NPC_2HM_MT` | 0 | 324.50 | 4.20 | 321.10 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 8 | `NPC_Bow_MT` | 0 | 324.50 | 4.21 | 320.99 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 9 | `NPC_Magic_MT` | 0 | 324.50 | 3.80 | 320.84 | 80.10 | 370 | 71.93 | 170.84 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 10 | `NPC_MagicCasting_MT` | 0 | 749.50 | 3.80 | 395.94 | 80.10 | 370 | 71.93 | 170.84 | 80.09 | 370 | 79.75 | 370 |
| DefaultMale | 15 | `NPC_Drunk_MT` | 0 | 324.50 | 29.47 | 29.47 | 29.47 | 29.47 | 0 | 0 | 0 | 0 | 0 | 0 |
| DefaultMale | 16 | `NPC_BowDrawn_QuickShot_MT` | 0 | 999.50 | 6.35 | 155.21 | 120 | 370 | 65.11 | 205.25 | 76.81 | 370 | 74.89 | 370 |
| DefaultMale | 17 | `NPC_Blocking_ShieldCharge_MT` | 0 | 324.50 | 3.80 | 321.03 | 81 | 370 | 71 | 205.25 | 81 | 370 | 81 | 370 |
| FirstPerson | 0 | `NPC_Default_MT` | 0 | 324.50 | 8.25 | 237.14 | 80.10 | 370 | 71.93 | 205.25 | 80.09 | 370 | 79.75 | 370 |
| ChaurusProject | 0 | `ChaurusDefault_MT` | 0 | 324.50 | 3.54 | 324.71 | 85.51 | 350.27 | 95.09 | 95.09 | 87.53 | 350 | 87.53 | 350 |
| HighlandCowProject | 10 | `Cow_Default_MT` | 0 | 324.50 | 4.93 | 324.25 | 65 | 525.45 | 77.84 | 77.84 | 0 | 0 | 0 | 0 |
| DeerProject | 20 | `Deer_Default_MT` | 0 | 449.50 | 4.92 | 431.92 | 169.80 | 833 | 123.80 | 123.80 | 0 | 0 | 0 | 0 |
| DeerProject | 21 | `Deer_DefaultRun_MT` | 400 | 832.50 | 391.50 | 832.25 | 169.80 | 833 | 123.80 | 123.80 | 0 | 0 | 0 | 0 |
| ChaurusFlyer | 0 | `ChaurusFlyer_Default_MT` | 0 | 324.50 | 0 | 0 | 150 | 725 | 99 | 710.50 | 95 | 725 | 95 | 0 |
| VampireBruteProject | 0 | `GargoyleDefault_MT` | 0 | 324.50 | 5.54 | 314.79 | 118.92 | 403.72 | 75.41 | 180.57 | 78.12 | 340 | 89.14 | 340 |
| BenthicLurkerProject | 0 | `BenthicLurkerDefault_MT` | 0 | 324.50 | 5 | 232.09 | 122.15 | 305.46 | 91.95 | 91.95 | 99.52 | 99.52 | 99.52 | 99.52 |
| BenthicLurkerProject | 1 | `BenthicLurkerCombatRun_MT` | 0 | 324.50 | 3 | 320.81 | 122.15 | 305.46 | 91.95 | 91.95 | 99.52 | 99.52 | 99.52 | 99.52 |
| BoarProject | 0 | `Boar_Default_MT` | 0 | 324.50 | 6.42 | 315.12 | 64.23 | 594.35 | 64.23 | 128 | 0 | 0 | 0 | 0 |
| BallistaCenturion | 0 | `DwarvenBallista_Default_MT` | 0 | 324.50 | 2.34 | 336.58 | 99.80 | 284.72 | 100.04 | 284.96 | 99.44 | 302.07 | 100.16 | 302.42 |
| HMDaedra | 0 | `HMDaedraDefault_MT` | 0 | 324.50 | 2.32 | 64 | 64 | 128 | 64 | 128 | 64 | 128 | 64 | 128 |
| NetchProject | 0 | `DLC2Netch_Default_MT` | 0 | 324.50 | 400 | 400 | 60 | 350 | 60 | 350 | 60 | 350 | 60 | 350 |
| RieklingProject | 0 | `DLC2Riekling_Default_MT` | 0 | 324.50 | 0.92 | 381.13 | 167.42 | 300 | 167.42 | 275.82 | 167.42 | 300 | 167.42 | 300 |
| ScribProject | 0 | `ScribDefault_MT` | 0 | 324.50 | 24.06 | 84.63 | 401.01 | 802.29 | 95.09 | 95.09 | 0 | 0 | 0 | 0 |
| DragonProject | 0 | `Dragon_Default_MT` | 0 | 324.50 | 381.18 | 384 | 384 | 384 | 384 | 384 | 0 | 0 | 0 | 0 |
| Dragon_Priest | 0 | `DragonPriest_Default_MT` | 0 | 324.50 | 55.29 | 300 | 80 | 300 | 80 | 300 | 80 | 300 | 80 | 300 |
| DraugrProject | 0 | `DraugrDefault_MT` | 0 | 324.50 | 4.63 | 312.89 | 76.97 | 339.24 | 70.09 | 70.09 | 76.97 | 339.24 | 76.97 | 339.24 |
| DraugrProject | 3 | `Draugr1HM_MT` | 0 | 324.50 | 4.61 | 312.77 | 92.25 | 345.37 | 70.09 | 70.09 | 92.25 | 345.37 | 92.25 | 345.37 |
| DraugrProject | 4 | `DraugrBattleAxe_MT` | 0 | 324.50 | 4.59 | 271.38 | 105.25 | 271.38 | 70.09 | 70.09 | 105.25 | 271.38 | 105.25 | 271.38 |
| DraugrProject | 5 | `DraugrGreatSword_MT` | 0 | 324.50 | 4.56 | 271.38 | 106.93 | 271.38 | 70.09 | 70.09 | 106.93 | 271.38 | 106.93 | 271.38 |
| DraugrProject | 6 | `DraugrH2H_MT` | 0 | 324.50 | 4.54 | 351.68 | 116.74 | 298.92 | 70.09 | 70.09 | 116.74 | 298.92 | 116.74 | 298.92 |
| DraugrProject | 7 | `DraugrBow_MT` | 0 | 324.50 | 4.57 | 312.89 | 76.97 | 339.22 | 70.09 | 70.09 | 76.97 | 339.22 | 76.97 | 339.22 |
| DraugrSkeletonProject | 0 | `DraugrDefault_MT` | 0 | 324.50 | 4.63 | 312.89 | 76.97 | 339.24 | 70.09 | 70.09 | 76.97 | 339.24 | 76.97 | 339.24 |
| SphereCenturion | 0 | `SphereDefault_MT` | 0 | 324.50 | 4.63 | 324.48 | 192 | 384 | 192 | 384 | 192 | 384 | 192 | 384 |
| DwarvenSpiderCenturionProject | 0 | `SpiderDefault_MT` | 0 | 324.50 | 1.80 | 162.81 | 85.09 | 300.20 | 90.09 | 299.67 | 90.09 | 154.21 | 90.09 | 154.21 |
| SteamProject | 0 | `SteamDefault_MT` | 0 | 324.50 | 4.63 | 192 | 96 | 192 | 96 | 192 | 96 | 192 | 96 | 192 |
| FalmerProject | 2147483648 | *corrupt* | — | — | — | — | — | — | — | — | — | — | — | — |
| FalmerProject | 1 | `FalmerBowDrawn_MT` | 0 | 324.50 | 3.99 | 100.46 | 100.44 | 100.44 | 81.55 | 81.55 | 77.12 | 77.12 | 90.73 | 90.73 |
| FalmerProject | 2 | `Falmer_1HM_Walk` | 0 | 324.50 | 4.62 | 298.61 | 100.44 | 175.77 | 81.55 | 142.71 | 77.12 | 134.96 | 90.73 | 158.78 |
| FalmerProject | 1651406194 | *corrupt* | — | — | — | — | — | — | — | — | — | — | — | — |
| FrostbiteSpiderProject | 0 | `SpiderDefault_MT` | 0 | 324.50 | 4.83 | 300.20 | 85.09 | 300.20 | 90.09 | 299.67 | 90.09 | 154.21 | 90.09 | 154.21 |
| GiantProject | 0 | `GiantDefault_MT` | 0 | 324.50 | 4.59 | 123.10 | 61.84 | 61.84 | 54.43 | 54.43 | 66.14 | 66.14 | 64.92 | 64.92 |
| GiantProject | 1 | `GiantCombatWalk_MT` | 0 | 189.50 | 4.61 | 187.43 | 82.46 | 247.37 | 63.50 | 190.50 | 93.70 | 281.09 | 103.86 | 311.59 |
| GiantProject | 2 | `GiantCombatRun_MT` | 150 | 414.50 | 61.16 | 410.56 | 50 | 415 | 50 | 115.90 | 50 | 227.27 | 50 | 220.59 |
| GoatProject | 40 | `Goat_Default_MT` | 0 | 324.50 | 4.97 | 324.49 | 62.13 | 360 | 39.65 | 39.65 | 0 | 0 | 0 | 0 |
| HagravenProject | 0 | `Hagraven_Default_MT` | 0 | 324.50 | 3.99 | 116.29 | 73.79 | 116.24 | 73.79 | 102.87 | 73.79 | 116.24 | 73.79 | 116.24 |
| HorkerProject | 50 | `Horker_Default_MT` | 0 | 324.50 | 3.31 | 83.41 | 32.77 | 83.41 | 32.77 | 32.77 | 0 | 0 | 0 | 0 |
| HorseProject | 60 | `Horse_Default_MT` | 0 | 324.50 | 5 | 321.14 | 125.11 | 450 | 108.08 | 108.08 | 0 | 0 | 0 | 0 |
| IceWraithProject | 0 | `IceWraith_Default_MT` | 0 | 324.50 | 230.52 | 319.67 | 100 | 319.67 | 100 | 319.67 | 100 | 319.67 | 100 | 319.67 |
| MammothProject | 70 | `Mammoth_Default_MT` | 0 | 324.50 | 2.50 | 290.34 | 61.84 | 400 | 61.84 | 61.84 | 0 | 0 | 0 | 0 |
| MudcrabProject | 0 | `MCrab_Default_MT` | 0 | 324.50 | 2.79 | 149.65 | 72.74 | 145.87 | 72.74 | 145.87 | 72.74 | 145.87 | 72.74 | 145.87 |
| SabreCatProject | 80 | `SabreCat_Default_MT` | 0 | 324.50 | 4.98 | 288.79 | 113.09 | 563 | 66.79 | 66.79 | 0 | 0 | 0 | 0 |
| SkeeverProject | 90 | `Skeever_Default_MT` | 0 | 324.50 | 5.15 | 308.37 | 36.14 | 486.23 | 36.14 | 36.14 | 0 | 0 | 0 | 0 |
| SlaughterfishProject | 0 | `SlaughterfishSwim_MT` | 0 | 324.50 | 0.58 | 302.23 | 180 | 360 | 15 | 0 | 0 | 0 | 0 | 0 |
| Spriggan | 0 | `Spriggan_Default` | 0 | 324.50 | 4.64 | 310.24 | 65.32 | 358.87 | 57.73 | 214.02 | 90.57 | 358.87 | 90.57 | 358.87 |
| Spriggan | 1 | `Spriggan_Combat` | 0 | 324.50 | 3.74 | 297.51 | 65.32 | 358.87 | 57.73 | 214.02 | 67.93 | 358.87 | 67.93 | 358.87 |
| TrollProject | 0 | `TrollDefault_MT` | 0 | 324.50 | 4.50 | 269.50 | 102.70 | 269.50 | 83.11 | 83.11 | 102.70 | 269.50 | 102.70 | 269.50 |
| VampireLord | 0 | `VampireLordDefault_MT` | 0 | 324.50 | 5.46 | 350.87 | 70 | 400 | 70 | 400 | 70 | 400 | 70 | 400 |
| WerewolfBeastProject | 0 | `WerewolfBeastDefault_MT` | 0 | 324.50 | 4.65 | 303.06 | 70 | 400 | 70 | 400 | 70 | 400 | 70 | 400 |
| WispProject | 0 | `Wisp_Default_MT` | 0 | 324.50 | 0 | 0 | 100 | 300 | 100 | 300 | 100 | 300 | 100 | 300 |
| WitchlightProject | 0 | `Witchlight_Default_MT` | 0 | 324.50 | 0 | 0 | 500 | 500 | 500 | 500 | 500 | 500 | 500 | 500 |
