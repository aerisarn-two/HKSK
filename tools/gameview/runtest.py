"""Run a behaviour tree and return a trace of what the evaluator did.

    python3 runtest.py           # builds a two-clip blend and traces it

The graph is described in Python, written as a packfile, evaluated by the real
runtime, and sampled out of its memory.  Driving is by writing node members
directly: those writes persist, whereas a raw write to a variable does not
reach the members bound to it (see the README).
"""
import os
import struct
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gvbehavior
import gvchar
import gvconfig
import hkpack
import livestate as L

ANIM_A = r'..\..\Shared Animations\gun_sweep_left_to_right_animation_only_stationary.hkx'
ANIM_B = r'..\..\Shared Animations\gun_sweep_low_to_high_animation_only_stationary.hkx'


def stage(gv, graph, animations, project='proj/', data='Lesson01.hkx'):
    """Write the three files the runtime reads: graph, character, config."""
    exe = os.path.join(gv, 'GameView_Release.exe')
    root = os.path.join(gv, project)
    open(os.path.join(root, 'Behaviors', 'Made.xml'), 'w').write(graph.xml(exe))
    open(os.path.join(root, 'Characters', 'Made.hkx'), 'w').write(
        gvchar.emit(exe, 'Capt_Hi', 'Capt_Hi.hkx', 'Made.xml',
                    list(animations), [os.path.basename(a.replace('\\', '/'))
                                       for a in animations]))
    open(os.path.join(gv, 'Gameview', 'GameViewConfig.xml'), 'w').write(
        gvconfig.emit(exe, project, data, 'Made.hkx'))
    open(os.path.join(gv, 'hkdemo.cfg'), 'w').write(
        '-r null -g GameView -bload -l60 -bconfig Gameview/GameViewConfig.xml\n')


def trace(gv, samples=6, interval=0.25, drive=None):
    """Sample the live graph `samples` times.  `drive(mem, nodes)` is called
    once before sampling and may write node members."""
    exe = os.path.join(gv, 'GameView_Release.exe')
    img, cs, byname = hkpack.load(exe)
    proc, mem = L.launch(gv)
    try:
        vtmap = L.vtable_map(mem, cs)
        found = L.graphs(mem, vtmap)
        if not found:
            raise RuntimeError('no live behaviour graph')
        graph = found[0]
        if drive:
            drive(mem, L.walk(mem, img, cs, byname, vtmap, mem.u32(graph + 0x28)))
        out = []
        for _ in range(samples):
            out.append(L.walk(mem, img, cs, byname, vtmap, mem.u32(graph + 0x28)))
            time.sleep(interval)
        return out
    finally:
        proc.kill()


if __name__ == '__main__':
    gv = sys.argv[1] if len(sys.argv) > 1 else '/tmp/claude-1000/gv'
    graph = gvbehavior.Behaviour(
        variables={'Left': 0.0, 'Right': 0.0},
        root=gvbehavior.Blend('Root', [
            (1.0, gvbehavior.Clip('ClipL', ANIM_A, mode='MODE_LOOPING', binding=0)),
            (0.0, gvbehavior.Clip('ClipR', ANIM_B, mode='MODE_LOOPING', binding=1)),
        ]))
    stage(gv, graph, [ANIM_A, ANIM_B])

    def drive(mem, nodes):
        for _, addr, name, _ in nodes:
            if name == 'hkbBlenderGenerator':
                mem.write(addr + 0x1c, struct.pack('<f', 0.42))   # blendParameter

    for i, frame in enumerate(trace(gv, drive=drive)):
        print(f'--- sample {i}')
        for depth, addr, name, state in frame:
            live = {k: v for k, v in state if v}
            print('   ' + '  ' * depth, name, live)
