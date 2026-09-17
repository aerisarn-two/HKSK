# exe-re

The small toolkit used to read `SkyrimSE.exe` for `docs/speed-data.md` §4 and
`docs/animation-set-data.md` §4. The method is `docs/reverse-engineering.md`.

- `pe.py` — PE sections and address translation, C strings, pointer search, MSVC x64
  RTTI virtual tables, and Bethesda INI setting records.
- `index.py` — over an `objdump` disassembly: which functions reference an address,
  what a function calls and which strings it touches, and who calls it.
- `vtables.py` — every RTTI virtual table as tab-separated text: vftable, subobject
  offset, class, slots. Search it for which classes put a function in a slot.

No binary is in this repository. The retail executable is SteamStub-wrapped and has to be
unwrapped locally for reading; see the method document.
