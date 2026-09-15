"""Write a Havok 6.6 XML packfile whose __types__ come from the runtime itself.

Everything here is driven by hkreflect's read of GameView_Release.exe, so the
class definitions in the file are the ones the binary constructs at start-up.
Two constraints the 6.6 reader imposes, both found by disassembling it:

  * classversion must be 2..7 -- at 8 it skips reading contentsversion and then
    strdups the null it left behind, faulting in hkString::strDup;
  * hkRootLevelContainer's signature in this version is 0xf598a34e.
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
import hkreflect as R

RDATA = (0xeac000, 0xeac000 + 0x1f077e)
SERIALIZE_IGNORED = 1024
ROOT_SIG = '0xf598a34e'


def load(exe):
    img = R.Image(exe)
    cs = R.classes(img, 0x88d6a0)
    return img, cs, live(img, cs)


def live(img, cs):
    """The classes this build actually uses.  The versioner keeps historical
    copies under the same names, so prefer the one whose member table sits in
    .rdata and fall back to any constructed instance that has a real size."""
    best, spare = {}, {}
    for k in cs.values():
        if not k['size'] and k['nmembers']:
            continue
        if not k['nmembers'] or RDATA[0] <= k['members'] < RDATA[1]:
            best.setdefault(k['name'], k)
        else:
            spare.setdefault(k['name'], k)
    for n, k in spare.items():
        best.setdefault(n, k)
    return best


def types_section(img, cs, byname, declare, untyped=()):
    """__types__ for `declare`, in order; members whose class is in `untyped`
    are written class="null" so their (always null) values drag nothing in."""
    ids = {n: f'#{i + 1:04d}' for i, n in enumerate(declare)}
    out = ['\t<hksection name="__types__">\n']
    for n in declare:
        k = byname[n]
        par = cs.get(k['parent'], {}).get('name')
        rows = R.members(img, k, cs) if k['nmembers'] else []
        out.append(f'\t\t<hkobject name="{ids[n]}" class="hkClass" signature="0xc6528005">')
        out.append(f'\t\t\t<hkparam name="name">{n}</hkparam>')
        out.append(f'\t\t\t<hkparam name="parent">{ids.get(par, "null")}</hkparam>')
        out.append(f'\t\t\t<hkparam name="objectSize">{k["size"]}</hkparam>')
        out.append('\t\t\t<hkparam name="numImplementedInterfaces">0</hkparam>')
        out.append('\t\t\t<hkparam name="declaredEnums" numelements="0"></hkparam>')
        out.append(f'\t\t\t<hkparam name="declaredMembers" numelements="{len(rows)}">')
        for r in rows:
            typ, sub = r['type'], r['subtype']
            if r['flags'] & SERIALIZE_IGNORED:
                typ, sub = 'TYPE_ZERO', r['type']
            cls = 'null' if r['cls'] in untyped else ids.get(r['cls'], 'null')
            out.append('\t\t\t\t<hkobject>')
            out.append(f'\t\t\t\t\t<hkparam name="name">{r["name"]}</hkparam>')
            out.append(f'\t\t\t\t\t<hkparam name="class">{cls}</hkparam>')
            out.append('\t\t\t\t\t<hkparam name="enum">null</hkparam>')
            out.append(f'\t\t\t\t\t<hkparam name="type">{typ}</hkparam>')
            out.append(f'\t\t\t\t\t<hkparam name="subtype">{sub}</hkparam>')
            out.append(f'\t\t\t\t\t<hkparam name="cArraySize">{r["carray"]}</hkparam>')
            out.append('\t\t\t\t\t<hkparam name="flags">0</hkparam>')
            out.append(f'\t\t\t\t\t<hkparam name="offset">{r["offset"]}</hkparam>')
            out.append('\t\t\t\t</hkobject>')
        out.append('\t\t\t</hkparam>')
        out.append('\t\t\t<hkparam name="defaults"><!-- zero defaults --></hkparam>')
        out.append('\t\t</hkobject>\n')
    out.append('\t</hksection>\n')
    return '\n'.join(out), ids


def root(name, obj_id, class_id):
    return (f'\t\t<hkobject name="#0100" class="hkRootLevelContainer" signature="{ROOT_SIG}">\n'
            f'\t\t\t<hkparam name="namedVariants" numelements="1">\n'
            f'\t\t\t\t<hkobject>\n'
            f'\t\t\t\t\t<hkparam name="name">{name}</hkparam>\n'
            f'\t\t\t\t\t<hkparam name="className">{name}</hkparam>\n'
            f'\t\t\t\t\t<hkparam name="variant">({obj_id} {class_id})</hkparam>\n'
            f'\t\t\t\t</hkobject>\n'
            f'\t\t\t</hkparam>\n'
            f'\t\t</hkobject>\n')


def packfile(types, data, version="Havok-6.6.0-r1", classversion=4):
    return (f'<?xml version="1.0" encoding="ascii"?>\n'
            f'<hkpackfile classversion="{classversion}" contentsversion="{version}">\n\n'
            f'{types}\n\t<hksection name="__data__">\n\n{data}\n\t</hksection>\n\n</hkpackfile>\n')
