# Driving GameView

`GameView_Release.exe` is the standalone runtime Havok Behavior Tool launches for
Preview. It is the only way to execute a behaviour graph outside the Tool, which
makes it the oracle an evaluator written here is measured against — so it has to
run without the Tool, headless, on files we generate.

It does:

    cp -r ".../Havok Behavior/gameview" gv && cd gv
    python3 gvchar.py  GameView_Release.exe Capt_Hi Capt_Hi.hkx SampleBehavior.hkx \
        '..\..\Shared Animations\clip.hkx=clip.hkx' > <project>/Characters/Char.hkx
    python3 gvconfig.py GameView_Release.exe 'proj/' 'Lesson01.hkx' 'Char.hkx' \
        > Gameview/GameViewConfig.xml
    echo '-r null -g GameView -bload -bconfig Gameview/GameViewConfig.xml' > hkdemo.cfg
    env -u DISPLAY wine GameView_Release.exe

`hkdemo.cfg` overrides argv entirely, so a stale one silently ignores everything
passed on the command line.

## Reflection is built at run time

Havok 6.6 stores no static `hkClass` tables. Generated code calls the `hkClass`
constructor with thirteen pushed arguments and a BSS `this`; scanning for those call
sites is what `hkreflect.py` does, and it recovers 15932 classes with their members,
types and offsets. Searching instead for a class-name string finds only version
registry entries and memory-tracker layouts — plausible, and not the definitions.

The versioner keeps historical copies of a class under the same name. The live one
is the instance whose member table lies in `.rdata`.

## Five things the reader insists on

Each was found by disassembling the failure, and none of them announces itself:

- **`classversion` must be 2..7.** At 8 the reader skips reading `contentsversion`
  and then strdups the null it left behind. 6.6 writes 4; 8 is 2010.2, Skyrim's.
- **`hkRootLevelContainer`'s signature is `0xf598a34e`** in this version.
- **`numTracks` must be 14**, the class default. At 0 the track buffer lands
  8-aligned and a `movaps` faults on a worker thread, which Wine reports as a read
  of `0xffffffff`.
- **The character asset is not in the project.** `Characters/<name>.hkx` is the rig
  scene; HBT exports `hkbCharacterData` separately, so `gvchar.py` writes it.
- **The graph must declare at least one variable.** With an empty variable table
  nothing is instantiated and no `hkbBehaviorGraph` is ever found in memory, with
  no message of any kind -- the demo comes up and runs an empty world. One unused
  variable is enough. The demo config binds twelve `stickVariables` by name, which
  is the likeliest reason.

Class defaults are worth reading rather than guessing: `hkClass::m_defaults` is one
int per member (-1 for none) followed by the values.

## Paths

A project's search paths are the **absolute** authoring paths, baked into its
`hkbProjectStringData` — `C:\work\tremor\...` for the shipped tutorials. `rootPath`
in the config does not override them, so symlink them into the Wine prefix.

## Reading the state out of the running evaluator

`livestate.py` prints the live graph: the variable table, and every node with
its runtime fields.

    hkbBehaviorGraph @0x2c10100
      variables: [0.25, 0.75]
       hkbBlenderGenerator      numActiveChildren: 2  indexOfSyncMasterChild: -1
         hkbBlenderGeneratorChild  weight: 1.0  isActive: 1
           hkbClipGenerator       userControlledTimeFraction: 0.25
         hkbBlenderGeneratorChild  weight: 1.0  isActive: 1
           hkbClipGenerator       userControlledTimeFraction: 0.75

Which arm of a blend is live, which state a machine is in, how long it has been
there -- `numActiveChildren`, `isActive`, `weight`, `currentStateId`,
`previousStateId`, `timeInState` -- are all reflected members, so the reflection
that writes the input files also reads the state back.

Three things make it work. The runtime keeps a pointer map from vtable to
hkClass, and in memory an entry is the two words side by side, so any object
can be named from its first word; an hkClass is itself `{name, parent}` and
looks the same, which is why keys that are known class addresses are rejected.
The node tree is walked through the reflected child members rather than the
internal active-node list, which is not reflected. And Yama restricts ptrace to
descendants, so the tool launches the runtime itself instead of attaching.

## Writing a behaviour to run

`gvbehavior.py` emits the graph itself, so a test can describe a tree in Python
and have the runtime evaluate it:

    Behaviour(variables={'Left': 0.0, 'Right': 0.0},
              root=Blend('Root', [(1.0, Clip('ClipL', anim_a)),
                                  (1.0, Clip('ClipR', anim_b))]))

Every member comes from the runtime's own class definition, in its order, with
its enum values and -- this matters -- **its defaults**. Zero is a real value in
Havok, so a member with no declared default is not a member defaulting to zero:
`indexOfSyncMasterChild` defaults to -1, and emitting 0 instead sends the
blender indexing its sync master into a null child array. `hkClass::m_defaults`
is one int per member, -1 where there is none, followed by the values.

Two things about loading it. The reader is chosen by **extension**: a generated
graph named `.hkx` takes the binary path, whose contents version comes back null
and is then strcmp'd, so write `.xml`. And a node's children only exist if the
child member is actually set -- a blender whose arms are built but never
assigned loads happily and evaluates nothing.

## Running a tree and tracing it

`runtest.py` stages the three files, runs the graph and samples the live nodes:

    --- sample 0
       hkbBlenderGenerator  blendParameter: 0.42  numActiveChildren: 1
         hkbBlenderGeneratorChild  weight: 1.0  isActive: 1
           hkbClipGenerator  mode: 1  time: 8.096
         hkbBlenderGeneratorChild  worldFromModelWeight: 1.0
           hkbClipGenerator  mode: 1  time: -1.0

Clip time advances between samples, and the zero-weight arm never starts --
`numActiveChildren: 1`, no `isActive`, `time` still -1. Which arm the evaluator
chose is read off, not inferred.

A run is driven by **writing node members**: those writes persist.
`blendParameter` above is ours.

## Enums must go into the file by name

This build leaves the per-member enum pointer null, so the reader resolves an
enum against its own class -- **by name only**. A bare number is read as zero
and nothing complains.

That is what stopped every binding on a generated graph. `variableInfos[].type`
went out as `4` and was read as `0`: `VARIABLE_TYPE_REAL` became
`VARIABLE_TYPE_BOOL`. The binding itself was fine -- resolved exactly like a
working one, `offsetInObjectPlusOne` 53 and `memberType` 11 -- and
`copyVariablesToMembers` ran every frame; it just switched on the variable's
declared type, found BOOL, and never wrote the real member. With no node using
them the variables were then discarded each frame, which looked like something
overwriting them.

`hkbVariableBindingSet` copies per binding: it resolves `offsetInObjectPlusOne`
lazily if zero, skips the binding if it stays zero, branches on `bindingType`,
and then reads `variableInfos[variableIndex].type` out of the graph's data to
choose how to copy. That last read is the one that was being fed a zero.

The emitter now writes enums by name and raises on an integer it cannot name,
so this cannot recur silently.

## The runtime tells you none of this by itself

Everything above is read out of its memory. It reports nothing. Checked and
ruled out:

- `-rl0..5` is the physics report level; it prints nothing here and writes no file.
- The statistics packet is the monitor stream: 71 named timers a frame, but they
  name classes and phases (`hkbClipGenerator::generate`, `UpdateActiveNodes2`),
  never instances. Only the `Lt`/`St`/`Tt` commands appear -- no
  `TimerBeginObjectName` -- so no node, state, variable or transition is named.
  Searching a frame for the graph's own node and variable names finds nothing.
- The `Behaviors` viewer sends no text; it draws through the debug display.
- `ObjectInspection` publishes one top-level object, an `hkpRigidBody`.
- `-video` writes frames, but under Wine they come out black.

So what the runtime yields is the evaluated pose, plus a profile of which code
paths ran. Which node was active has to be inferred from the pose -- run a
candidate generator alone and compare -- rather than read.

## Measuring a semantic rule against the runtime

`triggertest.py` is the worked example, and the pattern generalises: describe two
graphs that differ in exactly the thing being tested, run each, and read the
member that answers.

    python3 triggertest.py /tmp/claude-1000/gv

    with an end-of-clip trigger   currentStateId over time: [1, 1, 1, 1, 1, 1, 1, 1]
    without one                   currentStateId over time: [0, 0, 0, 0, 0, 0, 0, 0]

A two-state machine whose first state plays a clip carrying one trigger at
`relativeToEndOfClip`, raising the only event, with the only transition firing on
that event. Nothing else can move it, so state 1 means the trigger fired on its
own. That is `ActiveGenerators.Finished` measured rather than reasoned.

`syncmodetest.py` is the same pattern on the other rule a state machine rests by:

    START_STATE_MODE_DEFAULT   startStateId=0, sync variable=1 -> currentStateId [0, 0, 0, 0]
    START_STATE_MODE_SYNC      startStateId=0, sync variable=1 -> currentStateId [1, 1, 1, 1]

so `m_syncVariableIndex` is consulted in that mode and nowhere else, which is
`ActiveGenerators.StateIdOf`. **Declare the variable as an int**: `Behaviour`
types a variable from the Python value it is given -- `bool`, `int`, anything else
-- and a state id read out of a float's bits lands nowhere.

## Staging it again

The tutorial assets live outside the Wine prefix and the staging directory is in
`/tmp`, so it does not survive a reboot:

    SRC="$HOME/.wine/drive_c/Program Files (x86)/Havok/Havok Behavior/gameview"
    TUT="$HOME/Documents/Havok/Havok Behavior 6.6.0/Tutorial Projects/Tutorial01/Lesson01"
    GV=/tmp/claude-1000/gv
    mkdir -p "$GV" && cp -r "$SRC"/* "$GV"/ && cp -r "$TUT" "$GV/proj" && mkdir -p "$GV/Gameview"

`c:/work/tremor/Tutorial Projects/Tutorial01/Lesson01` must symlink to `$GV/proj`
and `.../Shared Animations` to the real one, because the search paths are baked
into the project's own `hkbProjectStringData` and the config's `rootPath` does not
override them.
