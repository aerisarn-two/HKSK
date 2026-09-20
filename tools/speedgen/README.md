# speedgen

Writes `speeddatasinglefile.txt` from the game's other assets:

    speedgen <meshes> <data> [-o <output>] [--tolerance <units>] [--force]

- `<meshes>` — the extracted `meshes` folder: `animationdatasinglefile.txt`, the split
  cache under `animationdata/`, and the actors' behaviour and character files. The
  BSAs are not read directly; extract them first.
- `<data>` — the game's `Data` folder, for `Skyrim.esm`, `Update.esm` and the three DLC
  masters. The movement types and what the races do with them come from there.
- `-o` — the file or folder to write. Default `./speeddatasinglefile.txt`.
- `--tolerance` — how far a dropped point may sit from the line the game draws
  between its neighbours, in units per second. The default is 0.5; the game's own
  files were thinned at 2, which is coarse against the half-unit grid.
- `--force` — allow writing over the shipped table inside `<meshes>`, which is
  otherwise refused.

A shipped `speeddatasinglefile.txt` in `<meshes>` is never read: the cache drops it
before anything is generated, and the output is byte-identical whether or not one is
there. The algorithm is `HKSK.Speed.SpeedDataGenerator`; this tool only opens the
masters, which the library deliberately does not.

## What comes out

From the 41 actor projects that read the table, about seven seconds:

    projects   41
    blocks     125  (2375 records, 62040 points, tolerance 0.5)

The table is written for the engine that reads it, not to reproduce the shipped one
(`docs/speed-data.md` §4.5, §8). A project is in it when its graph carries a
`BSSpeedSamplerModifier`, the one reader the game has; the eight flyers and hoverers
without one are left out, and the game answers a request for them as it answers any
absent project, unchanged. A block is written for every value the graph can put
`iState` at -- its initial value, its tagging generators, its state manager's rows
and its expressions -- because that value is what the engine reads back to choose the
movement type and what the sampler keys the table on. Nine such keys get no block on
purpose: a tag places them in a subtree nothing under which reads the sampler, the
player's mounted states, so a block there would be unread.

Against the shipped file (`SpeedDataRebuildTests`):

|                                                       |                          |
| ----------------------------------------------------- | ------------------------ |
| shipped blocks written                                | 76 of 86                 |
| shipped blocks the engine never asks for              | 10 (8 projects with no sampler, 2 keys no graph writes) |
| blocks the game does not ship                         | 49                       |
| shipped points within 2%, read as the game reads them | 13,451 of 16,930 (79.5%) |
| size                                                  | 518 KB                   |

Three choices cost against the shipped file and are kept because the engine is the
measure: the curve is read at the goal speed itself, where the shipped sweeps read it
0.0404 early; every heading is swept from zero, where the shipped sweeps settle in
from 0.5; and the sweep reaches the whole ladder and twice the fastest speed the
movement type names, where the shipped ones stop at 324.5 on 74 of 86 blocks and hand
faster requests back unchanged.
