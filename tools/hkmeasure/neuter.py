#!/usr/bin/env python3
"""Replace a managed method body with `ret`, in a private copy of the assembly.

Mono's IL importer rejects a handful of the C++/CLI methods in HavokAssembly
(`IL_xxxx: and` on mixed types).  The ones in the way here -- the physics floor
under a character -- are features this harness does not use, so the cheapest fix
is to stub them rather than to swap the runtime under the whole prefix.
"""
import struct, sys

def rva_to_off(d, rva):
    pe = struct.unpack_from('<I', d, 0x3c)[0]
    nsec = struct.unpack_from('<H', d, pe + 6)[0]
    optsz = struct.unpack_from('<H', d, pe + 20)[0]
    sec = pe + 24 + optsz
    for i in range(nsec):
        o = sec + 40 * i
        vs, va, rs, ra = struct.unpack_from('<IIII', d, o + 8)
        if va <= rva < va + max(vs, rs):
            return ra + (rva - va)
    raise SystemExit(f"rva 0x{rva:x} not in any section")

def neuter(d, rva):
    off = rva_to_off(d, rva)
    b = d[off]
    if (b & 3) == 2:                       # tiny header: size in the top 6 bits
        size = b >> 2
        d[off] = (1 << 2) | 2              # one byte of code
        d[off + 1] = 0x2A                  # ret
        return f"tiny, was {size} bytes"
    if (b & 3) == 3:                       # fat header: 12 bytes, code size at +4
        size = struct.unpack_from('<I', d, off + 4)[0]
        struct.pack_into('<I', d, off + 4, 1)
        d[off + 12] = 0x2A
        return f"fat, was {size} bytes"
    raise SystemExit(f"unknown method header 0x{b:02x}")

src, dst = sys.argv[1], sys.argv[2]
d = bytearray(open(src, 'rb').read())
for a in sys.argv[3:]:
    print(f"  rva {a}: {neuter(d, int(a, 16))}")
open(dst, 'wb').write(d)
print("wrote", dst)
