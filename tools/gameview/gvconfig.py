"""Emit the hkbDemoConfig packfile GameView loads through -bconfig.

The class definitions in the __types__ section are read out of
GameView_Release.exe itself (hkreflect.py), so they are the runtime's own,
not the 2006 ones in the file shipped beside it.  Two things the 6.6 reader
insists on, both learned the hard way:

  * classversion must be 2..7 -- at 8 it never reads contentsversion and then
    strdups the null it left behind;
  * hkRootLevelContainer's signature here is 0xf598a34e, not the 2010.2 one.
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
import hkreflect as R

RDATA = (0xeac000, 0xeac000 + 0x1f077e)
# Pointers that are always null in this config: leave the member untyped rather
# than drag the whole physics class tree into the file.
UNTYPED = {'hkpGroupFilter', 'hkpRigidBody'}
DECLARE = ['hkBaseObject', 'hkReferencedObject', 'hkbDemoConfigCharacterInfo',
           'hkbDemoConfigTerrainInfo', 'hkbDemoConfigStickVariableInfo',
           'hkbDemoConfig', 'hkRootLevelContainerNamedVariant', 'hkRootLevelContainer']
SERIALIZE_IGNORED = 1024


def live(img, cs):
    """The classes this build actually uses.  The versioner keeps historical
    copies of the same names, so prefer the one whose member table sits in
    .rdata and fall back to any constructed instance with a real size."""
    best, spare = {}, {}
    for k in cs.values():
        if not k['size'] and k['name'] != 'hkBaseObject':
            continue
        if k['nmembers'] and RDATA[0] <= k['members'] < RDATA[1]:
            best.setdefault(k['name'], k)
        elif not k['nmembers']:
            best.setdefault(k['name'], k)
        else:
            spare.setdefault(k['name'], k)
    for n, k in spare.items():
        best.setdefault(n, k)
    return best


def types_section(img, cs, byname):
    ids = {n: f'#{i + 1:04d}' for i, n in enumerate(DECLARE)}
    out = ['\t<hksection name="__types__">\n']
    for n in DECLARE:
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
            cls = ids.get(r['cls'], 'null') if r['cls'] not in UNTYPED else 'null'
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


STICK = "\n".join([
    "\t\t\t\t<hkobject>",
    "\t\t\t\t\t<hkparam name=\"variableName\">unused</hkparam>",
    "\t\t\t\t\t<hkparam name=\"minValue\">0.000000</hkparam>",
    "\t\t\t\t\t<hkparam name=\"maxValue\">0.000000</hkparam>",
    "\t\t\t\t\t<hkparam name=\"minStickValue\">0.000000</hkparam>",
    "\t\t\t\t\t<hkparam name=\"maxStickValue\">0.000000</hkparam>",
    "\t\t\t\t\t<hkparam name=\"stickAxis\">0</hkparam>",
    "\t\t\t\t\t<hkparam name=\"stick\">0</hkparam>",
    "\t\t\t\t\t<hkparam name=\"complimentVariableValue\">false</hkparam>",
    "\t\t\t\t\t<hkparam name=\"negateVariableValue\">false</hkparam>",
    "\t\t\t\t</hkobject>"])


def emit(exe, root_path, project_data, character_data, up_axis=2,
         version="Havok-6.6.0-r1", classversion=4):
    img = R.Image(exe)
    cs = R.classes(img, 0x88d6a0)
    byname = live(img, cs)
    types, ids = types_section(img, cs, byname)
    sticks = "\n".join([STICK] * 12)
    data = f"""\t<hksection name="__data__">

\t\t<hkobject name="#0100" class="hkRootLevelContainer" signature="0xf598a34e">
\t\t\t<hkparam name="namedVariants" numelements="1">
\t\t\t\t<hkobject>
\t\t\t\t\t<hkparam name="name">hkbDemoConfig</hkparam>
\t\t\t\t\t<hkparam name="className">hkbDemoConfig</hkparam>
\t\t\t\t\t<hkparam name="variant">(#0101 {ids['hkbDemoConfig']})</hkparam>
\t\t\t\t</hkobject>
\t\t\t</hkparam>
\t\t</hkobject>

\t\t<hkobject name="#0101" class="hkbDemoConfig">
\t\t\t<hkparam name="memSizeAndFlags"><!-- zero memSizeAndFlags --></hkparam>
\t\t\t<hkparam name="referenceCount"><!-- zero referenceCount --></hkparam>
\t\t\t<hkparam name="characterInfo" numelements="1">#0102</hkparam>
\t\t\t<hkparam name="terrainInfo" numelements="0"></hkparam>
\t\t\t<hkparam name="skinAttributeIndices" numelements="0"></hkparam>
\t\t\t<hkparam name="buttonPressToEventMap">0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15</hkparam>
\t\t\t<hkparam name="buttonReleaseToEventMap">16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31</hkparam>
\t\t\t<hkparam name="worldUpAxis">{up_axis}</hkparam>
\t\t\t<hkparam name="extraCharacterClones">0</hkparam>
\t\t\t<hkparam name="numTracks">14</hkparam>
\t\t\t<hkparam name="proxyHeight">2.000000</hkparam>
\t\t\t<hkparam name="proxyRadius">0.600000</hkparam>
\t\t\t<hkparam name="proxyOffset">0.000000</hkparam>
\t\t\t<hkparam name="rootPath">{root_path}</hkparam>
\t\t\t<hkparam name="projectDataFilename">{project_data}</hkparam>
\t\t\t<hkparam name="useAttachments">false</hkparam>
\t\t\t<hkparam name="useProxy">false</hkparam>
\t\t\t<hkparam name="useSkyBox">false</hkparam>
\t\t\t<hkparam name="useTrackingCamera">false</hkparam>
\t\t\t<hkparam name="accumulateMotion">true</hkparam>
\t\t\t<hkparam name="testCloning">false</hkparam>
\t\t\t<hkparam name="useSplineCompression">false</hkparam>
\t\t\t<hkparam name="stickVariables" numelements="12">
{sticks}
\t\t\t</hkparam>
\t\t\t<hkparam name="gamePadToRotateTerrainAboutItsAxisMap">0 1 2 3 4 5</hkparam>
\t\t\t<hkparam name="gamePadToAddRemoveCharacterMap">0 1</hkparam>
\t\t\t<hkparam name="filter">null</hkparam>
\t\t</hkobject>

\t\t<hkobject name="#0102" class="hkbDemoConfigCharacterInfo">
\t\t\t<hkparam name="memSizeAndFlags"><!-- zero memSizeAndFlags --></hkparam>
\t\t\t<hkparam name="referenceCount"><!-- zero referenceCount --></hkparam>
\t\t\t<hkparam name="characterDataFilename">{character_data}</hkparam>
\t\t\t<hkparam name="initialPosition">(0.000000 0.000000 0.000000 0.000000)</hkparam>
\t\t\t<hkparam name="initialRotation">(0.000000 0.000000 0.000000 1.000000)</hkparam>
\t\t\t<hkparam name="modelUpAxis">{up_axis}</hkparam>
\t\t\t<hkparam name="ragdollBoneLayers" numelements="0"></hkparam>
\t\t</hkobject>

\t</hksection>
"""
    return (f'<?xml version="1.0" encoding="ascii"?>\n'
            f'<hkpackfile classversion="{classversion}" contentsversion="{version}">\n\n'
            f'{types}\n{data}\n</hkpackfile>\n')


if __name__ == '__main__':
    exe, root, proj, char = sys.argv[1:5]
    sys.stdout.write(emit(exe, root, proj, char))
