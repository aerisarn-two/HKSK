#!/usr/bin/env python3
"""Make Methods::generate pass its timestep through to hkbBehaviorGraph::generate.

The facade hard-codes 0.0f there -- the Behavior Tool advances time from its own
timeline instead -- so a headless caller poses the graph forever at t=0.
`ldc.r4 0.0` and `ldarg.s 4` are both worth one float on the stack, and the
first is five bytes to the second's two, so the rest is padded with nop.
"""
import struct, sys

src, dst, rva = sys.argv[1], sys.argv[2], int(sys.argv[3], 16)
d = bytearray(open(src, 'rb').read())

pe = struct.unpack_from('<I', d, 0x3c)[0]
nsec = struct.unpack_from('<H', d, pe + 6)[0]
optsz = struct.unpack_from('<H', d, pe + 20)[0]
off = None
for i in range(nsec):
    o = pe + 24 + optsz + 40 * i
    vs, va, rs, ra = struct.unpack_from('<IIII', d, o + 8)
    if va <= rva < va + max(vs, rs):
        off = ra + (rva - va)
assert off is not None

hdr = d[off]
body = off + (1 if (hdr & 3) == 2 else 12)
size = (hdr >> 2) if (hdr & 3) == 2 else struct.unpack_from('<I', d, off + 4)[0]
print(f"method body at 0x{body:x}, {size} bytes ({'tiny' if (hdr&3)==2 else 'fat'})")

OLD = bytes([0x22, 0x00, 0x00, 0x00, 0x00])   # ldc.r4 0.0
NEW = bytes([0x0E, 0x04, 0x00, 0x00, 0x00])   # ldarg.s 4; nop; nop; nop

code = bytes(d[body:body + size])
hits = [i for i in range(len(code) - 4) if code[i:i + 5] == OLD]
print("ldc.r4 0.0 at body offsets:", [hex(h) for h in hits])
if len(hits) != 1:
    sys.exit("expected exactly one; refusing to guess")
d[body + hits[0]:body + hits[0] + 5] = NEW
open(dst, 'wb').write(d)
print(f"patched offset 0x{hits[0]:x} -> ldarg.s 4; wrote {dst}")
