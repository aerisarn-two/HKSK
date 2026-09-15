"""Write the hkbCharacterData packfile HBT would export into Gameview/Characters.

The tutorial project ships the rig (hka/hkx) and the behaviour, but the runtime
character asset only exists once HBT has exported it, so GameView finds nothing
to load and dereferences the null hkbCharacterSetup::data.  This writes that
asset directly.
"""
import sys

sys.path.insert(0, __file__.rsplit('/', 1)[0])
import hkpack

DECLARE = ['hkBaseObject', 'hkReferencedObject', 'hkbVariableValue', 'hkbVariableInfo',
           'hkbCharacterStringData', 'hkbCharacterData',
           'hkRootLevelContainerNamedVariant', 'hkRootLevelContainer']
UNTYPED = {'hkbVariableValueSet', 'hkbMirroredSkeletonInfo'}


def emit(exe, name, rig, behavior, anim_names=(), anim_files=(), ragdoll=None):
    img, cs, byname = hkpack.load(exe)
    types, ids = hkpack.types_section(img, cs, byname, DECLARE, UNTYPED)
    rag = ragdoll if ragdoll else '&#0;'

    def strings(vals):
        if not vals:
            return ' numelements="0"></hkparam>'
        body = "\n".join(f"\t\t\t\t<hkcstring>{v}</hkcstring>" for v in vals)
        return f' numelements="{len(vals)}">\n{body}\n\t\t\t</hkparam>'

    names, files = strings(anim_names), strings(anim_files)
    data = hkpack.root('hkbCharacterData', '#0101', ids['hkbCharacterData']) + f"""
\t\t<hkobject name="#0101" class="hkbCharacterData">
\t\t\t<hkparam name="memSizeAndFlags"><!-- zero memSizeAndFlags --></hkparam>
\t\t\t<hkparam name="referenceCount"><!-- zero referenceCount --></hkparam>
\t\t\t<hkparam name="modelUpMS">(0.000000 0.000000 1.000000 0.000000)</hkparam>
\t\t\t<hkparam name="modelForwardMS">(0.000000 1.000000 0.000000 0.000000)</hkparam>
\t\t\t<hkparam name="modelRightMS">(1.000000 0.000000 0.000000 0.000000)</hkparam>
\t\t\t<hkparam name="characterPropertyInfos" numelements="0"></hkparam>
\t\t\t<hkparam name="characterPropertyValues">null</hkparam>
\t\t\t<hkparam name="scale">1.000000</hkparam>
\t\t\t<hkparam name="stringData">#0102</hkparam>
\t\t\t<hkparam name="mirroredSkeletonInfo">null</hkparam>
\t\t</hkobject>

\t\t<hkobject name="#0102" class="hkbCharacterStringData">
\t\t\t<hkparam name="memSizeAndFlags"><!-- zero memSizeAndFlags --></hkparam>
\t\t\t<hkparam name="referenceCount"><!-- zero referenceCount --></hkparam>
\t\t\t<hkparam name="deformableSkinNames" numelements="0"></hkparam>
\t\t\t<hkparam name="rigidSkinNames" numelements="0"></hkparam>
\t\t\t<hkparam name="animationNames"{names}
\t\t\t<hkparam name="animationFilenames"{files}
\t\t\t<hkparam name="characterPropertyNames" numelements="0"></hkparam>
\t\t\t<hkparam name="name">{name}</hkparam>
\t\t\t<hkparam name="rigName">{rig}</hkparam>
\t\t\t<hkparam name="ragdollName">{rag}</hkparam>
\t\t\t<hkparam name="behaviorFilename">{behavior}</hkparam>
\t\t</hkobject>
"""
    return hkpack.packfile(types, data)


if __name__ == '__main__':
    exe, name, rig, behavior = sys.argv[1:5]
    pairs = [a.split('=', 1) for a in sys.argv[5:]]
    sys.stdout.write(emit(exe, name, rig, behavior,
                          [a for a, _ in pairs], [b for _, b in pairs]))
