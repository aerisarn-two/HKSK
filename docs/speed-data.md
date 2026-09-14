# speeddatasinglefile.txt — the speed sampler database

    Status:   format CONFIRMED (byte-exact round trip)
              semantics CONFIRMED
              curve computable in closed form (§6)
              one input still authored: the sweep bound (§9)
    Source:   Skyrim SE, meshes/speeddatasinglefile.txt, 162527 bytes
    Consumer: BSSpeedSamplerModifier via BSSpeedSamplerDBManager
    Gate:     bUseSpeedSampler:Animation, compiled default 1 (ON)
    Reader:   HKSK.Cache.SpeedDataFile -- read, write, and query

Third file of the animation cache, after `animationdatasinglefile.txt` and
`animationsetdatasinglefile.txt`. Named `.txt`; binary after byte 1943.

## 0. What the file is for

A creature's movement speed is written down twice, in two places that are supposed
to agree.

**The RACE `MOVT` record** says how fast the game may ask the creature to move —
eight numbers, a walk and a run for each of forward, back, left and right.

**The animations** say how fast it actually moves. A clip's root motion carries a
travel distance and a duration, and `PlaybackSpeed` scales the duration, so the
delivered speed is `travel / (duration / PlaybackSpeed)`.

These are meant to be the same number. `MOVT` is authored *from* the animations,
and the locomotion blender's children are placed at the `MOVT` values (§5.4). When
an animator gets it right, asking for 247.37 moves the creature at 247.37.

They drift. Someone reads a speed off a clip, then changes `PlaybackSpeed` for
looks and does not revisit the record. The chicken's movement type says its run is
251.94; the clip delivers 403.10, because it plays at 1.6.

**This file is the measured difference.** For every locomotion state, every
heading, and every speed the game might request, it records what the creature will
actually do.

    x   the speed requested, in MOVT units
    y   the speed delivered, in the same units

Both axes are the same axis. **For a correctly authored creature the table is the
identity and the sampler does nothing.** Its deviation from `y = x` is the
authoring error, and matches it numerically:

    creature          MOVT vs clip drift     table deviation from y = x
    Bear                     0.0%                      1.2%
    Goat                     0.0%                      0.0%
    HighlandCow              0.2%                      0.1%
    Hare                    29.7%                     21.3%
    Dog                     32.4%                     32.0%
    Chicken                 60.0%                     52.0%

One part is not authoring error and does not go away. The blend is
time-synchronised, so it returns a **duration-weighted** average of two clips'
speeds rather than a plain one, and the curve bends away from a straight line by

    (s_a - s_b)(D_a - D_b) / (2(D_a + D_b))

which is zero only when the two clips have the same duration. So even a perfectly
authored creature needs the table between its gaits (§6).

Without the file the modifier passes the request through unchanged, the gait blend
is indexed by the requested speed instead of the achievable one, and the feet slide.

## 1. On-disk format

Little-endian. The dirlist is CRLF ASCII; everything after it is 32-bit words.

    file    := dirlist block[count]                  /* blocks in dirlist order */

    dirlist := "<count>\r\n"
               count x "<Project>Data\<Project>.spd\r\n"

    struct block {                                   /* one per project */
            u32     version;                         /* == 1 */
            u32     n_entries;
            entry   entries[n_entries];
    };

    struct entry {                                   /* one per locomotion state */
            u32     key;                             /* state id, see §3.1 */
            u32     n_records;                       /* == 19, or 0 if malformed */
            record  records[n_records];
    };

    struct record {                                  /* one per heading */
            f32     direction;                       /* 0.00 .. 0.90, see §3.2 */
            u32     n_points;
            struct { f32 x; f32 y; } points[n_points];
    };

`sizeof(record) = 8 + 8 * n_points`. No padding, no alignment beyond 4. The block
count is the dirlist count; blocks follow the dirlist in its order, with no index
and no length prefix, so a reader must walk them in sequence.

    dirlist                1943 bytes
    binary               160584 bytes  (40146 words)
    total                162527 bytes

    projects (= blocks)      49
    entries                  88
    records                1634        /* 86 valid entries x 19 */
    points                18302

Read-then-write reproduces the file **byte for byte**.

A reader must tolerate `n_records == 0` (§10).

## 2. Invariants

Measured over the whole file. A reader may assert these; a writer must hold them.

    #    invariant                                                   observed
    I1   version == 1                                               49/49 blocks
    I2   n_entries in {1,2,3,4,6,14}                                 49/49
    I3   n_records == 19                                             86/88 entries
    I4   direction[i] is the float accumulation of +0.05f, in order  86/86
    I5   the direction sequence ends at 0.90; 0.95 never present     86/86
    I6   x mod 0.5 == 0                                              18302/18302
    I7   x non-decreasing within a record                            1634/1634
    I8   all 19 records of an entry share one exact max(x)           86/86
    I9   max(y) <= max over the project's clips of delivered speed   46/46 projects

**I4 is exact and a reader must reproduce it exactly.** The stored values are
bit-identical to accumulation and bit-different from `0.05f * i`, diverging from
i = 7:

    i     stored        0.05f * i     accumulated
    7     0.350000024   0.349999994   0.350000024
    18    0.900000155   0.900000036   0.900000155

**I5** means a heading in [0.95, 1.0) has no record and resolves against 0.90.

**I9** needs the playback factor. Against raw root-motion speed the bound fails on
4 of 46 projects; against `speed x PlaybackSpeed` it holds on all 46.

Not invariant, and not to be assumed:

- `y` is not monotonic in `x`, and **`y` may exceed `x`** — on 24 of the 86
  populated entries `max y` exceeds every `MOVT` translation speed in its own row.
- The 19 records of an entry do not share a minimum `x` (21 distinct minima).
- A repeated final point is **not** a saturation marker. 1162 of 1634 records end
  with the last point written twice at the same x. With the duplicate removed, 1068
  records are still rising where the sweep ends. Testing `y[-1] == y[-2]` detects
  the duplicate, not a plateau.

## 3. What the fields mean

### 3.1 key — the locomotion state

`m_state` reads the graph variable `iState`, and **the graph declares the values it
can take.** Each sampled state has an `iState_<MOVT>` variable whose initial value
is the state id and whose name suffix names a `MOVT` record:

    variable                 initial value   movement type
    iState_DeerDefault                  20   Deer_Default_MT
    iState_DeerDefaultRun               21   Deer_DefaultRun_MT
    iState_NPCBowDrawn                   3   NPC_BowDrawn_MT

Read them from `hkbBehaviorGraphData.m_stringData.m_variableNames`, paired **by
index** with `m_variableInitialValues.m_wordVariableValues`.

**Scope to the project's root behaviour graph.** Keys against declarations, all 49
projects, the Falmer's two corrupt keys excluded:

    scope                     keys == declared   keys subset   violations
    root behaviour graph                    24            25            0
    full reference closure                  21            28            0

Both are violation-free, so a key is always a declared state id, but the root graph
is the tighter statement and is the rule. It matters: `quadrupedbehavior.hkx` is a
shared template holding the union of all quadruped constants, reachable from eleven
projects, and it disagrees with two of them — `iState_DeerDefault` is 10 there and
20 in `deerbehavior.hkx`; `iState_HorkerSwimDefault` is 50 there and 51 in
`horkerbehavior.hkx`. The cache follows the root graph. The template's constants are
inert.

Within a root graph a state id is unique: 0 of 86 entries map to more than one
movement type.

#### Reaching the root graph

The project file is `hkbProjectData`, not a character:

    <Project>.hkx   hkbProjectStringData.m_characterFilenames   ->
    character       hkbCharacterStringData.m_behaviorFilename   ->  ROOT GRAPH
                    hkbBehaviorReferenceGenerator.m_behaviorName -> sub-graphs

Resolve each path against the project file's own directory, case-insensitively.
`m_behaviorFilenames` holds the root only; every other graph is reached through
`hkbBehaviorReferenceGenerator`.

Two traps, each of which silently empties the result:

- HKX2 models Havok members as **auto-properties**. Reflecting over `GetFields()`
  descends into nothing; use the properties.
- The character file and its graphs are **siblings**, not nested
  (`characters/defaultmale.hkx` beside `behaviors/0_master.hkx`), so matching graphs
  by path prefix under the character's directory finds none for the seven projects
  shaped that way.

#### The numbering

Eleven quadrupeds share one template and take a slot of ten each, in alphabetical
order:

    0 Bear    10 Cow    20 Deer    30 Dog    40 Goat    50 Horker
    60 Horse  70 Mammoth  80 SabreCat  90 Skeever  100 Wolf

`key = 10 x alphabetical index`, exact for all eleven, with the offset inside a slot
distinguishing gaits (deer 20 walk/trot, 21 run; horse 60/61/62/63). `BoarProject`
shows the scheme was frozen before Dragonborn: the boar would take slot 10 if
inserted alphabetically, and instead declares 0 in its own root graph.

#### Why iState is declared but never synced

The runtime value comes from game code. In every graph declaring `iState` the sole
binding is `BSSpeedSamplerModifier.state`, and no state machine syncs to it:

    file                     iState vars   machines syncing to it
    0_master.hkx                      94                        0
    giantbehavior.hkx                 37                        0
    quadrupedbehavior.hkx             45                        0
    draugrbehavior.hkx                33                        0
    ... 16 graphs checked, none

`hkbStateMachine.m_syncVariableIndex` exists and is used for `iSyncDefaultState`,
`iSyncSprintState`, `currentDefaultState`, `iIsInSneak` and `iCrossbowState`.
`iState` is not among them.

So matching key sets against state machine ids finds nothing, and should not be
attempted:

    DefaultMale     keys [0..10,15,16,17]   0 of 5 machines match
    DraugrProject   keys [0,3,4,5,6,7]      0 exact
    GiantProject    keys [0,1,2]            1 exact -- BleedOutBehavior, whose
                                            states are BleedOut_Start/Idle/Getup

The *set* of values it can take is nonetheless enumerated, once per movement type,
as those variable initial values. The key set is recoverable.

#### Resolving the name to a MOVT record

104 distinct declarations are reachable from the 49 projects; 103 name a record that
exists, and all 86 populated entries resolve.

Five naming disagreements must be handled:

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

Do not substitute a token-set or camelCase matcher. Consecutive capitals
(`NPCDefault`) do not split, so it scores worse than plain normalisation:

    token-set / camelCase split                  75 / 104
    normalised names only                        93 / 104
    normalised + the five rules above           103 / 104

The resolution is injective apart from the shipped typo pair, and the dangerous
near-neighbours separate correctly — `NPC_Bow_MT` 370, `NPC_BowDrawn_MT` 135,
`NPC_BowDrawn_QuickShot_MT` 370, `NPC_Blocking_MT` 81.

**`iState_CombatSpider_MT` is a dangling reference.** Both spider root graphs declare
it at state 1 and no such record exists. It is never a key, so it costs nothing; a
resolver must tolerate a declaration the ESM does not back.

Not every declared state is sampled: 25 of the 49 projects hold fewer entries than
they declare states.

### 3.2 direction — the heading

The graph's `Direction` variable, range [0,1]. A compass:

    0.00 ahead      0.25 right      0.50 behind      0.75 left

Fixed by the player's four cardinal records against the eight directional blenders
(§5.2), each matching its own blender's top child exactly:

    record       max y     blender                 weight    clip
    0.00        155.21     Bow_ForwardBlend        155.21  155.21
    0.25        131.06     Bow_RightBlend          130.04  131.06
    0.50        113.94     Bow_BackwardBlend       113.93  113.94
    0.75        134.43     Bow_LeftBlend           134.43  134.43

The 19 records sample at 0.05 while the eight blenders sit at 0.125 spacing, so only
records 0, 5, 10 and 15 read a single blender and the rest are mixtures of two
adjacent ones. That is why the direction profile is not monotonic: 0.20 gives 126.27
and 0.25 gives 131.06, because 0.20 is a forward/right mixture and 0.25 is pure right.

Curves are near mirror-symmetric about 0.5. Over the 688 pairs (0.10,0.90) …
(0.45,0.55): 127 bit-identical, 499 within 2%, 62 differ — **91.0% mirrored**.

**The residual is not noise and must not be symmetrised away.** `MOVT` is symmetric
left/right (`LeftRun == RightRun` on 54 of 63 testable entries) but the animations are
not: `Bow_LeftBlend` tops at 134.43 against `Bow_RightBlend`'s 130.04, a 3.4%
difference in the animators' own clips, and the table reproduces it. Six entries carry
a left/right gap above 1% despite symmetric `MOVT`, the riekling by 18%.

### 3.3 x and y — one axis

The query signature (§4.2) passes `(i32 state, f32 direction, f32 goalSpeed)` and
returns one float. The first two select entry and record; `goalSpeed` indexes within
the record.

Both x and y are game units/s on the `MOVT` scale, and they are the same axis (§0).
x is what the game asks for; y is what the creature does.

`x` is not a time, not an index, and not a sample number. 57.6% of stored x values
are half-integers, the spacing is irregular (354 distinct gap sizes, 1 to 1768 grid
steps), and the range differs per entry from 189.5 to 999.5.

## 4. The engine side

Strings in `SkyrimSE.exe`:

    bUseSpeedSampler:Animation        INI gate, [Animation] section
    Meshes/SpeedDataSingleFile.txt    merged form, as shipped
    MESHES/SPEEDDATA/                 split per-project form; not shipped
    BSSpeedSamplerDBManager           singleton, BSTSingletonSDM w/ static buffer
    BSISpeedSamplerDB                 abstract interface over the above
    BSSpeedSamplerModifier            hkbModifier subclass, the caller
    goalSpeed

`BSSpeedSamplerModifier` occurs in 42 compiled graphs / 30 distinct graph names. Its
parameters, from `bcbehavior.hkb`, the one authoring file that shipped:

    m_enable      bool            = true
    m_state       hkFloatVariable -> iState
    m_direction   hkFloatVariable -> Direction      range [0, 1]
    m_goalSpeed   hkFloatVariable -> Speed          range [0, 384] (this graph)
    m_speedOut    hkFloatVariable -> out

Variable ranges survive only in `.hkb`: `m_wordMinVariableValues` and
`m_wordMaxVariableValues` are empty in all 8 compiled graphs inspected, while
`m_variableInitialValues` is fully populated.

### 4.1 The INI gate defaults ON

No shipped INI names the setting; `Skyrim_Default.ini` and the four quality presets
have no `[Animation]` section, so the compiled default applies.

Each INI setting is a 32-byte record; the value precedes the name pointer:

    offset  field
    +0      vtable          0x141775178 for all bool settings
    +8      value           <- default
    +16     const char*     name
    +24     pad             0xEFBEADDE

`bUseSpeedSampler` value = **1**. The offset reads correctly across all 21
`b*:Animation` settings sharing that vtable: debug and dead-platform ones are 0
(`bDrawAnimPoseInVDB`, `bDisplayMarkWarning`, `bUseSPUGenerate`, `bEnableHavokHit`,
`bAlwaysDriveRagdoll`, `bInitiallyLoadAllClips`), functional ones are 1 (`bFootIK`,
`bAnimInterpEnable`, `bHumanoidFootIKEnable`, `bMultiThreadBoneUpdate`).

**This data is live for every actor whose graph carries the modifier.**

### 4.2 Query path

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

    +0x50  i32  state          float Query(this,
    +0x54  f32  direction                  void  *context,
    +0x58  f32  goalSpeed                  i32    state,      /* -> entry.key   */
    +0x5c  f32  speedOut                   f32    direction,  /* -> record      */
                                           f32    goalSpeed); /* -> record.x    */
                                           /* returns record.y */

`state` is the only integer; `direction` and `goalSpeed` both move through `movss`.

**At runtime nothing is computed.** The lookup is:

    1  state     -> the entry whose key matches
    2  direction -> one of that entry's 19 records
    3  goalSpeed -> the two stored points bracketing it; interpolate y linearly.
                    Below x[0] return y[0]; above x[n] return y[n].

Two searches and a lerp. All the modelling is baked into the stored y.

**With no database the call is skipped and `speedOut = goalSpeed`.** That is an
identity pass-through, not a fallback computation.

### 4.3 Load path

Two loaders, and the INI gate sits on only one.

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

So the gate controls a **lazy per-project `.SPD` load**, not the merged file. The
three path fragments are interned as globals:

    0x1431c67e0  "MESHES/SPEEDDATA/"
    0x1431c67f0  ".SPD"
    0x1431c67f8  "Meshes/SpeedDataSingleFile.txt"

`MESHES/SPEEDDATA/<...>.SPD` is a supported per-project path the game ships nothing
for. A tool adding one creature can write a single `.SPD` rather than rewriting the
merged file.

## 5. The graph side

### 5.1 Where the answer goes

    member       variable
    state    <-  iState              engine-written, graph-declared (§3.1)
    direction<-  Direction
    goalSpeed<-  Speed
    speedOut ->  SpeedSampled  |  SampledSpeed  |  HorseSpeedSampled

This independently confirms §3: the record tag is `Direction`, the point x is
`Speed`, and the point y is what lands in the output variable.

**The output variable has three names.** Filtering on one loses a quarter of the
consumers. Blenders whose `blendParameter` binds to each:

    SpeedSampled          764
    SampledSpeed          266
    HorseSpeedSampled       7
    ------------------------
                         1037   across 38 of the 49 projects

Match on `/(speedsampled|sampledspeed)/i`. `SpeedDamped` (184), `TurnDeltaDamped`
(152), `TurnDelta` (102) and `staggerMagnitude` (132) are other blend axes and are
not sampler outputs.

Eleven projects bind no blender to any of the three: both atronachs, chaurus flyer,
netch, dragon, dragon priest, dwarven spider, ice wraith, slaughterfish, wisp,
witchlight. For the storm atronach, wisp and witchlight that is consistent — their
tables are all zero, because there is no locomotion blend to report on.

### 5.2 Blender topology

Two topologies, and which one an actor uses decides what its 19 records mean.

**Bipeds: eight cardinal blenders, one scalar.** A full compass of
`hkbBlenderGenerator` — Forward, ForwardRight, Right, BackRight, Back, BackLeft,
Left, ForwardLeft — and **every one binds `blendParameter` to the same sampler
output**. They are not parameterised by direction; they *are* the direction, and the
graph activates the one matching the heading. The giant's default state:

    ForwardBlend       <- SpeedSampled   5, 61.84, 123.69
    ForwardRightBlend  <- SpeedSampled   5, 62.67
    RightBlend         <- SpeedSampled   5, 64.92
    BackRightBlend     <- SpeedSampled   5, 47.07
    BackBlend          <- SpeedSampled   5, 54.43
    BackLeftBlend      <- SpeedSampled   5, 49.53
    LeftBlend          <- SpeedSampled   5, 66.14
    ForwardLeftBlend   <- SpeedSampled   5, 62.67
    TurnLBlend         <- TurnDelta      45, 90      /* separate axis */

**This is why the table has a direction axis.** One scalar feeds all eight ladders
and the ladders have different tops — for the player's bow, forward 155.21, left
134.43, right 130.04, backward 113.94. A heading-independent scalar would index
`Bow_LeftBlend` against numbers its children were never placed on.

**Quadrupeds: one gait blender, turning instead of strafing.** A single speed blender
whose children are turn blenders. No compass family, so its side and back records
come from the gait blender combined with the turn axis — a structure §6 does not model.

The turn family is a genuinely separate axis, driven by `TurnDeltaDamped`, with
weights in degrees and left/centre/right children:

    gait    speed position      turn authority
    walk         169.8               +/- 90
    trot         371.4               +/- 135
    run          833.0               +/- 270

Turn authority grows with gait. The sampler supplies the speed axis and nothing else.

**Flags do not distinguish the families.** Across all 1037 sampler-bound blenders the
value is 17 (1022) or 16 (15) — `FLAG_SYNC | FLAG_IS_PARAMETRIC_BLEND_CYCLIC`.
`FLAG_PARAMETRIC_BLEND` (8) is never set, and the same pattern appears on the turn
blenders. Evidence for the blend model in §6 is the measured fit, not the flag.

### 5.3 Which blender serves a state

A state owns a whole compass, and the selector states are labelled by weapon or
stance family. Extracted by walking each project's root-graph closure and recording,
per `hkbStateMachineStateInfo`, the sampler-bound blenders beneath it — 4753 such
states across the 49 projects:

    1hm_locomotion.hkx  Melee_Direction_Behavior
        id 0-4, 10, 11  ->  1HM_*          id 7      ->  Bow_*
        id 5, 6         ->  2HM_*          id 8, 9   ->  Magic_*
                                           id 12     ->  CrossBow_*
    magicbehavior.hkx   MagicCastLocomotion_Behavior  id 0 -> MagicCast_*
    bow_direction_behavior.hkx  Bow_DirectionType_Behavior id 0 -> Bow_* (8)
    giantbehavior.hkx   StandingToLocomotionBehavior  id 1 -> ForwardBlend … (8)
                        CombatLocomotionBehavior      id 0 -> Combat*_WALK (8)
                        CombatLocomotionBehavior      id 1 -> Combat*_RUN  (8)

**The machine ids are not `iState` values.** `Melee_Direction_Behavior` runs 0-12
while the player's key set is 0-10 and 15-17, and `id 7 -> Bow_*` sits against
`iState 8 = NPC_Bow_MT`. Same fact as §3.1: game code drives the two independently
and nothing in the shipped data joins them.

So the join is by name, and it works — take the family token out of the state's
`iState_<MOVT>` name and match it to the blender prefix:

    NPC_BowDrawn, NPC_Bow  -> Bow_        NPC_Sneaking -> Sneak_
    NPC_MagicCasting       -> MagicCast_  NPC_1HM      -> 1HM_
    NPC_Magic              -> Magic_      NPC_2HM      -> 2HM_
    NPC_Blocking           -> 1HM_        NPC_Default  -> MT_

then pick within the family by the record's compass direction. **This is a convention,
not a reference.** `NPC_Bleedout_MT` and `NPC_Drunk_MT` carry no family token and
cannot be placed this way.

### 5.4 MOVT, root motion and PlaybackSpeed

Two quantities meet at every blend rung.

    POSITION   the child's m_weight -- where on the blend axis this child is fully
               active. Authored. For a cardinal blender it is the movement type's
               own value for that direction.

    CONTENT    what the clip under that child delivers:
                     travel / (duration / PlaybackSpeed)
               Measured, from the animation cache.

They are meant to be equal. `PlaybackSpeed` is the knob that makes them so: one
animation is reused at several rates, and each rate is a rung.

    GiantProject  CombatForwardBlend_WALK      movement type GiantCombatWalk_MT
    one clip, combatwalkforward, travel 164.13, natural duration 2.0 s

      position     from MOVT        pb        content
          5.00            --    0.0606           4.98
         82.46   ForwardWalk         1          82.07
        247.37   ForwardRun          3         246.20

Where they drift, the drift is exactly the playback factor:

    ChickenProject  position 251.94 = ForwardRun; runforward is 251.94 at pb 1;
                    pb is 1.6                   ->  content 403.10  = 251.94 x 1.6
    DogProject      position  74.54 = ForwardWalk; walkforward is 74.54 at pb 1;
                    pb is 1.4                   ->  content 104.36 =  74.54 x 1.4

Measured across every blender in the game, one test per child against the best clip
beneath it:

    blender kind   children   position == content   within 2%   combined
    speed                75             27 (36%)     19 (25%)     61.3%
    turn                 98              0            0            0.0%

The turn row is definitional — those weights are angles. The speed deviations fall
into three authored classes, none random:

    1  floor constant    the slowest rung is pinned at exactly 5.000 while its clip
                         delivers 0.486 to 3.854
    2  playback omitted  the case above -- chicken 251.938 at 1.6, bear 59.820 at 1.5
    3  MOVT value used   position is the MOVT ForwardRun and matches no clip product
                         -- hare 244.444, boar 638.070

Where they disagree the position is authored and the content is measured, and **the
table always records the content**: the chicken's `max y` is 403.10, not 251.94.

#### Which positions come from MOVT

For the four cardinal blenders, exact to the stored decimals:

    blender                        positions            MOVT
    CombatForwardBlend_WALK    [5, 82.46, 247.37]   ForwardWalk 82.46  ForwardRun 247.37
    CombatRightBlend_WALK      [5, 103.86, 311.59]  RightWalk  103.86  RightRun   311.59
    CombatBackBlend_WALK       [5, 63.50, 190.50]   BackWalk    63.50  BackRun    190.50
    CombatLeftBlend_WALK       [5, 93.70, 281.09]   LeftWalk    93.70  LeftRun    281.09

Pattern `[floor, <dir>Walk, <dir>Run]`, floor a literal 5.0. A default state is the
same with `Walk == Run`. A combat-run state drops the floor and extrapolates one rung
above `Run`: `[<dir>Walk, <dir>Run, k x <dir>Run]`, k of 1.5 forward and 2.0 sideways.

**This is the only way the eight MOVT numbers enter the graph.** The blender does not
read `MOVT` at runtime; the values were baked in as positions at authoring time, which
is what puts x on the `MOVT` scale.

Two limits. The four **diagonals** are not from `MOVT` — `ForwardRight` and
`ForwardLeft` share `[5, 82.07, 328.27]`, `BackRight` and `BackLeft` share
`[5, 63.50, 317.49]`, because `MOVT` has only four directions. And **intermediate gait
positions are never in `MOVT`**: across twelve forward ladders `ForwardWalk` is the
second rung in 12 of 12 and `ForwardRun` the top in 7 of 12, but trot and fast-trot
account for 19 of 38 non-floor rungs and appear nowhere in the record. `MOVT` alone
will not reconstruct a ladder.

RACE `SpeedOverrides` may override a movement type per race, but only 10 of 161 races
carry any, with 17 distinct values, and they are almost all byte-identical copies of
the record they override. Reading the record is correct.

## 6. Computing the curve

**y has a closed form. No graph evaluation is required.**

The blend is time-synchronised: Havok interpolates the children's root-motion
**translation** and their **duration** as two independent linear ramps, and the
delivered speed is their quotient. Order the children by weight into rungs

    rung i  =  (w_i, travel_i, dur_i)        dur_i = clip duration / PlaybackSpeed

then for x between rungs a and b

    u     = (x - w_a) / (w_b - w_a)
    y(x)  = (travel_a + u*(travel_b - travel_a)) / (dur_a + u*(dur_b - dur_a))

clamped to `travel/dur` of the first rung below `w_0` and the last above `w_n`.

A quotient of two linear functions is a Mobius transform, so each segment is a
hyperbolic arc, not a chord — which is why the curves look like acceleration ramps,
and why every chord-based model came in short and never over. The hare's ladder shows
the size of it; its slowest rung plays `walkforward` at 0.058, stretching 0.833 s to
14.368 s:

    rung   weight   travel      dur     speed
       1     5.00    52.26   14.368      3.64
       2    89.18    52.26    0.833     62.71
       3   244.44   100.19    0.3125   320.62

    x        observed     this form   linear chord
    63.5        10.52         10.53          44.69
    79.0        21.10         21.15          55.56
    85.0        34.56         34.70          59.77
    88.0        50.75         51.06          61.88

**This is the part of the file that is not authoring error** (§0), and it has two
sources of very different character.

Measured over the 40 adjacent rung pairs of thirteen forward ladders:

    duration ratio ~ 1 (equal durations)    5 pairs   median |deviation|  0.000%
    duration ratio != 1                    35 pairs   median |deviation| 17.6%

Curvature comes from duration mismatch and from nothing else — equal durations give an
exactly straight segment. Since 26 of the 40 pairs are the *same clip* on both sides,
the mismatch is usually just a `PlaybackSpeed` ratio.

**The dominant term is the floor rung, and it is a rigging convention rather than a
fact about locomotion.** Every ladder's bottom step reuses the walk clip at a rate near
0.05, stretching it 7x to 71x, which bends that first segment enormously:

    Chicken   walkforward -> walkforward   ratio 71.43   -94.6%
    Deer      walkforward -> walkforward   ratio 34.48   -89.0%
    Bear      walkforward -> walkforward   ratio 25.00   -85.2%
    Skeever   walkforward -> walkforward   ratio  7.25   -57.4%

That is how a near-standstill is faked, not how an animal accelerates.

**Between real gaits the curvature is mild** — steps of 1.2x to 3x give a few percent:
bear trot pairs at -0.3% and -1.7%, dog trot pairs at -4.5% and -4.0%. Small, but not
zero, and it is genuine blending behaviour.

**The deviation is always below the chord, never above.** A faster rung is a shorter
clip, so `(s_a - s_b)` and `(D_a - D_b)` always carry opposite signs. That is why every
chord-based model tested came in short and never over.

### Verification

Over the whole file, on the four cardinal records, selecting the blender by the
state's family and the record's compass direction, with no fitting:

    records where family + direction leave exactly one candidate    122
      median relative error < 1%                                    105
    all deterministically selected records                          173
      median relative error < 1%                                    114
      median of the per-record medians                            0.13%

The 122-record figure is the honest one — no selection freedom, so no fitting is
possible. Against a linear chord the same records score 10-25%.

The misses are blender **identification**, not the form: they concentrate on
`NPC_Bleedout_MT` and `NPC_Drunk_MT`, which §5.3 cannot place.

**Scope.** Verified on the four cardinal directions only, which is 173 of the file's
1634 records. The 15 intermediate directions mix two adjacent compass blenders and
need a two-blender model that is not measured. Quadruped side and back records have no
compass family at all. 171 of the 344 cardinal records could not be assigned a blender,
mostly for that reason.

## 7. Authoring a creature

### Choosing MOVT

`MOVT`'s eight translation values become the cardinal rung positions, and a rung's
content is what its clip delivers, so:

    MOVT <dir>Walk = travel_walk / (duration_walk / PlaybackSpeed_walk)
    MOVT <dir>Run  = travel_run  / (duration_run  / PlaybackSpeed_run)

The shipped game follows this: of **428 rungs** whose position is a `MOVT` Walk or Run
value, **348 agree with their clip's content to within 1%**, 369 within 5%, median
ratio 1.000.

    1  pick a walk clip and a run clip for each of forward, back, left, right
    2  choose each clip's PlaybackSpeed -- the real tuning knob, since one animation
       reused at several rates is how a gait ladder is built
    3  compute the delivered speed of each; those eight numbers are the MOVT record
    4  place the blend rungs at the same eight numbers
    5  regenerate the table
    6  verify content / position == 1.000 on every cardinal rung

Step 6 is one division per rung and catches the only failure mode. When the two drift
the ratio is a recognisable playback rate:

    HorseProject    Horse_Default_MT   ForwardWalk   125.11 vs 303.91   x2.429
    ChickenProject  Chicken_Default    ForwardRun    251.94 vs 403.10   x1.600
    BearProject     Bear_Default_MT    ForwardWalk    59.82 vs  89.74   x1.500
    DogProject      Dog_Default_MT     ForwardWalk    74.54 vs 104.36   x1.400
    RieklingProject DLC2Riekling       ForwardWalk   167.42 vs 100.46   x0.600

### Why a wrong MOVT still matters

It does not slide the feet — the table records the content, so the gait blend stays
correctly indexed and the animation matches the motion. What breaks is upstream:
`MOVT` is what the engine plans movement with, so a dog whose record says 74.54 while
its clip delivers 104.36 travels 40% faster than pathing, combat spacing and arrival
timing assume.

Keep position and content equal and the table degenerates towards the identity. That
is the sign it was authored correctly.

### Changing an existing creature

    input                              controls
    clip travel and duration           the content: what the actor delivers, in y
    ClipGeneratorEntry.PlaybackSpeed   scales content; the knob that aligns it to MOVT
    blend rung m_weight                the position, in x
    RACE MOVT                          what the game requests, and what the cardinal
                                       rung positions are set from
    top(s)                             how far along x the table covers

To make an actor faster, raise the content — add or replace a faster clip, or raise
its `PlaybackSpeed` — then move the rung positions and the `MOVT` record to match, or
the table will simply record the new mismatch. Editing `y` in the file desynchronises
it from the animations it describes and can violate I9.

## 8. Generating a table

    for each sampled state s:                      /* from §3.1 */
        for (d = 0.0f; d < 0.95f; d += 0.05f):      /* 19 iterations, I4 */
            for (x = start; x < top(s); x += 0.5f):
                y = closed form of §6 at x
            retain points
        emit entry { key = s, records = 19 curves }

Everything except `top(s)`, `start` and the retention rule follows from this document.

**Retention is optional.** A generator that does not need to match the shipped file
byte for byte can emit every grid point: 1,206,044 points and about 9.7 MB against the
shipped 18,302 points and 162,527 bytes — 59x, and more accurate, since the consumer
then interpolates across half-unit steps rather than gaps of up to 1768 of them. Every
tenth grid point is 6x and still exact to well under one unit.

**Choosing the sweep.** The sweep may start at or below the ladder's bottom rung and
lose nothing, because the blend clamps there and the response is constant. Starting
above it discards live response. The file has one of each:

    DeerProject:21   starts at 400, bottom rung 416.50 -> constant below it;
                     nothing lost
    GiantProject:2   starts at 150, bottom rung  50.00 -> 50..150 is live and absent;
                     a request of 50 returns y(150) = 65.87 where 50.00 is correct

So `min(x)` is a claim that the response is flat below it — true for the deer, false
for the giant.

**The 0.5 grid is quantisation, not resolution.** About 11 of the swept positions
survive per record — some 650 of them at the default bound — and the consumer
interpolates between them, so the grid only fixes where a breakpoint may land, to
within half a unit. The y error that introduces is
bounded by `0.5 x slope`, about 0.43 units at the median slope of 0.86.

Point counts per record in the shipped file, terminal duplicate included:

    mean 11.2   median 11   min 2   max 121     quartiles 8 / 13, 90th 15

Bimodal: a main mode of 8-14 holds 1052 of 1634 records, and a spike at exactly 2
points holds 180 — the degenerate curves where y is constant. Per entry: mean 213,
median 206, range 38 to 1037.

## 9. What is still unknown

**`top(s)`, the sweep upper bound.** Authored, not derived. It is the exclusive bound
of the generator's loop, so the file exposes it only as `max(x) = top(s) - 0.5`:

    top(s)     190    325    415    425    450    750    833   1000
    max(x)   189.5  324.5  414.5  424.5  449.5  749.5  832.5  999.5
    entries      1     74      1      2      1      2      1      4

**`top(s)` is a speed**, in the same game units as everything else on the x axis, and
two of the eight are demonstrably copied from a movement type: `DeerProject` key 21 is
833, exactly `Deer_DefaultRun_MT.ForwardRun`, and `GiantProject` key 2 is 415, exactly
`GiantCombatRun_MT.ForwardRun`.

The deer settles which side is authored. 833 is a speed that exists elsewhere in the
game data; its loop iteration count, 1666, is not a number anyone would type. So the
bound is authored as a speed and the count is derived from it. (The other seven counts
are all divisible by ten, so they would be unremarkable as typed constants — the deer
is the only entry that discriminates.)

325 is the default on 74 of 86 entries and its origin is unknown. It is a plausible
place for one: across the 86 resolvable entries it exceeds the `ForwardWalk` of 81 and
falls below the `ForwardRun` of 52, sitting at the 67th percentile of all walk and run
values and at 88% of the player's own run. But that is where the number lands, not where
it came from, and it is applied unchanged to creatures whose speeds span 61.84 to
802.29.

Pools searched and exhausted, so they are not searched again:

    pool                                                  325    650    the twelve
    RACE, every numeric leaf to depth 4, 161 records     1 hit      -   -
    RACE SpeedOverrides (only 10 races carry any)            -      -   -
    MOVT, all fields, 107 records                            -      -   3/12, mixed
    GMST, 665 settings                                       -   1 hit  -
    behaviour-graph float literals                           2      -   4/12 weights

The single 325 in RACE is `DLC2SprigganBurntRace.Starting[0].Value`, a starting actor
value; the single 650 in GMST is `fIronSightsDOFRange`, a Fallout leftover.

The best remaining lead is that for three entries `max(x) + 0.5` equals the top weight
of the state's blender exactly — dog and wolf 425, deer 833 — at a 2.7% control. It
fails on the other nine. **3 of 12 is a lead, not a rule.**

Two hypotheses are tested and dead:

- **`V = MOVT ForwardRun x {1, 1.5, 2}`.** Reached 8 of 12 when scored against the
  project's whole movement-type pool, but against the movement type each state
  actually declares it falls to 2 of 12 and the multiplier disappears.
- **The bound covers the blend.** None of the 8 non-default entries with a resolvable
  family has `max(x)` equal to its family's top rung, and they fail in both directions
  at once — the player's bow states sweep 6.4x past a ladder topping at 155.21 while
  the giant's combat run stops at 67% of its blend. Meanwhile 30 of 35 default entries
  have ladders running past 325 and were never raised.

**`max(x)` does not bound what the game can request.** 47 of the 86 entries stop below
their own state's `ForwardRun` — the scrib sweeps to 324.5 against a movement type of
802.29 — and 37 of those are still rising when the sweep ends. Above the last point the
query clamps, so those actors are indexed below the speed they are asked for.

**The point-retention rule.** Needed only for a byte-identical rebuild (§8). Now a
tractable experiment, because §6 can produce the dense curve to score candidates
(Douglas-Peucker at a tolerance, curvature threshold, error-bounded decimation) against
the shipped file's 1634 exact point sets.

**Unverified rather than unknown:** the 15 intermediate directions, quadruped side and
back records, the two player states §5.3 cannot place, and the eight non-hovering
projects that bind no sampler blender.

**One measured anomaly.** `DeerProject` key 21 stores y = 391.50 at x = 400 where the
blend, clamped below its bottom rung of 416.50, should deliver 416.67. There is nothing
to interpolate in that region, so there is nothing to be approximately right about.

**Sentinel rungs.** The player's ladders carry a top rung at 3509.88 and the sneak
family at 1320, each exactly 10x the rung below. Those ladders never saturate, which is
why "cover the blend" is undefined for them. Unexplained.

## 10. Known corruption

`FalmerProjectData` declares 4 entries, 2 malformed. In file order:

    key            hex           bytes (LE)   n_records
    -2147483648    0x80000000                         0     /* INT_MIN */
    1              0x00000001                        19
    2              0x00000002                        19
    1651406194     0x626E7572    "runb"               0

Both malformed keys carry `n_records == 0`, so they occupy 8 bytes and describe
nothing; no lookup will request state `0x80000000`.

`0x626E7572` is not a number. Its bytes as stored are the ASCII `runb` — the head of a
clip name, and `runbackward`, `runbackwardleft` and `runbackwardright` are all in the
Falmer's own cache. The exporter wrote the start of a string into a `u32` key field.

**A reader MUST tolerate `n_records == 0`.** A writer SHOULD NOT reproduce these.

## 11. Method note

§4.2 and §4.3 required unwrapping the SteamStub Variant 3.1 (x64) wrapper on the retail
executable, which encrypts `.text`. Unwrapped with Steamless v3.1.0.5, for reading only;
verified by `.text` entropy falling from 8.000 to 6.354 bits/byte and by references to
the setting object appearing (6, against 0 while packed).

The unwrapped binary is not redistributable and is not in this repository. Every address
quoted is an RVA, re-derivable from a local copy.

## 12. Consequences for tooling

The gate defaults ON, so this data is live. A mod that adds a creature, alters a race's
movement speeds, or renumbers a shared graph's species keys leaves this file stale, and
nothing currently reads or writes it.

A project absent from the table is not an error and will not be reported as one: the
modifier passes `goalSpeed` through unchanged (§4.2). The failure mode is a wrong gait
mix and foot sliding, not a crash.

For movement types do not resolve through RACE: the project's own root graph names them
(§3.1), which covers 49 of 49 and distinguishes states within a project as a race link
cannot. Resolving through the race also picks the wrong record for the player, whose
highest `ForwardRun` is `NPC_Horse_MT` — the mounted type, at double the on-foot speed.

Implementing §1 is sufficient to read and rewrite the file losslessly; §3 to interpret
it; §6 and §8 to generate one.

`HKSK.Cache.SpeedDataFile` does the first two. `SkyrimCache.Load` picks the file up from
the meshes folder when it is there and `SkyrimCache.Save` writes it back; the round trip
is byte-exact against the shipped file. `Sample(project, state, direction, goalSpeed)`
answers the query the engine makes (§4.2), and returns `goalSpeed` unchanged for an
absent project, state or curve — which is the engine's own behaviour with no database,
and not zero, which would model a creature that cannot move.

Generation (§8) is not implemented: it needs `top(s)`, which is still authored (§9).

## 13. Entry census

Every entry in the file against the movement type its key resolves to, in file order.
86 of the 88 resolve; the two that do not are the Falmer's corrupt keys (§10), which
carry no points and so have no bounds either.

Speeds are the `MOVT` translation speeds in game units/s. The three rotation fields are
degrees/s on a different axis (§5.2) and are omitted.

- **`max x`** takes 8 distinct values and is shared by all 19 records of an entry (I8).
- **`min x` is 0 on 84 of the 86 populated entries**; the exceptions are `DeerProject`
  21 at 400 and `GiantProject` 2 at 150 (§8).
- **`min y` is above 0 on 82 of the 86.** Of the four that reach 0, three are zero
  throughout — `AtronachStormProject`, `WispProject`, `WitchlightProject` hover and
  carry no root motion. A generator must reproduce the all-zero curve rather than treat
  it as a gap.
- **`max y` is bounded by what the clips deliver** (I9), not by any `MOVT` field. On 24
  of the 86 it exceeds every translation speed in its own row.

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
