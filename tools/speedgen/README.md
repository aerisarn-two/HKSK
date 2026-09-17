# speedgen

Writes `speeddatasinglefile.txt` from the game's other assets:

    speedgen <meshes> <data> [-o <output>] [--tolerance <units>] [--force]

- `<meshes>` — the extracted `meshes` folder: `animationdatasinglefile.txt`, the split
  cache under `animationdata/`, and the actors' behaviour and character files. The
  BSAs are not read directly; extract them first.
- `<data>` — the game's `Data` folder, for `Skyrim.esm`, `Update.esm` and the three DLC
  masters. The movement types and what the races do with them come from there.
- `-o` — the file or folder to write. Default `./speeddatasinglefile.txt`.
- `--tolerance` — how far a dropped point may sit from the line that replaces it.
  The default, 2, is the game's own rule; smaller keeps more of each curve in a
  larger file.
- `--force` — allow writing over the shipped table inside `<meshes>`, which is
  otherwise refused.

A shipped `speeddatasinglefile.txt` in `<meshes>` is never read: the cache drops it
before anything is generated, and the output is byte-identical whether or not one is
there. The algorithm is `HKSK.Speed.SpeedDataGenerator`; this tool only opens the
masters, which the library deliberately does not.

## What comes out

From the 49 actor projects, about seven seconds:

    projects   49
    blocks     149  (2831 records, 43048 points, tolerance 2)

Against the shipped file (`docs/speed-data.md` §9 and `SpeedDataRebuildTests`):

|                                          | tolerance 2      | tolerance 0.1    |
| ---------------------------------------- | ---------------- | ---------------- |
| shipped blocks written                   | 86 of 86         | 86 of 86         |
| blocks the game does not ship            | 63               | 63               |
| shipped points within 2%, read as the game reads them | 15,034 of 18,302 (82.1%) | 15,656 (85.5%) |
| size                                     | 371 KB           | 1.4 MB           |

The 63 extra blocks are movement types a graph declares and the game never swept.
Which ones were swept is not decidable from these inputs — `DraugrProject` and
`DraugrSkeletonProject` share every one and ship six blocks against one — so the tool
writes every block it can build. In the game an extra block gives a state a sampled
speed where the shipped file leaves the request as it is.

Two numbers in the algorithm were measured against the shipped file rather than
derived: the sampler reading its curve 0.0404 early, and the 324.5 the game's sweeps
stop at. Both are constants in the code; neither is read at run time.
