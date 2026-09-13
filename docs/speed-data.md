# speeddatasinglefile.txt — speed sampler database

    Status:    format CONFIRMED (byte-exact round trip); semantics part INFERRED
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

Parameters, from `bcbehavior.hkb` (the one authoring file that shipped):

    m_enable      bool            = true
    m_state       hkFloatVariable -> iState
    m_direction   hkFloatVariable -> Direction      range [0, 1]
    m_goalSpeed   hkFloatVariable -> Speed          range [0, 384] (this graph)
    m_speedOut    hkFloatVariable -> out

Three inputs; the file nests three deep. Variable ranges exist only in `.hkb`:
`m_wordMinVariableValues` / `m_wordMaxVariableValues` are empty (count 0) in all
8 compiled graphs inspected, while `m_variableInitialValues` is fully populated.

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
| I4 | `direction[i] == 0.05 * i`, in order, complete | 86/86 entries |
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

### 4.3 x — goal speed, INFERRED (strong)

Structural: the modifier takes exactly three inputs. `state` selects the entry,
`direction` selects the record, so `goalSpeed` is what indexes within one. No
fourth input exists.

Statistical: race movement speeds fall on x knots far past chance (§5).

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
| O1 | What sets an entry's ceiling. | Not race max (23 counterexamples). Not the graph's `Speed` bound — stripped from compiled `.hkx`; the one shipped `.hkb` says 384 where its curve stops at 324.5. |
| O2 | What generates the knots between race thresholds. | Not clip root-motion speeds. |
| O3 | Why `y > x` (player answers 395.94 to a 324.5 ceiling). | Not a unit error — I8 shows y is a reachable speed. |
| O4 | Why direction stops at 0.90. | |
| O5 | Whether a computed path exists, selected against the DB by the INI gate. | **Not testable statically.** The shipped exe is Steam-wrapped (`.bind` section); `.text` is encrypted at rest — 0 RIP-relative references to the setting object, and disassembling the modifier's vtable slots returns noise. Consistent with `BSISpeedSamplerDB` being an interface, but undemonstrated. |

## 9. Consequences for tooling

The gate defaults ON, so this data is live for every actor whose graph carries the
modifier. A mod that adds a creature, alters a race's movement speeds, or
renumbers a shared graph's species keys leaves this file stale, and nothing
currently reads or writes it.

Implementing §2 is sufficient to read and rewrite the file losslessly; §4 is
sufficient to interpret it; O1 and O2 are required to *generate* one from scratch.
