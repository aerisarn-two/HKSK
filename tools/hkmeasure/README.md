# hkmeasure

Measures what a synchronised `hkbBlenderGenerator` actually delivers, by running one
inside Havok Behavior's own runtime rather than inferring it from the shipped cache.
It exists to check §6 of `docs/speed-data.md` against the thing it models.

It is not part of the library and nothing in HKSK depends on it: it needs a 32-bit
Havok Behavior 6.6 install, and it is here because the result is only as good as the
method that produced it.

## What it needs

    HavokAssembly.DLL, msvcr80d.dll, msvcm80d.dll, Microsoft.VC80.DebugCRT.manifest

from `Havok Behavior/bin/Release/`, all beside the built `hkmeasure.exe`. The project
targets `net48` / x86 and references `HavokAssembly` with `<Private>false</Private>`.

## Running it

Two phases, because `hkaDefaultAnimatedReferenceFrame` — the class that carries root
motion — has no managed constructor, so the clips are given their travel as text:

    hkmeasure emit rig.xml 1.0 0.8          # skeleton + two clips, XML packfile
    python3 patch_rf.py rig.xml rigrf.xml 100,300
    hkmeasure rigrf.xml 20                  # sweep the blend parameter, 21 points

`patch_rf.py` takes the travel of each clip over its own duration, so `1.0/100` and
`0.8/300` are clips of 100 and 375 units per second. The sweep prints the delivered
speed, what §6 predicts, and the blend weight implied by the measurement.

`FLAGS=11` (hex) is the shipped ladder configuration and the default; `FLAGS=31` adds
`FLAG_IS_PARAMETRIC_BLEND_CYCLIC`, which is what the direction compass uses and which
makes a two-rung ladder degenerate — knots at 0 and 1 are the same point on a circle.
`SOLO=0` makes one clip the root generator, bypassing the blender. `SAMPLE=n` sets the
sampling window in frames.

## Patching the host

Two edits to a **copy** of `HavokAssembly.DLL` — never the install:

    python3 neuter.py   HavokAssembly.DLL out.dll 0xf3568   # stub moveFloorUnderCharacter
    python3 timestep.py out.dll            out.dll 0xf762c   # pass the timestep through

The first is for Wine, whose Mono rejects that method's IL; it is the physics floor
under a character and nothing here uses it. The second matters everywhere:
`Methods.generate` hard-codes a `0.0f` timestep where it calls
`hkbBehaviorGraph::generate`, because the Tool advances time from its own timeline. A
headless caller poses the graph forever at t=0 without it.

The RVAs are for Havok Behavior 6.6.0's `HavokAssembly.DLL`; both scripts verify what
they are overwriting and refuse to guess.

## Two traps that look like results

- **Never read `worldFromModel` as an accumulated position.** It is float32, and at a
  few hundred units a second it stops resolving a 1.7-unit increment inside a minute.
  That manufactures a residual of about 1e-4 that looks exactly like a weighting
  constant. Reset the character to the origin each frame and sum the deltas in double.
- **`activate()` clones the node tree.** Set `m_blendParameter` on the template before
  the clone is taken, or the sweep comes out flat.
