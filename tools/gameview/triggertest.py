"""Ask the runtime whether an end-of-clip trigger carries a state machine on.

    python3 triggertest.py [gv-dir]

The engine reads a graph "at rest" and assumes a clip that has been playing has
reached its end, so a trigger placed there has fired -- which is what carries the
player's bleedout out of its transition-in clip and the riekling out of its equip.
That was reasoned from the class and the corpus, not measured. This measures it.

The graph is a two-state machine. State 0 plays a clip carrying one trigger at
`relativeToEndOfClip`, raising the only event; state 0 transitions to state 1 on
that event and state 1 plays a different clip. Nothing else can move it. So if
the runtime is found in state 1, the trigger fired on its own.
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


def machine(with_trigger):
    """Two states; the first ends and says so, unless the trigger is left out."""
    triggers = B.Node('hkbClipTriggerArray', triggers=[{
        'localTime': 0.0,
        'event': {'id': 0, 'payload': None},
        'relativeToEndOfClip': True,
        'acyclic': False,
        'isAnnotation': False,
    }]) if with_trigger else None

    first = B.Clip('First', ANIM_A, mode='MODE_SINGLE_PLAY', binding=0,
                   **({'triggers': triggers} if triggers else {}))
    second = B.Clip('Second', ANIM_B, mode='MODE_SINGLE_PLAY', binding=1)

    leaving = B.Node('hkbStateMachineTransitionInfoArray', transitions=[{
        'eventId': 0, 'toStateId': 1, 'flags': 0, 'transition': None,
        'triggerInterval': {}, 'initiateInterval': {}, 'toNestedStateId': 0,
        'priority': 0, 'condition': None,
    }])

    return B.Node(
        'hkbStateMachine', 'Machine', startStateId=0,
        startStateMode='START_STATE_MODE_DEFAULT',
        # both default to 0, which is the event the trigger raises
        returnToPreviousStateEventId=-1, randomTransitionEventId=-1,
        states=[
            B.Node('hkbStateMachineStateInfo', 'S0', stateId=0,
                   generator=first, transitions=leaving, probability=1.0, enable=True),
            B.Node('hkbStateMachineStateInfo', 'S1', stateId=1,
                   generator=second, probability=1.0, enable=True),
        ])


def run(gv, with_trigger, samples=8, interval=0.4):
    # a graph with no variables at all does not load; one is enough
    graph = B.Behaviour(root=machine(with_trigger), variables={'Unused': 0.0},
                        events=['Ended'])
    runtest.stage(gv, graph, [ANIM_A, ANIM_B])

    exe = os.path.join(gv, 'GameView_Release.exe')
    img, cs, byname = hkpack.load(exe)
    proc, mem = L.launch(gv, settle=16.0)
    try:
        vtmap = L.vtable_map(mem, cs)
        found = L.graphs(mem, vtmap)
        if not found:
            raise RuntimeError('no live behaviour graph')

        seen = []
        for _ in range(samples):
            for _depth, _addr, name, state in L.walk(
                    mem, img, cs, byname, vtmap, mem.u32(found[0] + 0x28)):
                if name != 'hkbStateMachine':
                    continue
                fields = dict(state)
                seen.append((fields.get('currentStateId'), round(fields.get('timeInState') or 0, 2)))
            time.sleep(interval)

        return seen
    finally:
        proc.kill()


if __name__ == '__main__':
    gv = sys.argv[1] if len(sys.argv) > 1 else '/tmp/claude-1000/gv'

    for label, trigger in (('with an end-of-clip trigger', True), ('without one', False)):
        seen = run(gv, trigger)
        states = [s for s, _ in seen]
        print(f'{label:32} currentStateId over time: {states}')
        print(f'{"":32} timeInState: {[t for _, t in seen]}')
