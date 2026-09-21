# Animation as the setpoint of physics: how far the ragdoll can be driven

The question: is driving the physics bodies toward the animation generally
available, or is a creature either animated or ragdolled, with a switch between?
The answer is that the driven mode is the *default* whenever the ragdoll is in the
physics world, because every shipped project keeps the driver in its root modifier
list, and the only gate is who puts the ragdoll into the world. Evidence: the graphs
through HKX2 (`0_master`, `horsebehavior`, `atronachfrostbehavior`,
`deerbehavior`, `bearbehavior`, `draugrbehavior`, `dragonbehavior`), the skeletons'
ragdoll constraints, and the executable (AE layout, addresses as in
`docs/animation-events.md`).

## 1. The three ways a body can follow the animation

Havok gives a ragdoll driver (`hkbRagdollDriver`, created with the character at
`0x140aac520`) three controls, each a modifier the graph places, and Skyrim's
executable registers all three:

- **`hkbKeyframeBonesModifier`**: the listed bones are keyframed, that is moved
  kinematically to the animated pose. They push, they cannot be pushed;
- **`hkbRigidBodyRagdollControlsModifier`**: the bodies stay dynamic and a
  hierarchy controller drives them toward the animated pose with gains
  (`hierarchyGain`, `velocityGain`, `positionGain`, `accelerationGain`, the `snap`
  set with its distance and velocity caps). This is animation as the setpoint of a
  dynamic body: it goes where the animation says unless something stops it;
- **`hkbPoweredRagdollControlsModifier`**: the constraint motors between the bodies
  are driven toward the animated joint angles (`maxForce`, `tau`, `damping`,
  `proportionalRecoveryVelocity`, `constantRecoveryVelocity`), so the body is a
  motorised puppet that gravity and contacts can bend within the force limit. It
  needs motors in the ragdoll, and every shipped `skeleton.hkx` carries one
  `hkpPositionConstraintMotor` beside its `hkpRagdollConstraintData` and
  `hkpLimitedHingeConstraintData`.

Both driving modifiers take a bone list, empty meaning all, so a body can be driven
on some bones and free on the rest.

## 2. What the shipped graphs do with them

Every project, in the modifier list of its **root** modifier generator, active for
the whole life of the actor:

- `DriveRagdollRB`, a rigid-body ragdoll control on all bones with the same gains
  in every file read: `hierarchyGain 0.17`, `velocityGain 0.6`, `positionGain
  0.05`, `accelerationGain 1`, `velocityDamping 0`, snap gain 0.1 within 0.03 m and
  0.1 rad at 0.3 units/s, `durationToBlend 0.5`;
- `KeyframeLowerBody`, keyframing the pelvis and legs: 8 bones on the character
  and the draugr, 17 on the bear's root, 6 on the horse.

So while the ragdoll is in the world, the legs are kinematic and the rest is driven
dynamically toward the pose. The same pair sits again in the get-up modifier list.

**The death transition** is the one place Bethesda uses the driven mode on purpose:
`AnimateToRagdoll` (the character's `AnimToRagdollModGen`, the dragon's
`MG_AnimateToRagdoll`, the bear's, the draugr's) enters with `AddRagdollToWorld`,
keyframes the full body (`KeyframeFullRagdoll`, 18 bones on the humanoids, 22 on
the dragon), keeps `DriveRagdollRB`, and listens for contacts with
`BSRagdollContactListenerModifier` on 13 or 14 bones; the death clip plays on a body
that is in the world and follows it, and after `durationToBlend` the graph moves
to `FullyRagdoll`, whose entry sends `RemoveCharacterControllerFromWorld` and lets
go. The kill-move states (`draugrbehavior`'s `KillMove_State`, the bear's paired
kill) do the same with a contact listener on a few bones, which is how the victim's
body registers the blow.

**The get-up** is the one use of the powered mode: `MatchAndSendGetup` holds
`PoweredRagdollMatching` on all bones with `maxForce 200`, `tau 0.8`, `damping 1`,
recovery velocities 2 and 1, and pose matching (`mode 2`) on bones 0, 11 and 10,
that is the root, the spine and the pelvis on the humanoids (0, 9, 10 on the
horse): the motors pull the fallen body toward the get-up animation while the model
frame is matched to the ragdoll's pose, and `GetUpStart` then re-attaches the
controller (`docs/animation-events.md` §7). A `PoweredRagdollNoMatching` twin with
`maxForce 0` and `mode 4` exists in the same files and belongs to no list.

## 3. The gate: who puts the ragdoll into the world

The driver runs only on a ragdoll that is in the physics world, and the ragdoll of a
living actor normally is not: the character controller's capsule is its body. The
executable adds it in five places:

- the **`AddRagdollToWorld` handler** (`docs/animation-events.md` §7), sent by the
  graph from `AnimateToRagdoll`, `KeyframeToRagdoll` and the kill-move states;
- the **death code** (`0x1406972d0`, the function that logs who was killed by
  whom), for a death without the animation;
- the **sit and sleep state** (`0x140711b60`): entering a furniture sets the
  ragdoll's collision layer to `BIPED_NO_CC` (33) and adds the ragdoll
  (`0x140655a00`); leaving sets it back to `BIPED` (8) and removes it. A seated or
  sleeping actor is therefore a living actor whose ragdoll is in the world and
  driven by the root's `DriveRagdollRB` toward the sit animation, on a layer that
  does not collide with character controllers. This is the shipped proof that the
  engine tolerates the driven mode on a living body;
- the **3D load** of a placed reference whose model carries a ragdoll
  (`0x1401d42f0`, `0x1401d46d0`, `0x140219ad0`), for dead bodies and animated
  statics;
- the **Papyrus natives** `ForceAddRagdollToWorld` and `ForceRemoveRagdollFromWorld`
  (`0x140a2acd0`, "Object Reference cannot be found, cannot add/remove ragdoll to
  world"), which call the graph's add (`BShkbAnimationGraph` virtual 2,
  `0x140bb6c60`, building the bodies from the current pose) with no other change.

`RagdollStart` sets the graph's drive weight to 1.0 (virtual `0xB`) and takes the
controller out; `AddRagdollToWorld` does neither. Removing the controller is what
makes an actor fall; adding the ragdoll alone leaves it standing on the controller
with a driven body around it.

## 4. General availability, then

Driving the physics from the animation is available to any actor at any time, from
data or script, and needs no graph change to start: `ForceAddRagdollToWorld` on a
living actor puts its ragdoll into the world, and the root's `DriveRagdollRB` and
`KeyframeLowerBody` take over, legs kinematic, body driven toward every clip the
graph plays. What that buys: limbs that collide with clutter and doors, a body that
props hit, arrows that stick where they land on a driven body, and impulses that
deflect the pose within the gains and snap back. What it costs: a second physics
body per actor, and a collision layer to choose, since the sit code picks
`BIPED_NO_CC` to keep the ragdoll off the controllers and the script native leaves
the layer as loaded.

What a graph can add on top, all from the classes above:

- **partial driving**: a bone list on `hkbRigidBodyRagdollControlsModifier` or
  `hkbKeyframeBonesModifier` in a state, so that a tail, a cloak's bones, a
  tentacle or a mane are dynamic while the body stays keyframed. The driver drives
  the listed bones toward the animation with the gains; with low `positionGain`
  and `hierarchyGain` they trail and swing, which is the secondary motion the
  registered modifier set otherwise lacks (`hkbJigglerModifier` is not registered);
- **powered driving**: `hkbPoweredRagdollControlsModifier` with a bone list and a
  `maxForce`, for a body part that should be bent by the world and recover, a
  stagger from a hit that the animation cannot know, a limb that yields to a wall;
- **contacts as events**: `BSRagdollContactListenerModifier` on chosen bones raises
  its event on contact, which is how kill moves and the death transition know the
  body touched something, and how a graph could react to a driven limb hitting an
  obstacle;
- **pose matching** (`worldFromModelModeData`, mode 2) to let the model frame follow
  the bodies rather than the other way, as the get-up does.

The mode Bethesda never used is the middle one: a living, walking actor with the
ragdoll in the world and a subset of bones driven softly. Nothing found refuses it;
the death transition drives all bones, the sit drives all bones with the controller
present, and the modifiers take bone lists.

## 5. Open points

Only a run settles these: the right collision layer for a walking actor's driven
ragdoll (the sit code's `BIPED_NO_CC` avoids controllers but a walking body also
needs to avoid its own capsule); whether the get-up's pose matching is
disturbed by a script-added ragdoll on an actor that is later knocked down; the
gains for a trailing part, since the shipped ones are tuned to hold a dying body to
its clip; and the cost per actor at scale.

## 6. How this was measured

Six projects' graphs were dumped with HKX2 for every powered, rigid-body,
keyframe and contact-listener modifier, its owning modifier list, its fields two
levels deep, and the states whose entry events touch the ragdoll or the
controller. The skeletons were searched for constraint and motor classes. The
executable was read for the callers of the queued add and remove of the ragdoll,
the per-graph virtuals the handlers use, the Papyrus natives, the sit-state's
layer choice, and the collision-layer names.
