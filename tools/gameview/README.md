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
