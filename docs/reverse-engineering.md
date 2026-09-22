# Reading SkyrimSE.exe

How the engine-side facts in `docs/speed-data.md` §4 and `docs/animation-set-data.md` §4
were read out of the executable, written so the next question can be answered the same
way. The tools are `objdump`, Python and `tools/exe-re`; nothing here needs a
disassembler GUI or symbols.

The method is narrow on purpose. Skyrim has no symbols, so naming an arbitrary function is
guesswork. What can be established without guessing is **structure**: which code touches
a given string, global, setting or class, and what that code does with it. Every question
below was answered by starting from something the data names — a file path, a class, an
INI setting — and following references outward until the question was decided.

## 1. A readable binary

The retail executable is wrapped by SteamStub Variant 3.1 (x64). The tell is a `.bind`
section with the entry point inside it; `.text` is encrypted at rest and disassembles to
x87 junk and bad opcodes.

    python3 -c "from pe import PE; p = PE('SkyrimSE.exe'); print(hex(p.entry_rva), [s[0] for s in p.sections])"
    # entry inside .bind, sections include .bind  ->  wrapped

`.rdata` is **not** encrypted, so strings, RTTI and virtual tables read correctly from the
wrapped file. Anything that needs code — references, control flow — needs `.text`
unwrapped.

**Unwrap a copy, for reading only.** Steamless v3.1.0.5:

    cp "<Game Root>/SkyrimSE.exe" ~/Dev/steamless/
    cd ~/Dev/steamless
    cp Plugins/Steamless.API.dll Plugins/SharpDisasm.dll .
    wine Steamless.CLI.exe --quiet 'Z:\home\<user>\Dev\steamless\SkyrimSE.exe'

The copy step is the Wine workaround. Under Wine's Mono the CLI fails with
`TypeLoadException ... Steamless.API` because a compiler-generated lambda field loads
before the CLI's resolver can point at `Plugins/`; with the two assemblies beside the
executable the ordinary probe finds them. The output lands beside the input as
`SkyrimSE.exe.unpacked.exe`.

**Verify it before trusting it.** Disassemble an address already known to be code — the
speed query in `BSSpeedSamplerModifier::Update` is a good one — and check it reads as
instructions:

    objdump -d --no-show-raw-insn --start-address=0x140b9f047 --stop-address=0x140b9f074 SkyrimSE.exe.unpacked.exe
    #   mov 0x261e112(%rip),%rcx   # 0x1431bd160   <- readable

The unwrapped binary is not redistributable and is kept out of every repository. Every
address in the documents is a virtual address in this build (image base `0x140000000`),
re-derivable from a local copy.

## 2. One disassembly, searched as text

    objdump -d --no-show-raw-insn -j .text SkyrimSE.exe.unpacked.exe > text.asm

About 7.2 million instructions, a few seconds to produce. `objdump` resolves every
RIP-relative operand to an absolute address in a trailing comment (`# 0x14315c930`), so
**"which code references this address" is a text search**, and that one search carries
most of the work:

    grep -nE "0x14315c930\b" text.asm

`tools/exe-re/index.py` wraps the three searches used throughout, over the same file:

    python3 index.py <exe> text.asm refs    <address>...   # functions referencing it
    python3 index.py <exe> text.asm summary <address>...   # what a function calls, which strings it reads
    python3 index.py <exe> text.asm callers <address>...   # direct callers, and where it is stored as a pointer

Function boundaries come from a heuristic — a function starts at the first instruction
after `int3` padding. It is right almost always and wrong occasionally (adjacent functions
with no padding, an `int3` inside a function). Before relying on a boundary, read the
prologue at the start it reports.

## 3. The recipes

### 3.1 From a file path to the code that reads it

A path in the data is the natural starting point, and it takes two hops, not one.

1. **Find the literal.** `p.string_va("Meshes/AnimationSetDataSingleFile.txt")` →
   `0x14186ed38`.
2. **Find its references.** A literal is almost never used directly: its only reference is
   a **static initialiser** that builds a global `BSFixedString` from it —

        lea  <literal>,%rdx
        lea  <global>,%rcx
        call 0x140cec5d0          ; BSFixedString construction
        lea  <exit thunk>,%rcx
        jmp  0x14153b080          ; register the destructor

   The initialisers for related paths sit next to each other, which is how the six
   animation text paths were found together.
3. **Search for the global, not the literal.** The code that uses the path references the
   global (`0x14315c930`), and a search for the literal finds only the initialiser. This is
   the step that is easy to skip and that finds nothing if skipped.

### 3.2 From a class name to its layout

MSVC x64 RTTI survives in `.rdata`. `p.rtti_vftables("BSSpeedSamplerDBManager")` finds
the class's virtual tables:

- the TypeDescriptor is the `.?AV<name>@@` string minus 16 bytes;
- a CompleteObjectLocator has signature 1, the descriptor's RVA at +12 and its own RVA at
  +20;
- the virtual table starts 8 bytes after the qword that points at the locator.

Two things follow cheaply. **The slot count bounds what the class can do**: the speed
database has two virtuals, a destructor and the query, so it has no virtual that records a
sample. **Comparing a class's slot count with a sibling's** says whether it adds virtuals
of its own: `BSSpeedSamplerModifier`, `BSIsActiveModifier` and `BSModifyOnceModifier` all
have 25. Searching for the virtual table's address in `text.asm` finds the constructors and
destructors, which store it into the object.

An unknown object's class can be named the same way backwards. The qword before a virtual
table points at its locator, and the locator's descriptor names the class — that is how the
save-game reader `AnimationStreamLoadGame` was identified from a bare virtual table address.

### 3.3 From an INI setting to its default and its readers

A Bethesda setting is a 32-byte record:

    +0   vtable          0x141775178 for every bool setting
    +8   value           the compiled default
    +16  const char*     "name:Section"
    +24  padding         0xEFBEADDE

`p.setting("bInitiallyLoadAllClips:Animation")` finds the record by looking for pointers to
the name. The value's address is the record plus 8, and **searching for that address finds
every reader** — often a single small function that caches it:

    cmpb $0x0, 0x142015628     ; bInitiallyLoadAllClips
    sete 0x1431a939c           ; cached as its negation

and from there, searching for the cached flag's address, or for callers of the small
function, finds the code whose behaviour the setting decides. Whether a shipped INI
overrides the default is a separate check against the game's INI files.

### 3.4 From a singleton to its consumers

Managers are published as singletons: a constructor writes `this` into a global
(`mov %rcx, 0x143138d58`). Searching for that global and classifying each reference is how
"what uses this" and "is this necessary" were answered for both tables:

- **writes** of the global are life cycle: creation, usually behind a null check in a
  start-up routine, and clearing at shutdown;
- **reads followed by a null test and an early return** are consumers that do nothing
  when the manager is absent — the step that decides necessity;
- **reads followed by a call** name the manager's methods; `summary` on each consumer
  shows which methods it calls and which other singletons it pairs them with.

Then work from the methods back: `callers` on a manager method lists who else can reach
it. When every path to a behaviour has been classified — construction, destruction,
loading, the query, a save-game restore — the claim "only X does Y" is proven rather than
suspected. When one path has not, the document says so.

### 3.5 From a parser to a structure's layout

A consumer shows which offset it reads, not what lives there. The loader shows both: it
reads the file in order and writes each part somewhere. Read the parser alongside the
file format — each `readline` followed by `atoi` is a count, each `readline` followed by a
string-handle constructor is a name, each array reservation names the container the next
lines go into — and the offsets fall out in file order. Only then read the consumers
against the layout. Doing it the other way round had the animation set's attacks and
checksums swapped until the parser was checked.

### 3.6 Two callers that differ in one place

When two functions call the same helpers and look alike, `diff` their disassembly with
addresses masked out:

    diff <(objdump ... --start-address=A --stop-address=B | sed 's/0x[0-9a-f]\{6,\}/ADDR/g') \
         <(objdump ... --start-address=C --stop-address=D | sed 's/0x[0-9a-f]\{6,\}/ADDR/g')

The animation set consumers that call the file manager's two request entries are identical
except for one extra stack argument, which is what said the second entry is a variant of
the first and not its inverse.

### 3.7 From a virtual call to the classes behind it

A call through `this+0x38` to slot 6 names no function. `vtables.py` dumps every RTTI
virtual table once, with each table's subobject offset — the locator's +4, which says which
base the table belongs to — so the question becomes a search: the tables whose offset is
0x38, and their seventh slot.

    python3 vtables.py SkyrimSE.exe.unpacked.exe > vtables.tsv     # 8,558 tables

That is how the choice between loading an object's clips up front and not was found to be
a constant of the class: `IAnimationGraphManagerHolder` slot 6 is `mov $1,%al; ret` for
`TESObjectREFR` and every projectile and explosion, and `xor %al,%al; ret` for `Actor`,
`Character` and `PlayerCharacter`. The same file names a function stored in a table but
never called directly — `callers` reports it as "stored at", and the table's row says whose
slot it is (`QueuedActor` slot 25, for one of the checksum readers).

### 3.8 From an index to a game object

Code that takes a small integer and indexes a manager is usually reaching a table the data
also names. Actions are default objects: the manager at `0x1420f5600` keeps objects from
+0x20 and a loaded flag per index from +0xb90, and the names — `Action Draw`, `Action Force
Equip` — are 24-byte records at `0x141fd8f50`, found by searching for a pointer to one name
and checking that its neighbours at a fixed stride are names too:

    p.cstr(p.qword(0x141fd8f50 + index * 0x18))

The last named index is 0x16d. For a constant past it (0x16e), say that it is past the
table rather than guess what it resolves to.

### 3.9 From a structure's shape to its reader

When a field's reader cannot be reached from a name -- the attack flag's container was filed
in a map and the map's readers were inlined -- search for the *shape* of the access. The
parser gave the layout: entries of 0x18 bytes in an array, a clip list at +0x08 whose
header's sign bit marks inline storage, the flag as a byte at +0x10. A reader has to index
by three (`lea (%rX,%rY,2)`), test a byte at +0x10, and test a header's sign
(`cmpl $0x0,(%rX)`). Split `text.asm` into functions and keep the ones with all three:

    triple = lea +\(%r\w+,%r\w+,2\),%r\w+
    flag   = cmpb +\$0x0,0x10\(%r\w+\)  |  movzbl +0x10\(%r\w+\),
    inline = cmpl +\$0x0,\(%r\w+\)

Of every function in the executable, one matched, and it was the reader. Each condition
alone matches thousands.

A related search found where a clip generator's mirror bit is read: `hkbClipGenerator`'s
flags byte is at +0x73, so `testb $0x4,0x73(%r` finds the two graph visitors that collect
clips for the animation data manager.

### 3.10 From an animation variable's name to the code that uses it

A graph variable's name is not a literal with a global of its own. The engine keeps one
lazily-built table of 189 `BSFixedString`s -- bone names, event names and variable names
together -- and reaches each by a fixed offset into it:

1. find the name's bytes in the file and turn the offset into an address (`pe.off2va`);
2. the address has **no data pointers**: it is loaded with `lea`, once, by the function
   that fills the table (`0x14021f300`). Grepping `text.asm` for the address gives that one
   site, and the neighbouring stores give the whole table in order -- each entry is
   `lea 0x<slot>(%rbx),%rcx; lea <string>(%rip),%rdx; call <BSFixedString assign>`;
3. the slot is the name's identity from then on. `bAnimationDriven` is `+0x568`,
   `bAllowRotation` `+0x570`, `Direction` `+0x518`, `IsSprinting` `+0x350`;
4. the table is a thread-safe static: 28 accessors each guard it (`_Init_thread_header` at
   `0x14153b45c`) and return the base, `0x1420f6370`. So a user of one name looks like
   `call <accessor>` followed within a few instructions by `lea 0x<slot>(%rax),%rdx`;
5. grep for exactly that pairing. Searching the slot's absolute address finds nothing, and
   searching `0x568(%r` alone finds 74 unrelated structure fields.

Whether a hit reads or writes the variable is settled by the argument, not the vtable slot:
a getter is handed a **pointer to a local** that the caller reads afterwards.

## 4. Worked examples

**The speed table** (`docs/speed-data.md` §4). Strings named `bUseSpeedSampler`, the
database manager and the modifier. RTTI gave the manager two virtuals. The path literals'
initialisers gave three globals; their references were the gated `.SPD` loader and the
merged-file loader inside the constructor. The query singleton had exactly one reader,
the modifier's `Update`. Classifying every reference to the file names, the database,
the singleton and both virtual tables showed **no code writes a speed table** — the
sampler that recorded the shipped one was a tool outside the game.

**The animation set data** (`docs/animation-set-data.md` §4). The path literals'
initialisers gave six globals; the set data's were used by one loader, which publishes a
singleton and consults `bLoadCollatedAnimTextData`. The singleton's 15 referencing
functions split into life cycle and ten consumers. Reading the smallest consumer showed
it returns early without `AnimationFileManagerSingleton`, and otherwise turns a set data
lookup into a load request. `bInitiallyLoadAllClips`, found through its setting record,
decides whether that file manager exists at all. The file manager's two request entries
have no callers besides the set data consumers and a save-game restore. That was first
written up as the set data being **necessary** — and it was wrong, because the behaviour
runtime reaches the same file manager through a copy of its pointer (§5): the clip
generators demand their own files when they activate. The set data decides what is loaded
ahead of need. Reading the per-set parser in file order
gave the set object's layout, and reading the set selector against that layout showed the
lookup is keyed on the swap event, with hand variables checked as ranges against the
graph's live variables.

**Who sends the key** (`docs/animation-set-data.md` §4.3). Every set-selecting consumer was
followed one level up. Two took an actor and a small integer rather than a string; the
integer indexed the default object table (§3.8) — `Action Draw`, `Action Force Equip` — and
the key came from a helper that fills a `TESActionData` (named by its vftable's RTTI) and
asks the idle manager which idle the action would pick. The string that helper returns was
matched to the idle record's `ENAM` offset from the record loader's switch over subrecord
types.

**Who sets `bAnimationDriven`** (§3.10). Nothing in the executable does. The name resolves
to slot `+0x568` of the string table, and exactly one call site takes it: `0x140669760`,
which asks the actor's `IAnimationGraphManagerHolder` (at `this+0x38`, vtable slot `+0x90`)
for `bAnimationDriven` and then for `bAllowRotation`, both into locals it reads afterwards --
a getter, twice. It compares the pair with the previous frame's and, only on a change, sends
one of three events through `0x14069f450`:

    bAnimationDriven   bAllowRotation    event
    0                  0                 StartMotionDriven       0x1405418e0
    1                  -                 StartAnimationDriven    0x1405419c0
    0                  1                 StartAllowRotation      0x140541aa0

So the direction is the opposite of the obvious one. The **graph** decides whether it is
animation-driven -- an animator ticks a `BSIsActiveModifier` whose `bIsActive0` is bound to
the variable on the branch that should carry its own root motion, and the variable is 0 in
every project until something raises it -- and the **engine polls it** and moves the actor's
motion mode to match. It is a report from the animation to the game, not a command from the
game to the animation, which is why it explains nothing about values the game stores beside
a clip (`docs/animation-set-data.md` §6).

**Whether an event name is matched by case** (`docs/animation-events.md` §1). The
names the graphs are sent (`moveStart`, `staggerStart`, `IdleStop`) are not in the
executable at all -- they are the idle records' events -- so there was no literal to
start from. The way in was a virtual slot: `IAnimationGraphManagerHolder`'s vftable
(§3.2) has `NotifyAnimationGraph` at slot 1, and three hops of reading bodies reached
the per-graph lookup, which hashes and compares the string's *pointer*. That made the
question one about the string pool: the `BSFixedString` constructor's hash folds case
and its lookup calls an import, named by `objdump -p` on the IAT slot (`_stricmp`);
the pool's only other lookup is the wide one (`_wcsicmp`), and `strcmp`'s callers,
found through its jump thunk rather than the import slot, are all elsewhere. Two
things to keep from it: an import is reached by a thunk, so searching the IAT slot's
address finds the thunk and not the callers; and a compare by pointer is a compare by
whatever rule interned the pointer.

**The attack flag** (`docs/animation-set-data.md` §4.6). The parser gave the entry layout
and that the flag is stored as `atoi > 0`. The obvious leads -- the race's map, the combat
settings for moving attacks -- led to combat code that never read the byte. The shape search
(§3.9) found the reader, `CombatBehaviorContextMelee`'s attack update; following the value it
stores into the attack check that reads the moving-attack settings named the flag.

## 5. Traps

- **Reading `.text` from the wrapped binary.** It does not fail; it disassembles to
  plausible-looking junk. Check for `.bind` first.
- **Searching for the literal instead of the global.** The literal's only reference is its
  initialiser (§3.1).
- **Trusting a heuristic function boundary.** Read the prologue.
- **A probe's output file surviving a failed run.** A script that writes a report and then
  throws leaves the previous run's report in place, and it reads as a result. Delete the
  output before each run, and check the run's exit status before reading anything.
- **Counting with a pattern that matches more than the rows meant.** A tally that greps
  for a word also present in headers or diagnostic lines counts those too. Filter on a
  field only the intended rows have, and re-derive any number that goes into a document.
- **Naming a function from what it seems to do.** Without symbols, a name is a guess. The
  documents describe what a function touches and calls, and leave its name out unless RTTI
  or a string supplies one.
- **Taking a look-alike for an inverse.** Two entry points that share most of their callees
  were first written up as "load" and "release". Sharing callees says they are related, not
  that one undoes the other; the one-argument diff (§3.6) is what settled it.
- **Counting consumers from a range of addresses.** Functions near each other are not one
  kind: one of the "eight consumers in the cluster" read checksums through a different
  method. Classify each function by what it calls, not by where it sits.
- **A singleton read through a copy.** Searching for a singleton's address finds the code
  that reads that global, not the code that reads the same pointer from somewhere else.
  Start-up copied `AnimationFileManagerSingleton` into a second global that the whole
  behaviour runtime uses, and a necessity claim was written without it. After finding where
  a singleton is written, search for every other place the written register goes.
- **Assuming a family of functions shares a signature.** Seven consumers "took the key as
  the first argument" until two of them turned out to take an actor and an action. Read the
  argument setup at each call site before generalising.
- **A field named before it was read.** HKSK called the attack flag "mirrored" from its first
  reader. The data never agreed -- 122 flagged attacks have no mirrored clip -- and the
  executable uses it for something else entirely: whether combat measures the attack's reach
  by the clip's root motion or by the attacker's own movement. Check a name against its
  reader, or say it is a guess.
- **Walking up a graph as if it were a tree.** A behaviour node has several parents --
  11.1% of them do, and 6.8% of clip generators -- so "the path from this clip to the root"
  is a choice, not a fact. `ProjectWalk` records the first parent its walk reached, and a
  rule tested down that one path scored 17 flagged against 42 clear; the same rule over
  every parent scored 29 against 76, which is a different answer. Where a property is
  claimed of a node's ancestry, say whether it holds up **some** path or **every** path, and
  compute it as a fixpoint: monotone booleans settle through the cycles a behaviour has,
  where a recursive descent either loops or has to cut itself off.
- **"Only" without the other paths classified.** A behaviour is only reached from X when
  every caller and every stored pointer to the entry point has been accounted for, save-game
  and destructor paths included.
