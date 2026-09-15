"""Extract Havok hkClass reflection from a binary that builds it at runtime.

Havok 6.6 has no static hkClass tables: generated code calls the hkClass
constructor with thirteen pushed arguments and a BSS `this`.  Scanning for those
call sites recovers every class the binary knows, members and offsets included.
"""
import struct, sys, re

class Image:
    def __init__(self, path):
        d = open(path, 'rb').read(); self.d = d
        pe = struct.unpack_from('<I', d, 0x3c)[0]
        nsec = struct.unpack_from('<H', d, pe + 6)[0]
        optsz = struct.unpack_from('<H', d, pe + 20)[0]
        self.base = struct.unpack_from('<I', d, pe + 24 + 28)[0]
        sec = pe + 24 + optsz
        self.secs = []
        for i in range(nsec):
            o = sec + 40 * i
            vs, va, rs, ra = struct.unpack_from('<IIII', d, o + 8)
            self.secs.append((va, vs, ra, rs))
    def off(self, va):
        r = va - self.base
        for va_, vs, ra, rs in self.secs:
            if va_ <= r < va_ + vs and r - va_ < rs: return ra + (r - va_)
    def va(self, fo):
        for va, vs, ra, rs in self.secs:
            if ra <= fo < ra + rs: return self.base + va + fo - ra
    def u32(self, va):
        o = self.off(va)
        return struct.unpack_from('<I', self.d, o)[0] if o is not None else None
    def cstr(self, va):
        o = self.off(va)
        if o is None: return None
        e = self.d.index(b'\0', o); s = self.d[o:e]
        return s.decode('latin1') if s and all(32 <= c < 127 for c in s) and len(s) < 120 else None

TYPES = {0:'TYPE_VOID',1:'TYPE_BOOL',2:'TYPE_CHAR',3:'TYPE_INT8',4:'TYPE_UINT8',5:'TYPE_INT16',
 6:'TYPE_UINT16',7:'TYPE_INT32',8:'TYPE_UINT32',9:'TYPE_INT64',10:'TYPE_UINT64',11:'TYPE_REAL',
 12:'TYPE_VECTOR4',13:'TYPE_QUATERNION',14:'TYPE_MATRIX3',15:'TYPE_ROTATION',16:'TYPE_QSTRANSFORM',
 17:'TYPE_MATRIX4',18:'TYPE_TRANSFORM',19:'TYPE_ZERO',20:'TYPE_POINTER',21:'TYPE_FUNCTIONPOINTER',
 22:'TYPE_ARRAY',23:'TYPE_INPLACEARRAY',24:'TYPE_ENUM',25:'TYPE_STRUCT',26:'TYPE_SIMPLEARRAY',
 27:'TYPE_HOMOGENEOUSARRAY',28:'TYPE_VARIANT',29:'TYPE_CSTRING',30:'TYPE_ULONG',31:'TYPE_FLAGS',
 32:'TYPE_HALF',33:'TYPE_STRINGPTR',34:'TYPE_RELARRAY'}

PUSH = re.compile(rb'(?:\x6a(.)|\x68(....))', re.S)

def pushes_before(img, call_fo, want=13):
    """Walk back over a run of push imm / mov $imm,%ecx before a call."""
    args, i = [], call_fo
    # the ctor is preceded by: mov $this,%ecx (b9 imm32) then call (e8 rel32)
    this = None
    if img.d[call_fo - 5] == 0xb9:
        this = struct.unpack_from('<I', img.d, call_fo - 4)[0]
        i = call_fo - 5
    while len(args) < want:
        if img.d[i - 5] == 0x68:
            args.append(struct.unpack_from('<I', img.d, i - 4)[0]); i -= 5
        elif img.d[i - 2] == 0x6a:
            args.append(img.d[i - 1]); i -= 2
        else:
            return None, None
    return this, args  # walk-back order is already arg0..argN: cdecl pushes right to left

def classes(img, ctor_va):
    """Every hkClass the binary constructs, keyed by its `this` address."""
    out = {}
    rel_target = ctor_va
    for m in re.finditer(rb'\xe8(....)', img.d, re.S):
        fo = m.start()
        va = img.va(fo)
        if va is None: continue
        rel = struct.unpack_from('<i', img.d, fo + 1)[0]
        if va + 5 + rel != rel_target: continue
        this, args = pushes_before(img, fo)
        if this is None: continue
        name = img.cstr(args[0])
        if not name: continue
        out[this] = dict(this=this, name=name, parent=args[1], size=args[2],
                         enums=args[5], nenums=args[6],
                         members=args[7], nmembers=args[8], defaults=args[9],
                         flags=args[11], version=args[12])
    return out

def enums(img, klass):
    """The enums a class declares: hkClassEnum is {name, items, numItems, ...}
    and an item is {value, name}."""
    out = {}
    for i in range(klass.get('nenums', 0)):
        e = klass['enums'] + 20 * i
        name = img.cstr(img.u32(e))
        items, n = img.u32(e + 4), img.u32(e + 8)
        if not name or not items or not n or n > 256:
            continue
        out[name] = {}
        for j in range(n):
            o = img.off(items + 8 * j)
            value = struct.unpack_from('<i', img.d, o)[0]
            out[name][img.cstr(struct.unpack_from('<I', img.d, o + 4)[0])] = value
    return out


def defaults(img, klass, rows):
    """A class's declared defaults: one int per member, -1 where there is none,
    followed by the values.  Zero is a real value in Havok, so a member without
    a default is not the same as a member defaulting to zero."""
    base = klass.get('defaults')
    if not base or img.off(base) is None:
        return {}
    o = img.off(base)
    offs = struct.unpack_from('<%di' % len(rows), img.d, o)
    out = {}
    for r, off in zip(rows, offs):
        if off < 0:
            continue
        raw = img.d[o + off:o + off + 4]
        t = r['type']
        if t == 'TYPE_REAL':
            out[r['name']] = struct.unpack('<f', raw)[0]
        elif t == 'TYPE_BOOL':
            out[r['name']] = bool(raw[0])
        elif t in ('TYPE_INT8', 'TYPE_UINT8', 'TYPE_ENUM', 'TYPE_FLAGS'):
            out[r['name']] = raw[0]
        elif t in ('TYPE_INT16', 'TYPE_UINT16'):
            out[r['name']] = struct.unpack('<h', raw[:2])[0]
        elif t in ('TYPE_INT32', 'TYPE_UINT32'):
            out[r['name']] = struct.unpack('<i', raw)[0]
        elif t == 'TYPE_CSTRING':
            out[r['name']] = img.cstr(struct.unpack('<I', raw)[0])
    return out


def members(img, klass, byaddr):
    rows = []
    for i in range(klass['nmembers']):
        m = klass['members'] + 24 * i
        o = img.off(m)
        nm = img.cstr(img.u32(m))
        t, st, ca = struct.unpack_from('<BBh', img.d, o + 12)
        fl, mo = struct.unpack_from('<HH', img.d, o + 16)
        cls = img.u32(m + 4)
        rows.append(dict(name=nm, cls=byaddr.get(cls, {}).get('name'), enum=img.u32(m + 8),
                         type=TYPES.get(t, t), subtype=TYPES.get(st, st),
                         carray=ca, flags=fl, offset=mo))
    return rows

if __name__ == '__main__':
    img = Image(sys.argv[1])
    cs = classes(img, int(sys.argv[2], 16))
    print(f'{len(cs)} classes', file=sys.stderr)
    wanted = sys.argv[3:] or None
    for k in sorted(cs.values(), key=lambda c: c['name']):
        if wanted and k['name'] not in wanted: continue
        par = cs.get(k['parent'], {}).get('name')
        print(f"\n== {k['name']}  size={k['size']} parent={par} version={k['version']} flags={k['flags']}")
        for r in members(img, k, cs):
            print(f"  +0x{r['offset']:03x} {r['name']:<38} {r['type']:<22} {r['subtype']:<16} "
                  f"carray={r['carray']} flags={r['flags']} class={r['cls']}")
