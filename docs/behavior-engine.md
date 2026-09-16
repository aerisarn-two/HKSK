# The behaviour engine — what Skyrim's graphs are made of, and what we evaluate

    Status:   census CONFIRMED (taken from the 49 projects, not from a list)
              engine IN PROGRESS -- see §4 for what is implemented
    Source:   the 49 actor projects under meshes/actors, via HKSK.Behavior.ProjectWalk
    Oracle:   Havok Behavior 6.6 under Wine -- tools/gameview, reverse-hbt-6.6.md
    Census:   HKSK.Tests.NodeCensusTests

Reading a behaviour graph is solved: `ProjectWalk` crosses files and reaches every
object of all 49 projects. **Running** one is not, and everything the speed-data
work still cannot derive comes back to the same question — *given the variables,
which generator is actually active?* That is what this engine is for.

## 1. Why not a full animation engine

The question is about **selection**, not about poses. Which state a machine is in,
which arm of a blend carries weight, which clip a selector picks — those decide
which animation the game samples, and therefore which speed it travels at. The
pose itself, the ragdoll, the IK, the look-at: none of them feed back into that
choice.

So the engine is scoped in tiers, and the tiers are a scope decision rather than a
schedule:

| tier | what | why |
| --- | --- | --- |
| 1 | selection — variables, bindings, expressions, events, state machines, blends, selectors | answers the question |
| 2 | pose — clip sampling, blend maths, root motion | needed to *measure* speed rather than name a clip |
| 3 | ragdoll, foot IK, look-at, twist | never changes which generator is active |

Tier 3 nodes still have to be **recognised and traversed** — a modifier list
containing a foot-IK modifier must not stop the walk — but their effects are not
modelled.

## 2. The census

86 classes appear across the 49 projects. By what they derive from:

    14  generator     32  modifier      1  transition effect
     2  condition      5  bindable     32  data

### Generators

| instances | projects | class |
| ---: | ---: | --- |
| 12258 | 49 | `hkbClipGenerator` |
| 4378 | 49 | `hkbStateMachine` |
| 2550 | 49 | `hkbBlenderGenerator` |
| 2311 | 49 | `hkbModifierGenerator` |
| 791 | 48 | `hkbManualSelectorGenerator` |
| 769 | 26 | `BSSynchronizedClipGenerator` |
| 249 | 8 | `BSiStateTaggingGenerator` |
| 203 | 30 | `BSCyclicBlendTransitionGenerator` |
| 201 | 6 | `BSBoneSwitchGenerator` |
| 125 | 49 | `hkbBehaviorGraph` |
| 123 | 13 | `hkbBehaviorReferenceGenerator` |
| 97 | 47 | `hkbPoseMatchingGenerator` |
| 3 | 3 | `BSOffsetAnimationGenerator` |
| 1 | 1 | `hkbReferencePoseGenerator` |

### Modifiers

| instances | projects | class | tier |
| ---: | ---: | --- | :-: |
| 745 | 49 | `BSIsActiveModifier` | 1 |
| 738 | 49 | `hkbModifierList` | 1 |
| 491 | 47 | `hkbEvaluateExpressionModifier` | 1 |
| 168 | 48 | `hkbEventDrivenModifier` | 1 |
| 132 | 19 | `BSEventEveryNEventsModifier` | 1 |
| 123 | 35 | `BSLookAtModifier` | 3 |
| 93 | 47 | `hkbPoweredRagdollControlsModifier` | 3 |
| 91 | 48 | `hkbKeyframeBonesModifier` | 3 |
| 71 | 10 | `BSEventOnDeactivateModifier` | 1 |
| 65 | 47 | `hkbTimerModifier` | 1 |
| 55 | 48 | `hkbRigidBodyRagdollControlsModifier` | 3 |
| 52 | 39 | `BSRagdollContactListenerModifier` | 3 |
| 46 | 46 | `hkbGetUpModifier` | 3 |
| 44 | 41 | `BSSpeedSamplerModifier` | 1 |
| 38 | 7 | `BSInterpValueModifier` | 1 |
| 37 | 18 | `hkbDampingModifier` | 1 |
| 33 | 5 | `BSDirectAtModifier` | 3 |
| 29 | 8 | `BSEventOnFalseToTrueModifier` | 1 |
| 28 | 19 | `BSModifyOnceModifier` | 1 |
| 26 | 23 | `hkbFootIkControlsModifier` | 3 |
| 24 | 9 | `hkbTwistModifier` | 3 |
| 9 | 3 | `hkbRotateCharacterModifier` | 3 |
| 5 | 3 | `hkbEventsFromRangeModifier` | 1 |
| 5 | 1 | `BSTweenerModifier` | 1 |
| 3 | 3 | `BSIStateManagerModifier` | 1 |
| 2 | 1 | `hkbFootIkModifier` | 3 |
| 1 | 1 | `BSGetTimeStepModifier` | 1 |
| 1 | 1 | `BSPassByTargetTriggerModifier` | 3 |
| 1 | 1 | `BSDecomposeVectorModifier` | 1 |
| 1 | 1 | `BSTimerModifier` | 1 |
| 1 | 1 | `BSLimbIKModifier` | 3 |
| 1 | 1 | `hkbTransformVectorModifier` | 1 |

### The rest

One transition effect, `hkbBlendingTransitionEffect` (1258 / 49). Two conditions,
`hkbExpressionCondition` (370 / 42) and `hkbStringCondition` (9 / 5). The
remaining 37 are data carried by the above — event properties, clip triggers,
state infos, transition infos, binding sets, bone weights, expression data.

**Half the Bethesda nodes are rare.** Eight `BS*` classes appear in a single
project, and the whole long tail below 40 instances is 17 classes. The five
classes above 2000 instances are 41,000 of the 74,000 executable nodes.

## 3. Where the semantics come from

Three sources, in descending order of trust, and each claim in §4 says which:

1. **Measured** against the 6.6 runtime through `tools/gameview` — the strongest,
   but only covers stock `hkb*` nodes, since 6.6 has no `BS*` classes at all.
2. **Read** from Havok's own headers. `ck-cmd/vendor/havok_2010_2_0/Source`
   carries Common, Animation and Physics — but **no Behavior module**, so it
   documents `hkArray`, the visual debugger and the animation containers, not the
   graph. The 6.6 binary's own reflection (`tools/gameview/hkreflect.py`) is the
   substitute, and it gives members, offsets, enums and defaults exactly.
3. **Inferred** from the shipped graphs — what a field can be, given every value
   the 49 projects put in it. Weakest, and always marked.

## 4. Implemented

`HKSK.Engine`. Tier 1, selection only: given a project and its variables, which
generators the graph is evaluating at rest.

| class | how it selects | source |
| --- | --- | --- |
| `hkbBehaviorGraph` | its `rootGenerator` | reflection |
| `hkbStateMachine` | the state whose id is named — §4.1 | **measured** |
| `hkbBlenderGenerator` | every child whose weight, bound or stored, is above zero | reflection |
| `hkbPoseMatchingGenerator` | **derives from `hkbBlenderGenerator`**, so the same rule | census |
| `hkbModifierGenerator` | passes through to its generator | reflection |
| `hkbManualSelectorGenerator` | `selectedGeneratorIndex`, bound or stored | reflection |
| `BSiStateTaggingGenerator` | its default generator | reflection |
| `BSCyclicBlendTransitionGenerator` | its blender | reflection |
| `BSBoneSwitchGenerator` | its default plus every bone-data child | reflection |
| `hkbBehaviorReferenceGenerator` | the named file's graph, resolved as `ProjectVisitor` resolves it | measured |
| `hkbClipGenerator`, `BSSynchronizedClipGenerator`, `BSOffsetAnimationGenerator`, `hkbReferencePoseGenerator` | leaves: they sample, they do not select | census |

Supporting: `hkbVariableBindingSet` for the members that select,
`hkbBehaviorGraphData` / `StringData` / `VariableValueSet` / `VariableInfo` for the
variable table.

Held by `ActiveGeneratorTests`: all 49 projects resolve, **no state machine
anywhere fails to pick a state**, and every project selects down to at least one
clip.

### 4.1 Modifiers, expressions and character properties

`hkbEvaluateExpressionModifier` and the expression language are implemented, with
`hkbModifierList` and `BSIStateManagerModifier`. The grammar is taken from the
**261 distinct expressions the game ships** (`ExpressionCensus`): two forms,
`variable = expression` and `Event if condition` -- the condition's parentheses
are optional -- seven functions (`cond`, `fabs`, `clamp`, `max`, `min`, `sind`,
`cos`) and the usual operators. All 261 parse (`TheWholeCorpusParses`).

Three things about it:

- **`m_assignmentVariableIndex` is -1 on all 691 expressions.** The runtime
  resolves the name at activation and the file carries nothing, so the evaluator
  resolves names itself.
- **A variable name may begin with digits.** `1stPRot` is real; a leading run of
  digits is a name when a letter follows and a number when it does not.
- **`cond` is Bethesda's.** Havok 6.6's compiler emits zero tokens for it, so it
  cannot be checked against the oracle. It is a three-argument select, and only
  the branch taken has to resolve -- a shared file names constants that only some
  of its creatures declare.

Modifiers run on the way down, before the generator they sit above, and the walk
iterates to a fixed point because what a modifier writes decides what the machines
below it select.

**Which id is named** depends on `m_startStateMode`: the machine takes
`m_startStateId` unless the mode is `START_STATE_MODE_SYNC`, and only then is
`m_syncVariableIndex` consulted. That guard decides where two creatures rest --
the riekling's combat machine is in sync mode on a variable that starts at its
equip state, and the benthic lurker's locomotion machines are in it on `iState`,
which is what lets their keys be read off their state ids.

**Measured** (`tools/gameview/syncmodetest.py`): one machine, two states,
`startStateId` naming the first and the sync variable naming the second.

| `startStateMode` | runtime rests in |
| --- | --- |
| `START_STATE_MODE_DEFAULT` | state 0 — the variable ignored |
| `START_STATE_MODE_SYNC` | state 1 — the variable wins |

The variable has to be declared `VARIABLE_TYPE_INT32`, since a state id read out
of a float's bits lands nowhere.

### 4.2 How one file serves ten creatures

Ten quadrupeds load `quadrupedbehavior.hkx`, whose root modifier list holds a
modifier list per creature -- Bear, Canine, Cow, Deer, Goat, Horker, Mammoth,
SabreCat, Skeever -- **every one of them stored as enabled**. What picks one is a
binding on `enable` of type `BINDING_TYPE_CHARACTER_PROPERTY`, and the properties
are named `IsBear`, `IsDeer`, `IsSkeever` and so on.

**Character properties are not in the behaviour graph.** They live in the
character file's `hkbCharacterData`, which visiting the graph never reaches, so
the engine loads it separately. Read the field instead of the binding and all
nine creatures' modifiers run: the deer came out with the skeever's `iState`.

Values are shared by name across a character's files while indices stay per file,
and the same holds for property indices. `iState_DeerDefault` is 20 in the deer's
own file and 10 in the shared one, and it is the deer's that the speed tables are
numbered by.

Held by `TheDeersStateFollowsItsSpeed`: the deer's `iState` is **20 at rest and 21
at speed 300**, through `iMovementSpeed = cond((Speed < 100), 0, 1)` and
`iState = iState_DeerDefault + iMovementSpeed`.

### 4.3 Events, transitions and the two blend kinds

Events are raised by name and the graph is read where it settles. Ids are per
file like everything else, so what is shared between files is the name.

A machine follows transitions from its start state while a raised event keeps
taking it somewhere new: its own state's transitions first, then the machine's
wildcards, higher `m_priority` first within each. Two flags from the runtime's
own `TransitionFlags` matter and are honoured -- `FLAG_DISABLED` (32) and
`FLAG_DISABLE_CONDITION` (256). `hkbExpressionCondition` is read as a boolean;
`hkbStringCondition` carries a string the runtime hands to the game, so nothing
in the files says what it means and it is treated as holding.

**A blend is one of two quite different things**, and the flag says which.
`FLAG_PARAMETRIC_BLEND` (16) means the children are not mixed: each child's
weight is a position on an axis and `blendParameter` picks the two it falls
between. An ordinary blend mixes all its children, normalised to sum to one.
Treating a parametric blend as an ordinary one gives weights in the thousands
and every arm live at once.

### 4.4 Where the speed file joins the graph

The deer, driven with `moveStart`:

| | |
| --- | --- |
| at rest | `CLIP_Idle_Default` |
| moving, `Speed` 50 | `iState` 20, `WalkSlowForwardL` |
| moving, `Speed` 300 | `iState` 21, `SlowRunForwardL` |

Two mechanisms, one above the other. **`iState` picks the block**:
`iMovementSpeed = cond((Speed < 100), 0, 1)` moves the deer from the walk block
to the run block at 100. **A parametric blend picks the rung within it** -- and
its parameter is not `Speed` but **`SpeedSampled`**, which is the value
`BSSpeedSamplerModifier` writes by looking the creature up in
`speeddatasinglefile.txt`. Below that sits a second parametric blend on
`TurnDeltaDamped`, knotted at 270, 0, -270: a speed ladder over a direction
compass, which is the shape the file records.

### 4.5 The sampler, and the whole join

`BSSpeedSamplerModifier` is implemented, so nothing has to be told which rung to
stand on. It takes `state`, `direction` and `goalSpeed` in through bindings --
`iState`, `Direction`, `Speed` -- and writes its answer **out** through the
binding on `speedOut`, which is `SpeedSampled`. Output bindings are not marked in
the file, since the flags that would say so are computed at activation, so which
members are outputs is known per node.

The query itself was already read out of the game binary and lives in
`SpeedDataFile` (`docs/speed-data.md` §4.2): exact key or nothing, the heading as
a ceiling that wraps, and past the last point the goal passes through unchanged.
The engine only supplies the inputs and takes the answer.

The deer, driven with `moveStart`, `Direction` 0, and the shipped table:

| `Speed` | `SpeedSampled` | live clips |
| ---: | ---: | --- |
| 0 | 4.924 | `WalkSlowForwardL` |
| 50 | 8.523 | `WalkSlowForwardL`, `WalkForwardL` |
| 100 | 97.875 | `SlowRunForwardL` |
| 450 | 431.9 | `SlowRunForwardL`, `RunForwardL` |
| 900 | 900 | `RunForwardL` |

Three separate mechanisms are visible in that table. **`iState` changes the block
at 100** -- a different ladder, not a different rung. **The table maps the goal**,
50 down to 8.5 and 450 down to 431.9. **The ladder's knots bracket the answer**:
431.9 is just past 416.5, so two rungs blend; at 900 the query passes the goal
through untouched and the top rung carries it alone.

That is the join, end to end, from a raw speed to which animation plays, computed
from the shipped files and nothing else.

**It reaches 47 of the 49 projects** at a goal of 200. The two it does not are one
whose graph computes an `iState` its block has no entry for, and one with no
sampler. At a goal of 600 only 20 are driven, because most creatures' curves end
below that and the query then passes the goal through -- the documented behaviour,
not a gap. Held by `SpeedSamplerEngineTests`.

`SpeedSampledPicksTheRung` isolates the ladder by leaving the table out: with no
block the sampler passes the goal through, so `SpeedSampled` *is* `Speed` and the
ladder can be driven directly. Worth knowing when writing such a test -- the
sampler overwrites whatever `SpeedSampled` was set to, so setting it by hand does
nothing once the modifier runs.

### 4.5 Two things the files do not say

**Variable indices are per file.** Each behaviour packfile carries its own
`hkbBehaviorGraphData`, so a binding's `variableIndex` means nothing except
against the table of the file the bound node lives in. Resolving a referenced
file's bindings against the root's table reads a different variable entirely: on
the humanoids it turned `DrunkBehavior`'s `startStateId` into `fSpeedMin`, and the
whole graph below it went unreached — 15 active nodes and no clip, against 31 and
a clip once fixed.

**`syncVariableIndex` is only consulted in `START_STATE_MODE_SYNC`.** The field is
set on machines that do not use it, so reading it unconditionally picks an
arbitrary state. And when the synced value names no state — which is what the
vampire brute's initial values give — the machine falls back to its own
`startStateId` rather than to nothing.

Both were found by running the engine over the corpus and asking which machines
resolved to nothing, which is what the census and the corpus test are for.

## 4.6 Two traps in the ladder finder

**An `Evaluation` carries the visit its nodes came from.** A project read twice
gives two sets of objects, so asking a *different* `ProjectWalk` where one of
these nodes lives returns nothing -- silently -- and anything filtering on that
finds no ladders at all. `ActiveGenerators.Of` builds its own walk, so a caller
that also has one must not mix them.

**Not every ladder reads the sampler.** The netch's forward blend runs on
`SpeedDamped` and the slaughterfish's on raw `Speed`, and both creatures ship a
speed table regardless. Accepting only the sampler's output finds no ladder for
them, so the search falls back through `SpeedDamped` and `Speed`.

## 4.7 Where the remaining curve error is

Sorting the 16,592 points by the compass they came through says where the error
lives, and it is not where it looks:

| compass | on an arm | between arms |
| --- | --- | --- |
| 1 arm | 90% | 91% |
| 4 arms | 82% | 76% |
| 8 arms | 73% | 69% |
| 24 arms | 100% | 49% |

A heading that lands between two arms fares only about five points worse than
one that lands on an arm, so **interpolation is not the problem** -- identifying
the ladder is.

The 24-arm row was the exception and it is now fixed. A state can hold more than
one compass: the player's `DefaultBlockLocomotion` holds three -- one-handed,
two-handed and bow, eight arms each and near enough identical -- because which
one runs is a weapon type the graph is told rather than something it decides.
Reading the state's blends as one compass put three arms on every heading, so a
heading between two interpolated between *different weapons*: exact on an arm,
half wrong between them. Keeping the compasses apart and taking the widest takes
key 4 from 146 of its 247 points to all 247.

Nothing delivers zero any more, and the median ratio against the shipped file is
within a percent of 1 on almost every key, so where a block is wrong it is wrong
in shape rather than in scale. The two exceptions are worth naming: `HorseProject`
sits at a near-constant 0.41 -- its ladder delivers about 2.4x what its rung
weights say, while its shipped table is very nearly the identity -- and
`HorkerProject` at 0.62. Neither is a cache artefact: the cache's duration agrees
with the animation's own on both, checked against all 6,674 motion blocks (103
disagree corpus-wide and none of them are these).

### The ladder's floor

A ladder's lowest rung is a floor and below it the blend clamps, so on its own
the model says a creature runs at 214 units per second when asked for 50. The
benthic lurker does not: its key-1 table is the walk ladder's numbers exactly
below about 215 and the run ladder's exactly above, meeting in a step rather
than a blend. The deer, in the same shape -- two gait states in one machine with
transitions each way -- clamps instead. `docs/speed-data.md` §6.2b has both
measurements and why falling back is not in the tree.

### Not every locomotion is a ladder

Two of the player's keys held none of their 83 points and both were paired to an
attack state, because neither has a blend the sampler drives and so neither
became a locomotion state at all. `MT_Drunk_iStateGen` guards a machine whose
moving state is one clip, `IdleDrunk_Walk`; `Sprint_iStateGen` guards a blend of
two manual selectors. A creature playing one clip travels at that clip's speed
whatever it is asked for, which is a ladder of one rung and therefore flat, and
the shipped file agrees to five figures: key 15 reads 29.47 at every point
against the clip's 29.469, key 1 reads 370.37 against 370.365.

**Which clip is not guessed.** The subtree is evaluated, so the machine settles
into its moving state and the selectors resolve their own bindings. That matters:
the sprint's selector is driven by `iRightHandEquipped` and has thirteen arms,
one per weapon, and it picks the unarmed one because that is what the variable
starts at -- which is the case the shipped block records. Across the corpus the
selectors are driven by `iRightHandType` (250), `iLeftHandType` (191),
`i1stPerson` (140) and `iRightHandEquipped` (50), so reading the stored index
would have been wrong far more often than right.

Only a subtree with exactly one travelling clip and no sampler-driven blend is
answered. The second half of that is not optional: the falmer's `Bow_iStateGen`
guards eight ladders, and taking one clip out of them is a moment rather than a
curve -- it cost 150 of its points until the condition was added.

### An expression that undoes itself

`BSModifyOnceModifier` carries two modifiers, one run on entering its subtree and
one on leaving, and Bethesda uses the pair to set a variable and put it back. The
horker's swim state holds both, on one node in one list:
`HorkerSwimmingStart_EEM` writing `iState = iState_HorkerSwimDefault` and
`HorkerSwimmingStop_EEM` writing `iState = iState_HorkerDefault`.

Reading where an expression sits is how an undeclared key finds its state, and
both of these sit in the swim branch -- so `iState_HorkerDefault` was read as
naming the swim locomotion, and `HorkerProject` rebuilt its walking curve from
`ForwardSwimState`, holding 19 of its 119 points. An expression reached through
`m_pOnDeactivateModifier` describes leaving a subtree and so governs nothing;
with it ignored the key falls through to the movement-type route, which matches
`ForwardState_Horker`'s rungs to its own 32.77 and 83.41 exactly, and the block
holds all 119.

### An intro animation has finished

A graph read at rest is not a graph on its first frame, and the difference is one
rule: **a clip that has been playing has reached its end, so a trigger the author
placed there has fired.** Only those -- `m_relativeToEndOfClip` marks the event as
belonging to the clip finishing rather than to a moment inside it, which is what a
footstep or a hit frame is.

**Measured against the runtime** (`tools/gameview/triggertest.py`), which is the
strongest of the three sources in §3 and the only rule added recently that has
it. Two graphs differing in exactly this: a two-state machine whose first state
plays a clip carrying one trigger at `relativeToEndOfClip` raising the only event,
with the only transition firing on that event. Havok 6.6 rests in state 1 with the
trigger and in state 0 without it, so nothing but the trigger moved it.

Without it an intro animation holds the graph forever. The player's
`BleedOut_iStateGen` guards a machine that starts in `BleedOut_Transition_State`,
and the only way out is event 128, `bleedOut_TransInEnd`, which `BleedOut_TransIn`
raises at its own end -- so the bleedout rested in its transition-in clip and
never reached the four-way blend its table describes. The riekling was stuck the
same way in `MT_Equip`, which is why its two compasses could not be told apart by
running the graph.

Its own compass then follows: `BleedOut_Moving_Blend` is a cyclic parametric blend
of four clips at 0.25, 0.5, 0.75 and 1 with no ladder under it, because a creature
bleeding out has one animation per direction and no gait to choose. So the curve
is flat in the goal speed and varies only with the heading, and the file agrees --
20.5 forward against `BleedOut_Forward`'s 20.500, 17.96 sideways against
`BleedOut_Right`'s 17.957, and 13.51 between them, below both, which is the same
vector blend as anywhere else. A blend is read as a compass rather than a ladder
when every child weight is at most 1, which is `SpeedSampler`'s own test.

### A perk is not a stance

Two of the player's movement types change what the game *allows* rather than what
it plays. `NPCBowDrawnQuickShot` carries `NPCBowDrawn`'s four walk speeds exactly
-- 120, 65.11, 74.89, 76.81 -- and replaces only the runs, with `NPCDefault`'s 370
and 205.25; `NPCBlockingShieldCharge` does the same to `NPCBlocking`. The shipped
tables agree: key 16's records are key 3's and key 17's are key 4's, point for
point.

Neither is declared, and the speed pairing cannot help. It scores a state by how
many of a movement type's eight speeds are rungs, so for key 16 the bow ladder
matches the four walks and the default ladder matches the four runs -- it ties and
says nothing -- and for key 17 nothing matches at all.

**The masters settle it without the shipped file.** Where an undeclared key's
movement type walks at exactly the same four speeds as a declared key's, and only
one declared key does, it is that key's locomotion. Key 16 goes from 16 of its 187
points to 181 and key 17 from 38 of 247 to all 247, which takes both players to
97.3%. It is tried after the pairing rather than before, because a state whose
rungs match the movement type's own speeds is more direct evidence than two
movement types resembling each other -- the falmer has a key where the pairing is
right and this is not.

### The stance

What remains on the player is one thing: the graph is not driven into a stance.
Running it with Bethesda's movement selectors lands in default locomotion, which
is correct for someone walking about with nothing drawn, and the speed table also
records them sneaking, blocking, holding a bow and casting. So 17 of the 23 keys
the graph declares are reached by declaration and not by running it.

Searching all 301 of the player's variables against those declarations -- the
only ground truth available, since the graph says which state each key belongs
to -- finds exactly one: **`iIsInSneak = 1` reaches `Sneak_Locomotion_State`.**
No other stance falls to a single variable, which is expected: a drawn bow is a
weapon type and a draw flag together. Nothing in the tree sets any of them yet.

**508 clip generators across 26 projects bind `playbackSpeed`** -- to
`weaponSpeedMult`, `turnSpeedMult` and their kin -- and the ladder builder reads
the stored field. Only 19 of those clips are ever a ladder rung, all on the
player's keys 1 and 15, and those two keys are misidentified for other reasons,
so modelling it would change no number here. It is recorded because it is a real
gap and the next thing to trip over.

## 4.8 Which clips move the character

Selection says which animation is sampled, and that is not the same question as
how fast the creature travels. A blend mixes root motion over `weight *
worldFromModelWeight`, and the second field is a flag in practice: across the 49
projects it is 1 on 7,584 children and 0 on 668, never anything else. A child at
0 decides how the character *looks* and contributes nothing to where it goes --
the player's `MT_ForwardCameraBobBlend` is one, a whole parametric ladder that
moves nobody.

**Measured against the runtime** (`tools/hkmeasure`, `WFM=`). Two clips of equal
duration, one travelling at 100 units a second and one standing still, blended
from all of the first to all of the second:

| blend parameter | both at `worldFromModelWeight` 1 | the still one at 0 |
| --- | --- | --- |
| 0.00 | 100.0 | 100.0 |
| 0.25 | 75.0 | **100.0** |
| 0.50 | **50.0** | **100.0** |
| 0.75 | 25.0 | **100.0** |
| 1.00 | 0.0 | 0.0 |

The first column is the daedra's halving, and the second is what a child excluded
from the motion does: the character keeps its full speed however much of the pose
that child takes. At 1.00 nothing is left in the motion blend at all and the
answer is zero, which is the guard in `Moving`.

So `ActiveNode` carries `Motion` beside `Weight`: its share of the movement,
normalised over the children that are in the world-from-model blend. Two
creatures in the corpus end up below 1, and both at exactly 0.5, and they are
not the same case:

| | mixed with | share | table |
| --- | --- | --- | --- |
| `HMDaedra` | `MT Idle`, a clip that stands still | 0.5 | halved |
| `NetchProject` | a lower-body state machine that selects nothing | 1 | not halved |

**There is no such thing as a child that samples nothing**, and the engine no
longer pretends otherwise. A blend child whose generator is a state machine with
no states, or with a state whose generator is null, faults the 6.6 runtime as soon
as the child takes weight -- measured with `tools/hkmeasure`, `DEAD=`. So a rule
about branches that generate nothing had nothing to describe.

The netch looked like one. Its lower body is *the same subtree as its upper body*
-- `MoveForwardRootBehavior`, `ForwardBlend`, `NetchRunForward` at 400 units a
second, the same node objects reached twice -- so both halves of its body blend
travel at 400 and nothing is halved. What made the second half look empty was the
visit's own cycle guard, which is now scoped to the path rather than the visit,
because that is what a cycle guard is: a node may be live under two parents, and
only a path may not repeat one.

Two things depended on the old guard and both were wrong for the same reason.
A ladder live under two parents carries its share in each, so `Share` adds them
up; and a subtree that plays one clip twice is still playing one clip, so the flat
route asks whether the travelling clips **agree on a speed** rather than whether
there is only one of them -- which is also what the sprint needs, since it blends
a right-side and a left-side variant of the same stride.

With those, the corrected traversal reproduces the previous numbers exactly:
15,305 points, 1,215 records, 47 blocks. And the rule it replaces is then dead --
disabling it changes nothing at all -- so it is gone, and the daedra is still
halved for the reason it always was: `MT Idle` is a distinct clip that travels
zero.

So `ActiveNode` carries `Motion` beside `Weight`: its share of the movement,
normalised over the children that are in the world-from-model blend. Two
creatures in the corpus end up below 1, and both at exactly 0.5, and they are
not the same case:

| | mixed with | share | table |
| --- | --- | --- | --- |
| `HMDaedra` | `MT Idle`, a clip that stands still | 0.5 | halved |
| `NetchProject` | a lower-body state machine that selects nothing | 1 | not halved |

**A child that samples nothing gives its share back**, which is what the engine
does and what separates the two creatures in practice. **The mechanism is not
what it looks like**, and the difference is worth stating because it is the kind
of thing that reads as understood when it is not.

The daedra is straightforward: its sibling is `MT Idle`, a real clip that travels
zero, so it keeps its half and the creature moves at half its ladder's speed.

The netch is not. Its lower body is *the same subtree as its upper body* --
`MoveForwardRootBehavior`, `ForwardBlend`, `NetchRunForward` at 400 units a
second, the same node objects reached twice -- so both halves of the body blend
travel at 400 and nothing is halved. The reason the engine sees the second half as
empty is its own visit-wide `seen` set, which stops a node being entered twice
even under different parents. The right answer for the wrong reason.

**Havok will not run the state the rule describes.** A blend child whose generator
is a state machine with no states, or with a state whose generator is null, faults
the 6.6 runtime as soon as the child takes weight -- measured with
`tools/hkmeasure`, `DEAD=`. So "a branch that generates nothing" is not something
a shipped graph can contain, and no creature is really in that case.

Correcting the traversal was implemented and measured. Scoping the cycle guard to
the path rather than the visit -- which is what a cycle guard should be -- and
summing a ladder's motion over the instances that then appear gives **15,227
points and 1,177 records against 15,305 and 1,215**. So the corrected traversal is
not yet an improvement: something else is relying on the visit-wide set, and it
has not been found. Both changes are out of the tree and this paragraph is the
record.

Authored weight alone cannot tell the two apart. Only running the graph and
looking at what each branch actually sampled can, which is
`ActiveGenerators.Settle`. Applying it takes `HMDaedra` from 0 of 77 points to 65
and leaves `NetchProject` at 45 of 45.

## 5. Open

- The engine has no clock yet; tier 1 needs one only for `hkbTimerModifier` and
  the transition durations.
- **The expression evaluator is checked by parsing, not by evaluating.** All 261
  distinct expressions the game ships parse (`TheWholeCorpusParses`) and the
  evaluator is tested against hand-written cases, but nothing compares its answers
  with Havok's. `cond()` could never be: it is a Bethesda extension and 6.6's
  compiler emits zero tokens for it. The rest could in principle, through
  `hkmeasure`'s `states` mode, which drives a machine from an expression and
  sweeps a variable — but **that mode does not run**. `Methods.setCharacter`
  throws a null reference inside the managed assembly even with the context's
  project data and character set up, which is the same place the blend path had to
  be worked around. So the expression semantics are the weakest-evidenced part of
  the engine, and the gap is wider than `cond`.
