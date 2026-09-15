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

## Four things the reader insists on

Each was found by disassembling the failure, and none of them announces itself:

- **`classversion` must be 2..7.** At 8 the reader skips reading `contentsversion`
  and then strdups the null it left behind. 6.6 writes 4; 8 is 2010.2, Skyrim's.
- **`hkRootLevelContainer`'s signature is `0xf598a34e`** in this version.
- **`numTracks` must be 14**, the class default. At 0 the track buffer lands
  8-aligned and a `movaps` faults on a worker thread, which Wine reports as a read
  of `0xffffffff`.
- **The character asset is not in the project.** `Characters/<name>.hkx` is the rig
  scene; HBT exports `hkbCharacterData` separately, so `gvchar.py` writes it.

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
