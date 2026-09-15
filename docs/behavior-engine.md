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
| `hkbStateMachine` | the state whose id is named — §4.1 | reflection + corpus |
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

### 4.3 Two things the files do not say

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

## 5. Open

- The engine has no clock yet; tier 1 needs one only for `hkbTimerModifier` and
  the transition durations.
- `cond()` in expressions is a Bethesda extension — 6.6's compiler emits zero
  tokens for it — so the expression evaluator cannot be validated against the
  oracle for the graphs that use it.
- `hkbBehaviorReferenceGenerator` crosses files; `ProjectWalk` already resolves
  those, but the engine needs the same resolution at run time.
