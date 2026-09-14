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

    # Objects are declared in the file in reverse, but hkaAnimationContainer's
    # own `animations` array keeps the order they were added in -- and that is
    # the order hkmeasure sees as ac.m_bindings.  Drive off the array.
    order = re.search(r'<hkparam name="animations" numelements="\d+">(.*?)</hkparam>', s, re.S)
    ids = re.findall(r'#(\d+)', order.group(1)) if order else []
    if len(ids) != len(travels):
        sys.exit(f"{len(ids)} animations but {len(travels)} travel values")

    nextid = max(int(m) for m in re.findall(r'<hkobject name="#(\d+)"', s)) + 1

    def block(aid):
        """The text of one hkobject, located fresh each time so edits can't stale it."""
        m = re.search(r'<hkobject name="#%s" class="[^"]*"[^>]*>.*?</hkobject>' % aid, s, re.S)
        if not m:
            sys.exit(f"object #{aid} not found")
        return m

    blocks = []
    for aid, travel in zip(ids, travels):
        m = block(aid)
        dur = float(re.search(r'name="duration">([0-9.eE+-]+)<', m.group(0)).group(1))
        frames = int(re.search(r'name="transforms" numelements="(\d+)"', m.group(0)).group(1))

        rid = nextid
        nextid += 1
        samples = []
        for i in range(frames):
            t = 0.0 if frames == 1 else i / (frames - 1)
            v = [travel * t, 0.0, 0.0, 0.0][:ncomp]
            samples.append("(" + " ".join(f"{c:.6f}" for c in v) + ")")
        axis = "0.000000 0.000000 1.000000" + (" 0.000000" if ncomp == 4 else "")
        fwd = "1.000000 0.000000 0.000000" + (" 0.000000" if ncomp == 4 else "")

        blocks.append(
            f'\t\t<hkobject name="#{rid:04d}" class="hkaDefaultAnimatedReferenceFrame" signature="{SIG}">\n'
            f'\t\t\t<hkparam name="up">({axis})</hkparam>\n'
            f'\t\t\t<hkparam name="forward">({fwd})</hkparam>\n'
            f'\t\t\t<hkparam name="duration">{dur:.6f}</hkparam>\n'
            f'\t\t\t<hkparam name="referenceFrameSamples" numelements="{frames}">\n\t\t\t\t'
            + "\n\t\t\t\t".join(samples) + "\n\t\t\t</hkparam>\n\t\t</hkobject>\n\n")

        patched = m.group(0).replace('<hkparam name="extractedMotion">null</hkparam>',
                                     f'<hkparam name="extractedMotion">#{rid:04d}</hkparam>', 1)
        s = s[:m.start()] + patched + s[m.end():]
        print(f"  anim #{aid}: duration={dur} frames={frames} travel={travel}"
              f" speed={travel / dur:g} -> refframe #{rid:04d}")

    anchor = '\t<hksection name="__data__">\n'
    s = s.replace(anchor, anchor + "\n" + "".join(blocks), 1)
    open(out, "w").write(s)
    print(f"wrote {out}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], [float(x) for x in sys.argv[3].split(",")],
         int(sys.argv[4]) if len(sys.argv) > 4 else 4)
