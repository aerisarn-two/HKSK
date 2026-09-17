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

## What a child that is not in the motion does

`WFM=` sets each child's `worldFromModelWeight`, which the corpus only ever gives
1 or 0. With two clips of equal duration -- one travelling at 100 units a second,
one standing still -- and the blend swept from all of the first to all of the
second:

    WEIGHTS=0,1 WFM=1,1 hkmeasure rigrf.xml 4      WEIGHTS=0,1 WFM=1,0 ...
    0.00  100.0                                    0.00  100.0
    0.25   75.0                                    0.25  100.0
    0.50   50.0                                    0.50  100.0
    0.75   25.0                                    0.75  100.0
    1.00    0.0                                    1.00    0.0

So root motion blends over `weight * worldFromModelWeight` and is renormalised
over what is left: a child at 0 decides how the character looks and nothing about
where it goes, and at 1.00 there is nothing in the motion blend at all and the
answer is zero. Equal durations are deliberate -- a synchronised blend also
interpolates duration, and equal ones take that out of the measurement.

This is `HKSK.Engine.ActiveGenerators.Moving`, and it is why `HMDaedra` travels at
half what its ladder delivers while `NetchProject` does not.

## Bone weights do not reach root motion

`BONES=` gives each child a bone weight array with the given weight on the root
bone (`Methods.setBoneWeight`), or none with `-`. The chaurus flyer layers its
locomotion under partial-body idles this way, and the question was whether a child
that leaves the root out also leaves the motion. With a non-parametric blend of two
equal-weight children, one travelling at 370 and one standing:

    BONES=-,-    185.0        BONES=1,0    185.0        BONES=0,0    185.0
    BONES=1,1    185.0        BONES=0,1    185.0

The same in every case: root motion is weight times `worldFromModelWeight`,
renormalised, and the bone weights shape the pose alone. The tool does not show
independently that the array took effect on the pose; it shows that motion did not
move.

## What Havok does with a child that generates nothing

`DEAD=<index>` gives that child a state machine instead of its clip -- one with no
states, or with a single state whose generator is null. Either way the runtime
**faults** as soon as the blend parameter gives that child any weight: the sweep
prints its first row, at the parameter where the child has none, and dies.

That is worth knowing before modelling a branch as producing nothing. It is not a
state a shipped graph can be in, so where the engine sees one it is looking at
something else -- for the netch, at the same subtree reached twice.

## The `states` mode: an oracle for the expression language

    EXPR="sel = Speed > 100|iState = iState_Base + sel" hkmeasure states rigrf.xml 0 200 5

builds a state machine driven by an `hkbEvaluateExpressionModifier`, sweeps
`Speed` between the two bounds, and prints what Havok made of it:

         Speed     sel   iState   stateId  stateName
             0       0       20         0  WalkState
            40       0       20         0  WalkState
            80       0       20         0  WalkState
           120       1       21         0  WalkState
           160       1       21         0  WalkState
           200       1       21         0  WalkState

so a comparison yields 1 or 0 and is a value like any other. Sweeping the rest the
same way, over `Speed` = 0, 40, 80, 120, 160, 200 into an `INT32` variable:

    Speed % 100            0   40   80   20   60    0
    max(Speed, 100)      100  100  100  120  160  200
    min(Speed, 100)        0   40   80  100  100  100
    clamp(Speed, 50, 150) 50   50   80  120  150  150
    fabs(0 - Speed)        0   40   80  120  160  200
    !Speed                 1    0    0    0    0    0
    Speed / 2 + 1          1   21   41   61   81  101
    sind(Speed) * 100      0   64   98   86   34  -34
    cos(Speed) * 100     100  -66  -11   81  -97   48

`clamp` takes the value first and then its bounds, `!` is 1 only where its operand
is zero, division binds tighter than addition -- and **`sind` is degrees while
`cos` is radians**, which is Havok's asymmetry and not a misreading: `sind(200)`
is sine of 200 degrees and `cos(120)` is cosine of 120 radians.

It also prints the
compiled token count and the RPN, which is how `cond` is shown to compile to
nothing: `sel = cond((Speed < 100), 0, 1)` and `iState = iState_Base + sel`
together compile to three tokens, and the RPN holds only `iState_Base`, `sel`,
`OP_ADD`.

**It took two fixes to run at all**, and both are worth knowing.

The `NullReferenceException` inside `Methods.setCharacter` was never about the
character. `Methods.generate` calls the non-public
`Methods.generateUpToSceneModifiers`, which takes one argument the public overload
does not — a `List<hkbGeneratorOutput>` — and passes nothing for it. Calling the
inner method by reflection with the list supplied clears it. That the context and
character were fine was settled by calling `setCharacter(ctx, ch)` by hand from
the line before the one that failed: it succeeds.

The native crash after that was **calling it before `activate()`**. The fault is a
read of `0x8` inside `getAllVariableValues`, which reads what activation builds,
and the probe was running before the sweep loop where `Methods.activate` is
called. Stepping through the inner method from inside the loop is all it needed.

`addCharacter` is a red herring for this, though it is real and callable:
`hbtHavokEnvironment.addCharacter` takes the native `hkbCharacter*`, and
`Havok.hkbCharacter` converts through an `op_Explicit` the `api` dump hides as a
special name — `(IntPtr)ch` with `Pointer.Box`. It faults in
`hkPointerMap<hkbCharacter*, hkpRigidBody*>::insert`, so it is the physics floor
registry and behaviour evaluation does not need it.

`poke` prints the environment's state, the character wrapper's hierarchy and its
conversions, if any of this needs checking again.
