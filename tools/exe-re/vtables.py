"""Dump every RTTI virtual table of an executable as tab-separated text.

    python3 vtables.py <exe> > vtables.tsv
    # vftable  subobject-offset  class  slot-count  slot addresses...

Takes a minute or two. Search the result instead of the binary: which classes put a
given function in a slot (grep the address), what a class's slot N is (the N+5th field),
and which classes share an implementation.
"""
import sys
from pe import PE

for vt, suboff, name, slots in PE(sys.argv[1]).all_vftables():
    print(hex(vt), hex(suboff), name, len(slots), ' '.join(hex(s) for s in slots), sep='\t')
