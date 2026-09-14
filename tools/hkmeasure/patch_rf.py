#!/usr/bin/env python3
"""Give each animation in an XML packfile a constant-velocity extracted motion.

hkaDefaultAnimatedReferenceFrame has no managed constructor, so the Behavior
tool cannot emit one; it is cheaper to add it as text than to hand-write a
whole packfile.  travel[i] is the distance along +X clip i covers over its
own duration.
"""
import re, sys

SIG = "0x6d85e445"

def main(path, out, travels, ncomp=4):
    s = open(path).read()
    anims = [(m.start(), m.group(1)) for m in
             re.finditer(r'<hkobject name="#(\d+)" class="hkaInterleavedUncompressedAnimation"', s)]
    if len(anims) != len(travels):
        sys.exit(f"{len(anims)} animations but {len(travels)} travel values")

    nextid = max(int(m) for m in re.findall(r'<hkobject name="#(\d+)"', s)) + 1
    blocks = []
    for (pos, aid), travel in zip(anims, travels):
        body = s[pos:s.index("</hkobject>", pos)]
        dur = float(re.search(r'name="duration">([0-9.eE+-]+)<', body).group(1))
        frames = int(re.search(r'name="transforms" numelements="(\d+)"', body).group(1))

        rid = nextid; nextid += 1
        samples = []
        for i in range(frames):
            t = 0.0 if frames == 1 else i / (frames - 1)
            v = [travel * t, 0.0, 0.0, 0.0][:ncomp]
            samples.append("(" + " ".join(f"{c:.6f}" for c in v) + ")")
        up = "(0.000000 0.000000 1.000000 0.000000)"[:None] if ncomp == 4 else "(0.000000 0.000000 1.000000)"
        fwd = "(1.000000 0.000000 0.000000 0.000000)" if ncomp == 4 else "(1.000000 0.000000 0.000000)"

        blocks.append(
            f'\t\t<hkobject name="#{rid:04d}" class="hkaDefaultAnimatedReferenceFrame" signature="{SIG}">\n'
            f'\t\t\t<hkparam name="up">{up}</hkparam>\n'
            f'\t\t\t<hkparam name="forward">{fwd}</hkparam>\n'
            f'\t\t\t<hkparam name="duration">{dur:.6f}</hkparam>\n'
            f'\t\t\t<hkparam name="referenceFrameSamples" numelements="{frames}">\n\t\t\t\t'
            + "\n\t\t\t\t".join(samples) + "\n\t\t\t</hkparam>\n\t\t</hkobject>\n\n")

        # point this animation at its new reference frame
        patched = body.replace('<hkparam name="extractedMotion">null</hkparam>',
                               f'<hkparam name="extractedMotion">#{rid:04d}</hkparam>', 1)
        s = s[:pos] + patched + s[pos + len(body):]
        print(f"  anim #{aid}: duration={dur} frames={frames} travel={travel} -> refframe #{rid:04d}")

    anchor = '\t<hksection name="__data__">\n'
    s = s.replace(anchor, anchor + "\n" + "".join(blocks), 1)
    open(out, "w").write(s)
    print(f"wrote {out}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], [float(x) for x in sys.argv[3].split(",")],
         int(sys.argv[4]) if len(sys.argv) > 4 else 4)
