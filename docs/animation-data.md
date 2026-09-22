# animationdatasinglefile.txt — the animation data

What the file is for, what the engine does with it, and how each part of it is
produced from the Havok files. It is the first of the three text caches beside the
meshes, and the one the other two depend on: the set data names its clips
(`docs/animation-set-data.md`) and the speed table is computed from its root
motion (`docs/speed-data.md`). Section numbers are stable.

## 0. Purpose

A behaviour graph's clip generator names an animation by path, and the animation
is a Havok packfile that has to be opened to learn anything about it. Two things
the game needs at once, before any file is open, are in this cache:

- **the binding of every clip to a slot** in the character's animation list, with
  the playback speed and crop the generator applies, and every event the clip
  announces at a time -- so the engine can bind a clip generator to a file by
  name, and combat, magic and the set data can know when `HitFrame` or
  `arrowRelease` falls without decoding the animation;
- **the root motion of every animation**: where the root has travelled to and how
  it has turned by the end. Skyrim's animations carry no extracted motion of their
  own (`hkaAnimation.m_extractedMotion` is null on every shipped clip); this block
  is the only place the travel exists. Combat measures an attack's reach from it
  (§4.3), and the speed table was sampled from it.

## 1. Layout

Text, one value per line, a counted-block grammar: nothing is self-describing, and
the line counts are what keep a reader in step. `HKSK.Cache.AnimationDataFile`
reads and writes it byte-exact, recomputing every count from the content.

    <project count>
    <Project>.txt                      × count
    per project, in that order:
      <line count of the project block>
      <has file list: 0|1>
      [ <file count>, then the Havok files, Windows-separated, relative to the project .hkx ]
      <has animation cache: 0|1>
      [ clips until the block's lines run out:
          <clip generator name>
          <cache index>                the slot in the character's animation list
          <playback speed>
          <crop start seconds>
          <crop end seconds>
          <event count>, then name:time per event ]
      only when the cache flag is 1:
      <line count of the movement block>
      movements until the lines run out:
          <cache index>
          <duration>                   the time of the last key
          <translation key count>, then time x y z per key
          <rotation key count>, then time x y z w per key

- **The file list** restates what the project and character `.hkx` already say:
  the behaviour graphs, the character, the skeleton. Some projects ship without
  one, and whether it is present is itself recorded.
- **A clip entry is one clip generator.** The name is the generator's `m_name`
  (what the set data's attacks list); the cache index is the position of the
  clip's animation in the character's `animationNames`; playback speed and the two
  crops are copied from the generator verbatim -- all 10,556 shipped clips with a
  generator agree with it exactly.
- **Events** merge two sources: the animation's own annotation track and the
  behaviour's clip triggers, as `name:time` in clip seconds. No name carries a
  colon; 3,941 carry a dot, the behaviour's payload separator.
- **A movement is one animation slot**, not one clip: several clips share a slot
  and its motion. Translation is the displacement from the start, implicitly zero
  at time zero; 5,769 of the 6,725 shipped blocks hold a single key at the end
  with the whole displacement. Rotation is on the same terms, identity at zero,
  one key in 6,428 blocks. The duration is the last key's time, and equals the
  animation's own duration in 98.5% of blocks -- the keys define it, not the file.

## 2. The split form

`animationdata/<Project>.txt` holds one project block and
`animationdata/boundanims/anims_<name>.txt` its movement block; `HKSK.Cache.SplitCache`
reads them. **The shipped split form is a pre-DLC snapshot** and disagrees with the
merged file on every Dragonborn project; index from the merged file.

## 3. Producing it

Most fields restate a Havok file; two do not, and one part cannot be recovered from
the files at all. `HKSK.AnimationData.AnimationDataGenerator` brings an entry up to
date with its files and carries the rest:

- the **file list**: the root graph the character names, every graph it reaches
  across the `hkbBehaviorReferenceGenerator` joins, the project's character files and
  the rig -- and, for a prop, its animations after the rig, which all 247 shipped
  props with animations list and no actor does. The **order** is the authoring tool's
  and is not derivable: the humans' behaviours are listed neither depth nor breadth
  first. A file is listed when an entry resolves to it, however spelled -- the bow's
  character names `..\Bow\Behaviors\BowBehavior.hkx`, the list `Behaviors\BowBehavior.hkx`;
- one **clip** per `hkbClipGenerator` reachable from the root graph, once per name,
  matched exactly (`Crossbow_IdleHeld` and `CrossBow_IdleHeld` are two of the
  humans'): `m_name`, `m_playbackSpeed`, `m_cropStartAmountLocalTime`,
  `m_cropEndAmountLocalTime` -- all 10,556 shipped clips with a generator agree -- and
  the **index** of `m_animationName` in the character's list, by path or, failing that,
  by file name, since the female character lists `Animations\female\...` where her
  graph's clips say `Animations\male\...`;
- the **events**, derived from the animation's annotations and the clip's triggers:
  annotation times clamped to the playing length `(duration - crops) / speed`, a
  trigger relative to the end at that length plus its local time, an annotation's text
  as the longest dotted prefix naming an event of the clip's own graph -- dropped when
  none does -- an annotation restating a trigger counted once, and a trigger first on a
  tie. That reproduces 9,973 of the 10,556 lists exactly; most of the rest is drift, the
  dog's graph triggering `NPCFoxBreatheRun` that none of its lists carries;
- the **root motion is not in any Havok file.** A Skyrim animation's root bone does not
  move (§0), so the travel exists only here, set by importing an animation
  (`HKSK.Fbx.AnimationExchange`) or `ActorProject.SetRootMotion`.

So an entry is amended, not rebuilt: what the files state is made true, a new clip is
added at the end with derived events, and a cached event list, a cached number and the
root motion are kept. Amending all 429 shipped entries changes none of them, which is
the property a caller relies on to amend everything after adding one creature. Three
things the shipped file does that a generator must survive:

- the horse's and the werewolf's clips are numbered against a different character list
  -- 89 slots against 51 names, 104 against 99 -- and are left as they are rather than
  renumbered under root motion that cannot be checked;
- `SmallBird01` is listed twice, so the whole file is amended entry by entry;
- five packfiles are called `moth.hkx`, and the one a name finds first is an effect's:
  the project is the one whose folder resolves the files its entry already lists.

Whether a new project is an actor or a prop is not in the Havok files either: 247 props
have animations. An actor is a project a race wears, which the plugins say
(`HKSK.Records.GameRecordRules.ActorProjects`).

## 4. The engine side

Read out of `SkyrimSE.exe`, as `docs/animation-set-data.md` §4 and
`docs/speed-data.md` §4; addresses are image-base virtual addresses.

### 4.1 Where the file is read

Three of the six path globals the animation text data owns are this file's:
`0x14315c900` `Meshes/AnimationData/`, `0x14315c910` `BoundAnims/` and
`0x14315c918` `Meshes/AnimationDataSingleFile.txt` (initialisers
`0x14008fff0`–`0x1400900e0`). The animation data manager is created at start-up by
`0x140536ec0` and published at `0x143138d50`, in the same routine (`0x140541b80`)
that then creates the set data manager and the speed database. The merged file is
the one read; the split form is the fallback, which is how its snapshot could ship
stale.

### 4.2 Binding a clip to a file

`hkbClipGenerator::activate` (`0x140acd750`) with no file bound (`userData` at
`+0x30` is 0) calls the clip loader's slot 1 (`0x140bcbb90`), which maps the clip's
`animationBindingIndex` (`+0x70`) to a file id through the character's animation
list and demands the file from `AnimationFileManagerSingleton` through the copy of
its pointer the behaviour runtime holds at `0x1431b2820`. The cache index in this
file is that binding: the clip plays what the **character** lists at the index,
which is why the female body plays female files from a graph whose clips say
`Animations\male\...`.

### 4.3 What reads the root motion and the events

`CombatBehaviorContextMelee`'s attack update (`0x1408a3d30`,
`docs/animation-set-data.md` §4.6) asks the animation data through `0x140442a80`
for three things per attack clip: the translation at the end, the translation at
the `HitFrame` event, and the hit frame's time -- the movement block and the event
list of this file, joined on the clip's slot. With the moving-attack flag clear
the end translation's length is the attack's reach and the hit-frame translation
places the blow; the sampler that wrote the speed table read the same travel and
duration.

**The movement itself reads the same blocks, every frame.** The animation data
manager's singleton (`0x143138d50`) has fourteen readers; beside the loader and
the combat query above, three of them ask the actor's graph which clips are active
(`0x140ba4d40`, `0x140ba4e30`) and then the cache for their movement:

- `0x140540990` is the per-frame root-motion delta. For each active clip it takes
  the clip's local time and weight, samples the clip's movement block at the time
  and at the time less the frame's advance (`0x140538240`, which returns zero for a
  block with no keys and otherwise interpolates the keys linearly by time in
  `0x140538390`), and accumulates the difference scaled by the weight. Its one
  caller is slot 3 of **`MovementTweenerAgentAnimationDriven`** (`0x140797ed0`,
  vftable `0x1418b43b8`), which rotates the delta into the world by the actor's
  heading and hands it to the controller. That agent is what `AnimationDriven`
  installs (`docs/animation-events.md` §2), so **a clip in animation-driven mode
  moves the actor by this file's movement block and by nothing else** -- the Havok
  animation's `extractedMotion` is null on every shipped clip and is never
  consulted;
- `0x14053fd20`, `0x140540680` and their wrappers (`0x14053fa70`, `0x14053fbe0`)
  are end-of-clip queries -- the travel a clip will have made -- used by callers
  this pass did not name (`0x140783020`, `0x1407c51c0`, `0x1403b2230`, the
  `0x1407f9480` cluster, and the actor update `0x1406972d0`).

So a clip's root motion in this cache is what the game believes the animation does
*and* what moves the actor when the graph says the animation owns the movement; an
animation edited without regenerating the cache moves the character one way and is
measured another. In motion-driven mode -- the controller's velocity from the
movement type or, for the dragon in the air, from the flight follower
(`docs/flight.md` §2) -- the block is not applied, and the shipped cruise clips
accordingly carry none.

## 5. Authoring

- **Regenerate the cache whenever a clip generator or an animation changes**: a
  playback speed, a crop, a trigger, a root track. The cache is the game's view and
  the Havok files are not consulted for these numbers.
- **Give every locomotion and attack animation a root track** with its travel and
  turn on the root bone; without one the movement block is empty, the creature's
  ladder reads zero, combat measures a reach of zero, and the speed table records a
  creature that cannot move.
- **Put `HitFrame` on every attack clip** and the `SpellFire` and `arrowRelease`
  events on the casts and shots (`docs/animation-variables.md` §4): they are read
  from this file's event list, not from the animation.
- **Name clip generators uniquely within a project where the set data will list
  them** (`docs/animation-set-data.md` §1): the set data's attacks name clips by
  this name, and a duplicate is ambiguous.
- **Keep a slot per animation, not per clip**: several clips over one animation
  share its motion, and a per-clip difference has to be a crop or a playback speed
  on the clip entry.

## 6. Tooling

- `HKSK.Cache.AnimationDataFile`, `ProjectBlock`, `ClipGeneratorEntry`,
  `ClipMovement`, `SplitCache` -- the file and its split form, byte-exact.
- `HKSK.Model.SkyrimCache`, `ActorProject`, `AnimationSlot` -- a project as one
  model: its clips joined to their slots, motions and files.
- `HKSK.AnimationData.AnimationDataGenerator` -- an entry amended from its files, or a
  new one added (§3).
- `HKSK.Fbx.AnimationExchange` -- a clip to FBX and back with its root motion and
  events (`docs/paired-animations.md` for the paired case).
