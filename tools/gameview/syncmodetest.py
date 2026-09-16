"""Ask the runtime when a state machine's sync variable is consulted.

    python3 syncmodetest.py [gv-dir]

`ActiveGenerators.StateIdOf` reads `m_syncVariableIndex` only when
`m_startStateMode` is `START_STATE_MODE_SYNC`, and otherwise takes
`m_startStateId`. That guard decides where two creatures rest: the riekling's
combat machine is in sync mode on a variable that starts at its equip state, and
the benthic lurker's locomotion machines are in it on `iState`, which is what lets
their keys be read off their state ids.

The rule was read from the runtime's own reflection and the corpus. This measures
it: one machine, two states, `startStateId` naming the first and the sync variable
naming the second, run once in each mode.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gvbehavior as B
import hkpack
import livestate as L
import runtest

ANIM_A = r'..\..\Shared Animations\gun_sweep_left_to_right_animation_only_stationary.hkx'
ANIM_B = r'..\..\Shared Animations\gun_sweep_low_to_high_animation_only_stationary.hkx'


def machine(mode):
    """startStateId says 0 and the sync variable says 1; which wins."""
    return B.Node(
        'hkbStateMachine', 'Machine', startStateId=0, startStateMode=mode,
        syncVariableIndex=0, returnToPreviousStateEventId=-1, randomTransitionEventId=-1,
        states=[
            B.Node('hkbStateMachineStateInfo', 'S0', stateId=0, probability=1.0, enable=True,
                   generator=B.Clip('C0', ANIM_A, mode='MODE_LOOPING', binding=0)),
            B.Node('hkbStateMachineStateInfo', 'S1', stateId=1, probability=1.0, enable=True,
                   generator=B.Clip('C1', ANIM_B, mode='MODE_LOOPING', binding=1)),
        ])


def run(gv, mode, samples=4, interval=0.3):
    graph = B.Behaviour(root=machine(mode), variables={'Sync': 1})   # an int: a state id
    runtest.stage(gv, graph, [ANIM_A, ANIM_B])

    img, cs, byname = hkpack.load(os.path.join(gv, 'GameView_Release.exe'))
    proc, mem = L.launch(gv)
    try:
        vtmap = L.vtable_map(mem, cs)
        found = L.graphs(mem, vtmap)
        if not found:
            raise RuntimeError('no live behaviour graph')

        seen = []
        for _ in range(samples):
            for _d, _a, name, state in L.walk(
                    mem, img, cs, byname, vtmap, mem.u32(found[0] + 0x28)):
                if name == 'hkbStateMachine':
                    seen.append(dict(state).get('currentStateId'))
            time.sleep(interval)

        return seen
    finally:
        proc.kill()


if __name__ == '__main__':
    gv = sys.argv[1] if len(sys.argv) > 1 else '/tmp/claude-1000/gv'

    for mode in ('START_STATE_MODE_DEFAULT', 'START_STATE_MODE_SYNC'):
        print(f'{mode:28} startStateId=0, sync variable=1 -> currentStateId {run(gv, mode)}')
