"""Read the running evaluator's own state out of process memory.

The runtime reports nothing about the graph -- no log, no state over the
visual debugger -- but every field worth having is a reflected member:
hkbStateMachine::currentStateId, hkbBlenderGenerator::blendParameter and
numActiveChildren, hkbClipGenerator::time, hkbBehaviorGraph::variableValueSet.
So rather than reimplement the evaluator to find out what it decided, read what
it decided.

Objects are found by an anchor whose value we chose ourselves: a clip generator
is the object whose animationName points at a string we put in the character
file. Its first word is then the class's vtable, and every other instance of
that class is a word equal to it.

Yama restricts ptrace to descendants, so this launches the runtime itself
rather than attaching to one already running.
"""
import ctypes
import os
import struct
import subprocess
import sys
import time

libc = ctypes.CDLL("libc.so.6", use_errno=True)


class _iovec(ctypes.Structure):
    _fields_ = [("base", ctypes.c_void_p), ("len", ctypes.c_size_t)]


class Process:
    def __init__(self, pid):
        self.pid = pid

    def read(self, addr, n):
        buf = (ctypes.c_char * n)()
        local = _iovec(ctypes.cast(buf, ctypes.c_void_p), n)
        remote = _iovec(ctypes.c_void_p(addr), n)
        got = libc.process_vm_readv(self.pid, ctypes.byref(local), 1,
                                    ctypes.byref(remote), 1, 0)
        return bytes(buf[:got]) if got > 0 else None

    def u32(self, addr):
        d = self.read(addr, 4)
        return struct.unpack('<I', d)[0] if d else None

    def regions(self, writable=True):
        out = []
        for line in open(f'/proc/{self.pid}/maps'):
            f = line.split()
            lo, hi = (int(x, 16) for x in f[0].split('-'))
            if hi >= 1 << 32 or 'r' not in f[1]:
                continue
            if writable and 'w' not in f[1]:
                continue
            out.append((lo, hi))
        return out

    def scan(self, needle, writable=True, limit=64):
        """Every address holding `needle`, across the low 4G."""
        hits = []
        for lo, hi in self.regions(writable):
            chunk = self.read(lo, hi - lo)
            if not chunk:
                continue
            i = chunk.find(needle)
            while i >= 0:
                hits.append(lo + i)
                if len(hits) >= limit:
                    return hits
                i = chunk.find(needle, i + 1)
        return hits

    def pointers_to(self, addr, limit=64):
        return self.scan(struct.pack('<I', addr), limit=limit)


def launch(cwd, exe='GameView_Release.exe', settle=8.0):
    """Start the runtime as our own child so ptrace is permitted."""
    p = subprocess.Popen(['wine', exe], cwd=cwd,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                         env={**os.environ, 'WINEDEBUG': '-all', 'DISPLAY': ''})
    time.sleep(settle)
    for entry in os.listdir('/proc'):
        if not entry.isdigit():
            continue
        try:
            if open(f'/proc/{entry}/comm').read().strip().startswith('GameView'):
                return p, Process(int(entry))
        except OSError:
            continue
    p.kill()
    raise RuntimeError('the runtime did not come up')


def vtable_map(mem, cs):
    """vtable address -> class name, read out of the runtime's own registry.

    Havok keeps a pointer map from vtable to hkClass; in memory an entry is the
    two words side by side.  An hkClass object is itself {name, parent, ...},
    so a class name followed by a parent class looks identical -- those are
    rejected by refusing keys that are themselves known class addresses.
    """
    known = {k['this']: k['name'] for k in cs.values()}
    pairs = {}
    for lo, hi in mem.regions():
        blk = mem.read(lo, hi - lo)
        if not blk:
            continue
        words = struct.unpack_from('<%dI' % (len(blk) // 4), blk, 0)
        for i in range(len(words) - 1):
            key, cls = words[i], words[i + 1]
            if cls in known and 0x400000 < key < 0x1930000 and key not in known:
                pairs.setdefault(key, known[cls])
    return pairs


def array(mem, addr):
    """hkArray is {data, size, capacityAndFlags}."""
    data, size = mem.u32(addr), mem.u32(addr + 4)
    if not data or not size or size > 4096:
        return []
    return [mem.u32(data + 4 * i) for i in range(size)]


def node_state(mem, img, cs, byname, vtmap, addr):
    """The scalar fields of one node, named by its class."""
    import hkreflect as R
    name = vtmap.get(mem.u32(addr))
    if not name or name not in byname:
        return None, []
    out = []
    for r in R.members(img, byname[name], cs):
        raw = mem.read(addr + r['offset'], 4)
        if not raw:
            continue
        t = r['type']
        if t == 'TYPE_REAL':
            out.append((r['name'], round(struct.unpack('<f', raw)[0], 4)))
        elif t in ('TYPE_INT32', 'TYPE_UINT32'):
            out.append((r['name'], struct.unpack('<i', raw)[0]))
        elif t in ('TYPE_INT16', 'TYPE_UINT16'):
            out.append((r['name'], struct.unpack('<h', raw[:2])[0]))
        elif t in ('TYPE_BOOL', 'TYPE_INT8', 'TYPE_UINT8', 'TYPE_ENUM'):
            out.append((r['name'], raw[0]))
    return name, out


def walk(mem, img, cs, byname, vtmap, root, depth=0, seen=None, out=None):
    """The live node tree, followed through the reflected child members."""
    import hkreflect as R
    seen = set() if seen is None else seen
    out = [] if out is None else out
    if not root or root in seen or depth > 12:
        return out
    seen.add(root)
    name, state = node_state(mem, img, cs, byname, vtmap, root)
    if not name:
        return out
    out.append((depth, root, name, state))
    for r in R.members(img, byname[name], cs):
        kids = []
        if r['type'] == 'TYPE_POINTER' and r['cls']:
            kids = [mem.u32(root + r['offset'])]
        elif r['type'] == 'TYPE_ARRAY' and r['subtype'] == 'TYPE_POINTER':
            kids = array(mem, root + r['offset'])
        for k in kids:
            if k:
                walk(mem, img, cs, byname, vtmap, k, depth + 1, seen, out)
    return out


def graphs(mem, vtmap):
    """Every live hkbBehaviorGraph, found by its vtable and confirmed by the
    fact that a running one has a populated variable table."""
    found = []
    for vt, name in vtmap.items():
        if name != 'hkbBehaviorGraph':
            continue
        for obj in mem.scan(struct.pack('<I', vt), limit=8):
            if mem.u32(obj) != vt:
                continue
            values = mem.u32(obj + 0x50)
            if values and array(mem, values + 0x08):
                found.append(obj)
    return found


def variables(mem, graph):
    """The word variable table, as the evaluator currently holds it."""
    values = mem.u32(graph + 0x50)
    return [struct.unpack('<f', struct.pack('<I', v))[0] for v in array(mem, values + 0x08)]


if __name__ == '__main__':
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import hkpack

    gv = sys.argv[1] if len(sys.argv) > 1 else '/tmp/claude-1000/gv'
    exe = os.path.join(gv, 'GameView_Release.exe')
    img, cs, byname = hkpack.load(exe)
    proc, mem = launch(gv)
    try:
        vtmap = vtable_map(mem, cs)
        for g in graphs(mem, vtmap):
            print(f'hkbBehaviorGraph @{hex(g)}')
            print('  variables:', [round(v, 4) for v in variables(mem, g)])
            for depth, addr, name, state in walk(mem, img, cs, byname, vtmap,
                                                 mem.u32(g + 0x28)):
                live = {k: v for k, v in state if v}
                print('  ' + '  ' * depth, f'{name} @{hex(addr)}', live)
    finally:
        proc.kill()
