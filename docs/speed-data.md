# speeddatasinglefile.txt — speed sampler database

    Status:    format CONFIRMED (byte-exact round trip); semantics CONFIRMED
               key set derivable from the behaviour graph (§4.1)
               y derivable in closed form from clip root motion (§5.1.1)
               no graph evaluator required; one input authored (§5.4)
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

The graph's `Direction` variable, range [0,1]. A compass, and the sides are now
pinned: **0.00 ahead, 0.25 right, 0.50 behind, 0.75 left.**

The convention is fixed by the four cardinal records of the player's bow-drawn states
against the eight directional blenders (§6.2), each of which matches its own blender's
top child exactly:

    record       max y     blender                 weight    clip
    0.00        155.21     Bow_ForwardBlend        155.21  155.21
    0.25        131.06     Bow_RightBlend          130.04  131.06
    0.50        113.94     Bow_BackwardBlend       113.93  113.94
    0.75        134.43     Bow_LeftBlend           134.43  134.43

Note 0.25 and 0.50 track the clip and not the weight, per §4.4.

The 19 records sample at 0.05 while the eight blenders sit at 0.125 spacing, so only
records 0, 5, 10 and 15 read a single blender; the rest are mixtures of two adjacent
ones. That is why the direction profile is not monotonic — 0.20 gives 126.27 and 0.25
gives 131.06, because 0.20 is a forward/right mixture and 0.25 is pure right.

Curves are near mirror-symmetric about 0.5. Over the 688 pairs (0.10,0.90) (0.15,0.85)
... (0.45,0.55): 127 bit-identical, 499 within 2%, 62 differ — **91.0% mirrored**.
The player's run state is exact:

    direction 0.40 -> 272.96      direction 0.60 -> 272.96
    direction 0.45 -> 259.44      direction 0.55 -> 259.44

**The symmetry cannot be exact, and the residual is not noise.** `MOVT` is symmetric
left/right — `LeftRun == RightRun` on 54 of 63 testable entries — but the animations
are not. `Bow_LeftBlend` tops at 134.43 against `Bow_RightBlend`'s 130.04, a 3.4%
difference from the animators' own clips, and the table reproduces it faithfully. Six
entries carry a left/right ceiling gap above 1% despite symmetric `MOVT`, the riekling
by 18%. Do not symmetrise a table on the assumption that the difference is error.

### 4.3 x — goal speed

The query signature (§1.2) passes `(i32 state, f32 direction, f32 goalSpeed)` and
returns one float. The first two select entry and record, so `goalSpeed` indexes
within a record and the return is the paired `y`.

Units are game units/s, the same axis as RACE `MOVT`. x is the **position** axis of the
consuming blender, and it is on the `MOVT` scale because the rung positions are `MOVT`
values: §6.3.

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

**`max y` is that ladder evaluated at `max x`.** Where `max x` reaches the top rung it
is exactly the top clip's delivered speed, 4 of 4. Where `max x` lands mid-ladder, a
linear chord between the two bracketing speeds gets 5 of 9 and falls short on the other
four — deer 20 by 3.9%, skeever 5.2%, mammoth 10.5%, sabrecat 11.0%, always short and
never over. That is the chord error: the true segment is a hyperbola lying below it, and
the ratio form of §5.1.1 predicts all four to within 0.33%. Use §5.1.1, not a chord.

#### y follows the clip, not the weight

`y` is the **content** axis: what the clips deliver, not what the blend rungs claim.
Where the two disagree the table records the delivery, which is §6.3 and is the reason
the file exists.

One consequence is local to `y` and worth stating here: **`max y` may exceed `max x`.**
On 24 of the 86 populated entries it exceeds every `MOVT` translation speed in its own
row (§11). The chicken's top rung claims 251.94 while its `runforward` delivers 403.10,
so asking for 324.5 returns 403.10. A reader must not treat `y <= x` as an invariant.

## 5. Construction

### 5.1 Data model

A record is the locomotion response of one `(state, direction)` pair.

    x   position          the blend rung axis; what the game requests; game units/s
                          on the MOVT scale; 0.5 grid                        (§6.3)
    y   content           what the clips deliver there: travel / (duration /
                          PlaybackSpeed), blended                            (§6.3)

The graph selects from a fixed clip set and blends adjacent clips, so the response
is piecewise: pinned to one clip below the slowest, **hyperbolic** between clips
(§5.1.1), saturated above the fastest. The record stores the response, not the clip
set behind it.

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
251.94 x 1.6 = 403.104. Interior points are blends and match no clip individually;
§5.1.1 computes them.

**A repeated final pair is not a saturation marker.** 1162 of the 1634 records end
with the last point written twice, at the same x — a terminal duplicate, present
whether or not the curve has flattened. Saturation is a plateau between two
*distinct* x, as in the chicken's `252.5 -> 324.5` above (which has no duplicate).
Measured with the duplicate removed, 566 records end on a real plateau and **1068 are
still rising at the final sample**, so most records are cut before the response
flattens. A reader that tests `y[-1] == y[-2]` detects the duplicate, not the plateau.

`y` is unordered with respect to `x`. It exceeds `x` where the selected clip plays
above its authored rate, and by large factors where the actor's slowest locomotion
clip is fast:

    DragonProject          x   3.0  ->  y 384.00
    Dragon_Priest          x   1.0  ->  y  80.00
    SlaughterfishProject   x   1.0  ->  y 162.06

#### 5.1.1 Closed form for y

**y has a closed form. No graph evaluation is required.**

The consumer is an `hkbBlenderGenerator` carrying `FLAG_SYNC | FLAG_PARAMETRIC_CYCLIC`
(§6.2). Under sync, Havok interpolates the children's root-motion **translation** and
their **duration** as two independent linear ramps, and the resulting speed is their
quotient. Order the children by weight into a ladder of rungs

    rung i  =  (w_i, travel_i, dur_i)          dur_i = clip duration / PlaybackSpeed

then for x between rungs a and b

    u     = (x - w_a) / (w_b - w_a)
    y(x)  = (travel_a + u*(travel_b - travel_a)) / (dur_a + u*(dur_b - dur_a))

clamped to `travel/dur` of the first rung below `w_0` and of the last rung above
`w_n`. A quotient of two linear functions is a Mobius transform, so each segment is a
hyperbolic arc, not a chord. Where two adjacent rungs differ greatly in duration the
arc is strongly convex, which is why the curves read as acceleration ramps.

The hare's forward ladder shows the size of the effect. Its slowest rung plays
`walkforward` at `PlaybackSpeed` 0.058, stretching 0.833 s to 14.368 s:

    rung   weight   travel      dur     speed
       1     5.00    52.26   14.368      3.64
       2    89.18    52.26    0.833     62.71
       3   244.44   100.19    0.3125   320.62

    x        observed     this form   linear chord
    63.5        10.52         10.53          44.69
    79.0        21.10         21.15          55.56
    85.0        34.56         34.70          59.77
    88.0        50.75         51.06          61.88

Measured over the whole file, on the four cardinal records of every entry, selecting
the blender by the state's family and the record's compass direction with no fitting:

    records where family + direction leave exactly one candidate    122
      median relative error < 1%                                    105
    all deterministically selected records                          173
      median relative error < 1%                                    114
      median of the per-record medians                            0.13%

The 68 misses are blender **identification**, not the form: they concentrate on
`NPCBleedout` and `NPCDrunk`, whose movement-type names carry no family token, so no
group can be chosen for them. Against a linear chord the same records score a median
of roughly 10-25%, two orders of magnitude worse.

**Scope.** Verified on the four cardinal directions. The 15 intermediate directions
sample between two adjacent compass blenders — the eight sit at 0.125 spacing, the
records at 0.05 — and need a two-blender mixture that is not yet measured. Quadruped
side and back records have no directional family at all (§6.2) and are also
unverified: 171 of the 344 cardinal records could not be assigned a blender, most of
them for that reason.

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

No point is redundant and spacing follows the output axis: the retained points are
the vertices a piecewise-linear reader needs, which is what the consumer does between
them.

Point counts per record, terminal duplicate included:

    mean 11.2   median 11   min 2   max 121      quartiles 8 / 13, 90th 15

The distribution is bimodal. A main mode of 8-14 holds 1052 of the 1634 records, and a
separate spike at exactly **2 points** holds 180 — the degenerate curves, where y is
constant over the whole sweep. The three all-zero hover entries are 2 points in all 19
records; `RieklingProject` key 0 is the opposite extreme at 1037 points over its 19
records, up to 121 in one. Per entry: mean 213, median 206, range 38 to 1037.

Against a sweep of up to 650 grid positions, a median record retains about 1.7% of
them.

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

The inner loop does not need a graph step. `y` at any x is §5.1.1, evaluated from the
blender ladder; the sampled states come from the root graph's `iState_*` declarations
(§4.1); the direction values from I4. What remains as input is `top(s)` and the
retention rule: §5.4.

A generator that does not care about matching the shipped file byte for byte can skip
retention entirely and emit every grid point. That costs 1,206,044 points and about
9.7 MB against the shipped 18,302 points and 162,527 bytes — 59x — and is more
accurate, since the consumer then interpolates across half-unit steps rather than
across gaps of up to 1768 of them. Every tenth grid point is 6x and still exact to
well under one unit.

Output must satisfy I1-I9 (§3). x is in the units RACE `MOVT` records use.

### 5.4 Missing inputs

§2 and §4 suffice to read and rewrite a table. Producing one needs three further
inputs. The key set was a fourth and is now derivable.

    #  input                        shape              status
    1  top(s), sweep upper bound    1 float per entry  authored; default 325
    2  point-retention rule         1 algorithm        optional; see §5.3
    -  set of sampled states        list of state ids  DERIVABLE, §4.1
    -  y at any x                   closed form        DERIVABLE, §5.1.1
    -  behaviour graph evaluator    --                 NOT REQUIRED

**No evaluator is required.** Earlier revisions listed one as the blocker, on the
grounds that the curve interior is a measurement with no closed form. It has one
(§5.1.1): the blend is time-synchronised, so y is a quotient of two linear ramps, and
the inputs are the child weights from the graph plus each clip's travel and duration
from the animation cache. What still needs graph work is **identifying** which blender
serves a given `(state, direction)` — a traversal (§6.4), not a simulation.

**1 — Sweep upper bound.** Exposed only as `max(x)`, distribution in §3, and it
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

Pools searched and exhausted, so they are not searched again:

    pool                                                  325    650    the twelve V
    RACE, every numeric leaf to depth 4, 161 records     1 hit      -   already above
    RACE SpeedOverrides (only 10 races carry any)            -      -             -
    MOVT, all fields, 107 records                            -      -   3/12, mixed
    GMST, 665 settings                                       -   1 hit            -
    behaviour-graph float literals                           2      -   4/12 weights

The single 325 in RACE is `DLC2SprigganBurntRace.Starting[0].Value`, a starting actor
value; the single 650 in GMST is `fIronSightsDOFRange`, a Fallout leftover. Neither is
a movement quantity.

**`V = MOVT ForwardRun x {1, 1.5, 2}` is withdrawn.** It reached 8 of 12 at a 3.1%
control, but the 500 it leans on is `NPC_Horse_MT`/`NPC_Sprinting_MT` reached through
the project's whole movement-type pool rather than the state's own. Scored against the
movement type each state actually declares (§4.1) it falls to **2 of 12**, and both
survivors are plain equality with no multiplier. The multiplier was an artefact of the
loose pairing.

**`max(x)` does not bound what the game can request.** 47 of the 86 entries stop below
their own state's `ForwardRun` — the scrib sweeps to 324.5 against a movement type of
802.29 — and 37 of those are still rising when the sweep ends. Above the last point the
query clamps, so those actors are indexed below the speed they are asked for. `top(s)`
is how far somebody swept, not a speed the engine computes.

Nor is 325 derived: it is 650 half-unit steps, a grid length, applied unchanged to
creatures whose speeds span 61.84 to 802.29. Where the bound was changed it was changed
by hand and inconsistently — three entries set exactly to the blend ladder's top, two
raised but still truncating, five left at the default although their ladder runs past
it, and `GiantProject` state 1 *lowered* to 190 against a ladder reaching 247.37, which
no sampling-efficiency argument produces.

**2 — Retention rule.** Unknown, and needed only for a byte-identical rebuild
(§5.3). Given y on the full grid, candidates (Douglas-Peucker at a tolerance, curvature threshold,
error-bounded decimation) are scored against the shipped file by whether they
reproduce its exact point sets in all 1,634 records.

With `top(s)`, the rest follows: keys from §4.1, direction values from I4, grid from I6,
y from §5.1.1, and the retention rule only if the output must match byte for byte.

### 5.5 Authoring

The table is measured, so it is changed by changing what is measured (§6.3):

    input                              controls
    clip travel and duration           the content axis: what the actor delivers, in y
    ClipGeneratorEntry.PlaybackSpeed   scales content; the knob that aligns it to MOVT
    blend rung m_weight                the position axis, in x
    RACE MOVT                          what the game requests, and what the cardinal
                                       rung positions are set from
    top(s)                             how far along x the table covers

To make an actor faster, raise the content: add or replace a faster clip, or raise its
generator's `PlaybackSpeed` — then move the rung positions to match, or the table will
simply record the new mismatch. Editing `y` in the file desynchronises it from the
animations it describes and can violate I9.

## 6. Consumer

### 6.1 Output binding

The modifier's four parameters bind to named graph variables. Read from the
compiled graphs, not inferred:

    member       variable
    state    <-  iState              engine-written, graph-declared (§4.1)
    direction<-  Direction
    goalSpeed<-  Speed
    speedOut ->  SpeedSampled  |  SampledSpeed  |  HorseSpeedSampled

This is independent confirmation of §4: the record tag is `Direction`, the point x
is `Speed`, and the point y is what lands in the output variable.

**The output variable has three names, and filtering on one loses a quarter of the
consumers.** Counting blenders whose `blendParameter` binds to each:

    SpeedSampled          764
    SampledSpeed          266
    HorseSpeedSampled       7
    ------------------------
                         1037   across 38 of the 49 projects

`SpeedDamped` (184), `TurnDeltaDamped` (152), `TurnDelta` (102) and `staggerMagnitude`
(132) are the other blend axes and are **not** sampler outputs. Match on
`/(speedsampled|sampledspeed)/i`.

Eleven projects bind no blender to any of the three: both atronachs, chaurus flyer,
netch, dragon, dragon priest, dwarven spider, ice wraith, slaughterfish, wisp,
witchlight. For the storm atronach, wisp and witchlight that is consistent — their
tables are all-zero (§11), because there is no locomotion blend to report on.

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

### 6.2 Blender topology: who reads the sampled speed

There are two topologies, and which one an actor uses decides what the 19 direction
records mean for it.

**Bipeds: eight cardinal blenders, one scalar.** The graph holds a full compass of
`hkbBlenderGenerator` — Forward, ForwardRight, Right, BackRight, Back, BackLeft, Left,
ForwardLeft — and **every one of them binds `blendParameter` to the same sampler
output**. The blenders are not parameterised by direction; they *are* the direction,
and the graph activates the one matching the heading. The giant's default state:

    ForwardBlend       <- SpeedSampled   5, 61.84, 123.69
    ForwardRightBlend  <- SpeedSampled   5, 62.67
    RightBlend         <- SpeedSampled   5, 64.92
    BackRightBlend     <- SpeedSampled   5, 47.07
    BackBlend          <- SpeedSampled   5, 54.43
    BackLeftBlend      <- SpeedSampled   5, 49.53
    LeftBlend          <- SpeedSampled   5, 66.14
    ForwardLeftBlend   <- SpeedSampled   5, 62.67
    TurnLBlend         <- TurnDelta      45, 90        /* separate axis */

**This is why the table needs a direction axis at all.** One scalar feeds all eight
ladders and the ladders have different tops — for the player's bow, forward 155.21,
left 134.43, right 130.04, backward 113.94. A heading-independent scalar would index
`Bow_LeftBlend` against a number its children were never placed on. `Direction`
selects the record so the returned speed and the active blender are on one scale.

**Quadrupeds: one gait blender, turning instead of strafing.** A quadruped has a single
speed blender whose children are turn blenders. It cannot strafe, so it has no compass
family, and its side and back records come from the gait blender combined with the turn
axis — a structure §5.1.1 does not yet model.

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

**The sampled speed drives only the speed family.** The turn family takes
`TurnDeltaDamped`, an unrelated variable, and its weights are angles in degrees
with left/centre/right children. The two are independent axes:

    gait    speed position      turn authority
    walk         169.8               +/- 90
    trot         371.4               +/- 135
    run          833.0               +/- 270

Turn authority grows with gait. The sampler supplies the speed axis and nothing
else; direction of travel is the table's own `Direction` input, and turning is a
separate variable the table never sees.

### 6.3 MOVT, root motion, and PlaybackSpeed

Two different quantities meet at every blend rung, and the whole file exists because
they disagree.

    POSITION   the child's m_weight -- where on the blend-parameter axis this child
               is fully active. Authored. For a cardinal blender it is the movement
               type's own value for that direction.

    CONTENT    what the clip under that child actually delivers:
                     travel / (duration / PlaybackSpeed)
               Measured. travel and duration come from the animation cache.

**They are meant to be the same number.** `PlaybackSpeed` is the knob that makes them
so: one animation is reused at several rates, and each rate is a rung.

    GiantProject  CombatForwardBlend_WALK      movement type GiantCombatWalk_MT
    one clip, combatwalkforward, travel 164.13, natural duration 2.0 s

      position     from MOVT        pb        content
          5.00            --    0.0606           4.98
         82.46   ForwardWalk         1          82.07
        247.37   ForwardRun          3         246.20

Position and content agree to 0.5%. The animator chose `pb` so the clip delivers what
the movement type promises, then placed the rung at the movement type's number.

#### Where they drift, the drift is exactly the playback factor

    ChickenProject  Forward_Blend, top rung
      position  251.94   = Chicken_Default_MT.ForwardRun
      clip runforward: travel 251.94, natural duration 1.0 s  -> 251.94 at pb 1
      actual pb 1.6                                           -> content 403.10
      251.94 x 1.6 = 403.104                                     <- the content

    DogProject  ForwardWalkBlend_Dog, ForwardWalk rung
      position   74.54   = Dog_Default_MT.ForwardWalk
      clip walkforward: travel 89.45, natural duration 1.2 s  ->  74.54 at pb 1
      actual pb 1.4                                           -> content 104.36
      74.54 x 1.4 = 104.356                                      <- the content

In both the movement-type value equals what the clip delivers **at `pb = 1`**, and the
whole discrepancy is the playback rate. The number was taken from the raw animation,
the clip was later sped up for looks, and nothing went back to update it.

#### Consequence

The blender's rungs are labelled with positions; the clips deliver contents. Feeding the
requested speed straight into `blendParameter` would index a ladder labelled
74.54 / 251.94 with a number meaning 104.36 / 403.10, and the gait mix would be wrong
wherever the two differ.

**So x is the position axis and y is the content axis.** Where `pb` was tuned correctly
the table is nearly the identity and does nothing; where it was not, the table carries
the exact factor needed to land on the right blend of clips. `MOVT` does not come from
root motion and root motion does not come from `MOVT`: they are an intended equality
mediated by `PlaybackSpeed`, and this file records how far the shipped data is from it.

Neither side may be derived from the other. Measured over every blender in the game, one
test per child against the best clip beneath it:

    blender kind   children   position == content   within 2%   combined
    speed                75             27 (36%)     19 (25%)     61.3%
    turn                 98              0            0            0.0%

The turn row is definitional — those weights are angles (§6.2). The 29 speed-family
deviations fall into three authored classes, none random:

    1  floor constant     the slowest rung is pinned at exactly 5.000 while its clip
                          delivers 0.486 to 3.854 -- skeever, bear, horker, mammoth,
                          boar, chicken, hare

    2  playback omitted   the case above: position is the clip's natural speed while
                          the generator plays it at another rate -- chicken 251.938
                          at 1.6, bear 59.820 at 1.5

    3  MOVT value used    position is the actor's MOVT ForwardRun and matches no clip
                          product at all -- hare 244.444, boar 638.070

The dog's trot rungs are internally consistent at 186.758 / 287.320 / 425.000, being
0.65 / 1 / 1.5 times 287.32, so 287.32 is the authored trot speed; the cache records
192.866 for `TrotForward`. Where the two disagree the position is authored and the
content is measured, and **the table always records the content** — the chicken's
`max y` is 403.10 and not 251.94, the dog's is 288.73 and not 425.

#### Which positions come from MOVT

For the four cardinal blenders the positions are the movement type's own per-direction
pair, exact to the stored decimals:

    blender                        positions            MOVT
    CombatForwardBlend_WALK    [5, 82.46, 247.37]   ForwardWalk 82.46  ForwardRun 247.37
    CombatRightBlend_WALK      [5, 103.86, 311.59]  RightWalk  103.86  RightRun   311.59
    CombatBackBlend_WALK       [5, 63.50, 190.50]   BackWalk    63.50  BackRun    190.50
    CombatLeftBlend_WALK       [5, 93.70, 281.09]   LeftWalk    93.70  LeftRun    281.09

Pattern `[floor, <dir>Walk, <dir>Run]` with the floor a literal 5.0. A default state is
the same with `Walk == Run`. A combat-run state drops the floor and extrapolates one
rung above `Run`: `[<dir>Walk, <dir>Run, k x <dir>Run]`, k of 1.5 forward and 2.0
sideways.

**This is the only way the eight MOVT numbers enter the graph.** The blender does not
read `MOVT` at runtime; the values were baked in as positions at authoring time, and
that is what puts x on the `MOVT` scale and makes `Speed` comparable to a rung at all.

Two limits. The four **diagonal** blenders are not from `MOVT` — `ForwardRight` and
`ForwardLeft` share `[5, 82.07, 328.27]`, `BackRight` and `BackLeft` share
`[5, 63.50, 317.49]`, symmetric pairs with their own numbers, because `MOVT` has only
four directions. And **intermediate gait positions are never in `MOVT`**: across twelve
forward ladders `ForwardWalk` is the second rung in 12 of 12 and `ForwardRun` the top in
7 of 12, but trot and fast-trot account for 19 of 38 non-floor rungs and appear nowhere
in the record. `MOVT` alone will not reconstruct a ladder.

### 6.4 Selecting the blender for a state

A state owns a whole compass of blenders, and the selector states are labelled by
weapon or stance family. Extracted by walking each project's root-graph closure and
recording, per `hkbStateMachineStateInfo`, the sampler-bound blenders reachable beneath
it — 4,753 such states across the 49 projects:

    1hm_locomotion.hkx  Melee_Direction_Behavior
        id 0-4, 10, 11  ->  1HM_*          id 7      ->  Bow_*
        id 5, 6         ->  2HM_*          id 8, 9   ->  Magic_*
                                           id 12     ->  CrossBow_*
    magicbehavior.hkx   MagicCastLocomotion_Behavior   id 0 -> MagicCast_*
    bow_direction_behavior.hkx  Bow_DirectionType_Behavior  id 0 -> Bow_* (8)
    giantbehavior.hkx   StandingToLocomotionBehavior   id 1 -> ForwardBlend … (8)
                        CombatLocomotionBehavior       id 0 -> Combat*_WALK (8)
                        CombatLocomotionBehavior       id 1 -> Combat*_RUN  (8)

**The machine ids are not `iState` values.** `Melee_Direction_Behavior` runs 0-12 while
the player's key set is 0-10 and 15-17, and `id 7 -> Bow_*` sits against
`iState 8 = NPC_Bow_MT`. This is the same fact as §4.1: `iState` is engine-written and
no machine syncs to it, so game code drives the two independently and **nothing in the
shipped data joins them.**

So the join is by name, and it works: take the family token out of the state's
`iState_<MOVT>` name and match it to the blender prefix.

    NPC_BowDrawn, NPC_Bow          ->  Bow_          NPC_Sneaking  ->  Sneak_
    NPC_MagicCasting               ->  MagicCast_    NPC_1HM       ->  1HM_
    NPC_Magic                      ->  Magic_        NPC_2HM       ->  2HM_
    NPC_Blocking                   ->  1HM_          NPC_Default   ->  MT_

then pick within the family by the record's compass direction. This is a **convention,
not a reference**: `NPC_Bleedout_MT` and `NPC_Drunk_MT` carry no family token and cannot
be resolved this way, which is exactly where §5.1.1's misses land.

### 6.5 Why the table exists

§6.3 is the reason: the rungs carry positions, the clips carry contents, and the table is
the correction between them. Two further consequences follow from it.

**The output bound I9 is structural.** Above the top rung there is no further child to
blend toward, so the sampled speed pins and the curve saturates — which is why `max y`
is bounded by what the project's clips can deliver and by nothing in `MOVT`.

**A missing table is not a crash.** With no database the query is skipped and
`speedOut = goalSpeed` (§1.2), so the blend is indexed by the position axis instead of
the content axis. The failure mode is a wrong gait mix and sliding feet.

#### End to end

Authoring time, once per state and direction:

    1  MOVT <dir>Walk / <dir>Run  ->  rung positions of the <dir> blender   (§6.3)
    2  animator's clips           ->  each rung's travel and duration
    3  sweep Speed 0 .. top(s) at 0.5, record the delivered speed, keep the
       breakpoints                                                          (§5.3)

Runtime, once per frame:

    4  game code resolves the desired local velocity into a heading and a magnitude,
       writes Direction (0 fwd, .25 right, .5 back, .75 left) and Speed (MOVT units)
    5  game code writes iState for the current stance                       (§4.1)
    6  BSSpeedSamplerModifier::Update reads +0x50/+0x54/+0x58 and calls
       Query(state, direction, goalSpeed)                                   (§1.2)
    7  the DB picks entry by key, record by direction, brackets goalSpeed between two
       stored x and interpolates y linearly
    8  y lands in SpeedSampled / SampledSpeed / HorseSpeedSampled           (§6.1)
    9  that scalar is the blendParameter of all eight cardinal blenders; the graph has
       one of them active for the heading                                   (§6.2)
    10 the blend mixes the two bracketing gait clips, and because it is time-
       synchronised the actor travels at exactly the y that was looked up    (§5.1.1)

Step 10 closes the loop, and it is why the quantity in step 7 must be the *delivered*
speed rather than the requested one. With no database, step 6 is skipped and
`speedOut = goalSpeed` (§1.2): the blend is then indexed by the request, the gait mix is
wrong wherever request and delivery differ, and the actor's feet slide.

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

Format and semantics are settled, and so is the curve: `y` at any `x` follows from the
blend ladder and the animation cache in closed form (§5.1.1). A functionally correct
table can now be generated. What is still missing:

- **`top(s)`**, the sweep bound. Authored, not derived; 325 covers 74 of 86; the twelve
  exceptions were set by hand and inconsistently (§5.4). Carry it as an input.
- **The retention rule.** Needed only to match the shipped file byte for byte; a
  generator can emit the whole grid instead (§5.3). Now a tractable experiment, because
  the dense curve can be produced to score candidates against.
- **Blender identification for two player states.** `NPC_Bleedout_MT` and
  `NPC_Drunk_MT` carry no family token, so §6.5's name join cannot place them.
- **The 15 intermediate directions**, which mix two adjacent compass blenders, and
  **quadruped side and back records**, which have no compass family at all. Both are
  unmeasured; §5.1.1 is verified on the four cardinals only.

One measured fact remains unexplained rather than merely unimplemented:
`DeerProject` key 21 has `min y` 391.50 against a slowest child of 416.67, sitting below
the bottom rung rather than clamping to it. It is also the entry with the largest
authored `min x` (400).

Two earlier open items are closed. The `max y` shortfall of 4-11% on deer 20, skeever,
mammoth and sabrecat was the hyperbola lying below its chord — the ratio form predicts
all four to within 0.33% — and the note that "synchronised blending does not account for
it, all 13 blenders carry identical flags" had the inference backwards: identical flags
mean sync applies everywhere, which is the mechanism, not a reason to discount it.

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
