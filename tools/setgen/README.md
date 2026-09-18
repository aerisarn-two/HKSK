# setgen

Writes `animationsetdatasinglefile.txt` from the game's other assets:

    setgen <meshes> <data> [-o <output>] [--slack <factor>] [--flags-from <file>] [--force]

- `<meshes>` — the extracted `meshes` folder: `animationdatasinglefile.txt` and the
  actors' behaviour and character files.
- `<data>` — the game's `Data` folder, for `Skyrim.esm`, `Update.esm` and the three DLC
  masters. The idle events, the equip events and the races' attack events come from there.
- `-o` — the file or folder to write. Default `./animationsetdatasinglefile.txt`.
- `--slack` — how much larger than one weapon's files a set may grow to cover several.
  The default, 1.5, keeps the player's sets to a few thousand; 1 splits a set wherever two
  weapons load different files.
- `--flags-from` — a shipped `animationsetdatasinglefile.txt` to take the moving-attack
  flag from. Most of that flag cannot be derived (`docs/animation-set-data.md` §4.6, §6):
  without this, the 30 attacks whose travel is the actor's -- interpolated by a
  speed-parametric blend, or made while the graph raises `IsSprinting` -- are still flagged,
  and the hovering creatures and a few others, 12 of vanilla's 38, are not.
- `--force` — allow writing over the shipped file inside `<meshes>`, which is otherwise
  refused.

Apart from `--flags-from`, a shipped `animationsetdatasinglefile.txt` is never read: the
cache drops the one in `<meshes>` before anything is generated. The algorithm is `HKSK.SetData.SetDataGenerator`; this tool
only opens the masters, which the library deliberately does not.

## What it builds

Not a copy of the shipped file. The shipped file records how it was edited
(`docs/animation-set-data.md` §3.3); the rebuild builds what the executable reads it for
(§4): a base set an actor's graph loads when it is built, a set for every idle event the
graph handles holding every file that event can lead to before the graph is home again or at
another idle's door, weapon sets keyed on the equip
events with the attacks the race reads for each weapon, and one set with everything for a
creature that never chooses by weapon. The method is §5.

## What comes out

From the 49 actor projects, about 25 seconds:

    projects   49
    sets       2241  (57495 animations listed, 3819 attacks)

Against the shipped file (`SetDataRebuildTests`):

| | |
| --- | --- |
| files the shipped data lists that are in some set of the project | 6,758 of 6,829 |
| of the rest: first-person killmoves moved to the victim's sets, and the werewolf's human-side killmoves | 66 + 5 |
| files of a shipped idle set that its own keys load | 2,145 of 2,689 (79.8%) |
| attack entries identical, over the 121 weapon combinations a race asks about | 22,949 of 27,039 (84.9%) |
| files of a shipped weapon set in some set that applies under that weapon | 15,248 of 15,538 (98.1%) |
| one-set creatures identical to the shipped set | 26 of 38 |
| size | 2.4 MB (shipped 0.8 MB) |

Untested in play. By `docs/animation-set-data.md` §4.4 a file the data misses still loads
when its clip first plays, late, so the rules lean towards listing.
