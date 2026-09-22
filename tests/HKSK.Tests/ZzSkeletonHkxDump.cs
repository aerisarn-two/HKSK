using System.Numerics;
using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The object graph of a shipped skeleton.hkx, to write a new creature's from prototypes.
public sealed class ZzSkeletonHkxDump
{
    [CorpusFact]
    public void Dump()
    {
        string path = Corpus.Path_("actors", "sabrecat", "character assets", "skeleton.hkx");
        var file = HavokFile.Load(path);
        var sb = new StringBuilder();
        sb.AppendLine($"objects {file.Objects.Count}: " + string.Join(", ", file.Objects.GroupBy(o => o.GetType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        foreach (var v in file.Root.m_namedVariants) sb.AppendLine($"variant '{v.m_name}' class={v.m_className} -> {v.m_variant?.GetType().Name}");
        var container = file.First<hkaAnimationContainer>()!;
        foreach (var sk in container.m_skeletons)
        {
            sb.AppendLine($"skeleton '{sk.m_name}' bones={sk.m_bones.Count} parents={sk.m_parentIndices.Count} refPose={sk.m_referencePose.Count} floats={sk.m_referenceFloats.Count} floatSlots={sk.m_floatSlots.Count} localFrames={sk.m_localFrames.Count}");
            for (int i = 0; i < Math.Min(4, sk.m_bones.Count); i++)
                sb.AppendLine($"   bone[{i}] '{sk.m_bones[i].m_name}' lock={sk.m_bones[i].m_lockTranslation} parent={sk.m_parentIndices[i]} pose T=({sk.m_referencePose[i].M41:F2},{sk.m_referencePose[i].M42:F2},{sk.m_referencePose[i].M43:F2}) row0=({sk.m_referencePose[i].M11:F2},{sk.m_referencePose[i].M12:F2},{sk.m_referencePose[i].M13:F2},{sk.m_referencePose[i].M14:F2})");
        }
        sb.AppendLine($"container: animations={container.m_animations.Count} bindings={container.m_bindings.Count} attachments={container.m_attachments.Count} mappers={container.m_skins.Count}");
        foreach (var m in file.All<hkaSkeletonMapper>())
        {
            var d = m.m_mapping;
            sb.AppendLine($"mapper A='{d.m_skeletonA?.m_name}' B='{d.m_skeletonB?.m_name}' simple={d.m_simpleMappings.Count} chain={d.m_chainMappings.Count} unmapped={d.m_unmappedBones.Count} type={d.m_mappingType} keepUnmappedLocal={d.m_keepUnmappedLocal} extracted={d.m_extractedMotionMapping}");
            foreach (var s in d.m_simpleMappings.Take(3)) sb.AppendLine($"   simple A={s.m_boneA} B={s.m_boneB} aFromB T=({s.m_aFromBTransform.M41:F2},{s.m_aFromBTransform.M42:F2},{s.m_aFromBTransform.M43:F2}) R00={s.m_aFromBTransform.M11:F3}");
        }
        var inst = file.First<hkaRagdollInstance>()!;
        sb.AppendLine($"ragdollInstance bodies={inst.m_rigidBodies.Count} constraints={inst.m_constraints.Count} map=[{string.Join(",", inst.m_boneToRigidBodyMap)}] skeleton='{inst.m_skeleton?.m_name}'");
        var pd = file.First<hkpPhysicsData>()!;
        sb.AppendLine($"physicsData systems={pd.m_systems.Count} worldCinfo={(pd.m_worldCinfo is null ? "null" : "set")}");
        foreach (var sys in pd.m_systems)
        {
            sb.AppendLine($"system name='{sys.m_name}' bodies={sys.m_rigidBodies.Count} constraints={sys.m_constraints.Count} actions={sys.m_actions.Count} phantoms={sys.m_phantoms.Count} active={sys.m_active} userData={sys.m_userData}");
            var rb = sys.m_rigidBodies[1] as hkpRigidBody;
            if (rb is not null)
            {
                sb.AppendLine($"  body[1] '{rb.m_name}' userData={rb.m_userData} shape={rb.m_collidable.m_shape?.GetType().Name} filter=0x{rb.m_collidable.m_broadPhaseHandle.m_collisionFilterInfo:x} bpType={rb.m_collidable.m_broadPhaseHandle.m_type} objQuality={rb.m_collidable.m_broadPhaseHandle.m_objectQualityType} allowedPen={rb.m_collidable.m_allowedPenetrationDepth} forceCollide={rb.m_collidable.m_forceCollideOntoPpu}");
                var mo = rb.m_motion;
                sb.AppendLine($"  motion type={mo.m_type} deactCounter={mo.m_deactivationIntegrateCounter} inertiaMassInv=({mo.m_inertiaAndMassInv.X:F5},{mo.m_inertiaAndMassInv.Y:F5},{mo.m_inertiaAndMassInv.Z:F5},{mo.m_inertiaAndMassInv.W:F5}) linVel={mo.m_linearVelocity} angVel={mo.m_angularVelocity} gravityFactor={mo.m_gravityFactor}");
                var ms = mo.m_motionState;
                sb.AppendLine($"  motionState T=({ms.m_transform.M41:F2},{ms.m_transform.M42:F2},{ms.m_transform.M43:F2}) linDamp={(float)ms.m_linearDamping} angDamp={(float)ms.m_angularDamping} timeFactor={(float)ms.m_timeFactor} maxLinVel={ms.m_maxLinearVelocity} maxAngVel={ms.m_maxAngularVelocity} deactClass={ms.m_deactivationClass} objectRadius={ms.m_objectRadius}");
                sb.AppendLine($"  material friction={rb.m_material.m_friction} restitution={rb.m_material.m_restitution} rolling={rb.m_material.m_rollingFrictionMultiplier} responseType={rb.m_material.m_responseType}; localFrame={rb.m_localFrame?.GetType().Name} properties={rb.m_properties.Count} contactPointCallbackDelay={rb.m_contactPointCallbackDelay} autoRemoveLevel={rb.m_autoRemoveLevel} numShapeKeysInContactPointProperties={rb.m_numShapeKeysInContactPointProperties} responseModifierFlags={rb.m_responseModifierFlags} uid={rb.m_uid}");
                if (rb.m_collidable.m_shape is hkpCapsuleShape cap) sb.AppendLine($"  capsule A={cap.m_vertexA} B={cap.m_vertexB} r={cap.m_radius} userData={cap.m_userData}");
            }
            var ci = sys.m_constraints[0] as hkpConstraintInstance;
            if (ci is not null)
            {
                sb.AppendLine($"  constraint[0] '{ci.m_name}' data={ci.m_data?.GetType().Name} entities=[{string.Join(",", ci.m_entities.Select(e => (e as hkpRigidBody)?.m_name))}] priority={ci.m_priority} wantRuntime={ci.m_wantRuntime} destructionRemapInfo={ci.m_destructionRemapInfo} userData={ci.m_userData} constraintModifiers={ci.m_constraintModifiers?.GetType().Name}");
                if (ci.m_data is hkpRagdollConstraintData rd)
                {
                    var a = rd.m_atoms;
                    sb.AppendLine($"  ragdoll atoms: transformsA T=({a.m_transforms.m_transformA.M41:F3},{a.m_transforms.m_transformA.M42:F3},{a.m_transforms.m_transformA.M43:F3}) B T=({a.m_transforms.m_transformB.M41:F3},{a.m_transforms.m_transformB.M42:F3},{a.m_transforms.m_transformB.M43:F3}); coneLimit max={a.m_coneLimit.m_maxAngle} twistAxis={a.m_coneLimit.m_twistAxisInA} refAxis={a.m_coneLimit.m_refAxisInB} memOffset={a.m_coneLimit.m_memOffsetToAngleOffset}; plane [{a.m_planesLimit.m_minAngle},{a.m_planesLimit.m_maxAngle}] twistAxis={a.m_planesLimit.m_twistAxisInA} refAxis={a.m_planesLimit.m_refAxisInB}; twist [{a.m_twistLimit.m_minAngle},{a.m_twistLimit.m_maxAngle}] twistAxis={a.m_twistLimit.m_twistAxis} refAxis={a.m_twistLimit.m_refAxis}; friction max={a.m_angFriction.m_maxFrictionTorque} firstFrictionAxis={a.m_angFriction.m_firstFrictionAxis} numAxes={a.m_angFriction.m_numFrictionAxes}; motorsEnabled={a.m_ragdollMotors.m_isEnabled}; ballSocket solving={a.m_ballSocket.m_solvingMethod} maxImpulse={a.m_ballSocket.m_maxImpulse}; setupStab={a.m_setupStabilization.m_enabled}");
                }
            }
        }
        void Walk(hkMemoryResourceContainer c, int depth)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}resource container '{c.m_name}' handles={c.m_resourceHandles.Count} children={c.m_children.Count}");
            foreach (var h in c.m_resourceHandles.Take(4)) sb.AppendLine($"{new string(' ', depth * 2)}  handle '{h.m_name}' -> {h.m_variant?.GetType().Name}");
            foreach (var k in c.m_children) Walk(k, depth + 1);
        }
        foreach (var c in file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkMemoryResourceContainer>()) Walk(c, 0);

        {
            var sk = file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().First().m_skeletons;
            foreach (var k in sk) sb.AppendLine($"EXTRA skeleton '{k.m_name}' unlocked=[{string.Join(",", k.m_bones.Select((b,i)=>(b,i)).Where(x=>!x.b.m_lockTranslation).Select(x=>x.i))}] memSize={k.m_memSizeAndFlags} ref={k.m_referenceCount} pose1={k.m_referencePose[1]}");
            var sys = file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkpPhysicsData>().First().m_systems[0];
            foreach (var b in sys.m_rigidBodies.Take(2))
            {
                var m = b.m_motion; var st = m.m_motionState; var sw = st.m_sweptTransform;
                sb.AppendLine($"EXTRA body '{b.m_name}' memSize={b.m_memSizeAndFlags} ref={b.m_referenceCount} storageIndex={b.m_storageIndex} damageMult={b.m_damageMultiplier} cMaster=({b.m_constraintsMaster.m_size},{b.m_constraintsMaster.m_capacityAndFlags}) slave={b.m_constraintsSlave.Count} runtime={b.m_constraintRuntime.Count} npData={b.m_npData} localFrame={b.m_localFrame} props={b.m_properties.Count}");
                sb.AppendLine($"EXTRA  motion memSize={m.m_memSizeAndFlags} ref={m.m_referenceCount} savedQ={m.m_savedQualityTypeIndex} savedMotion={m.m_savedMotion} deact={m.m_deactivationIntegrateCounter} grav={m.m_gravityFactor} transform={st.m_transform} deltaAngle={st.m_deltaAngle} timeFactor={st.m_timeFactor}");
                sb.AppendLine($"EXTRA  swept com0={sw.m_centerOfMass0} com1={sw.m_centerOfMass1} rot0={sw.m_rotation0} rot1={sw.m_rotation1} comLocal={sw.m_centerOfMassLocal}");
                var c = b.m_collidable;
                sb.AppendLine($"EXTRA  collidable shape={c.m_shape?.GetType().Name} shapeMem={c.m_shape?.m_memSizeAndFlags} shapeRef={c.m_shape?.m_referenceCount} shapeUser={c.m_shape?.m_userData} bpType={c.m_broadPhaseHandle.m_type} bv=({c.m_boundingVolumeData.m_expansionShift},{c.m_boundingVolumeData.m_padding}) forcePpu={c.m_forceCollideOntoPpu} entries={c.m_collisionEntries.Count} spu=({b.m_spuCollisionCallback.m_eventFilter},{b.m_spuCollisionCallback.m_userFilter}) material rollingMult={b.m_material.m_rollingFrictionMultiplier}");
            }
            foreach (var c in sys.m_constraints.Take(30).Where(c => c.m_data is hkpLimitedHingeConstraintData).Take(1))
            {
                var a = ((hkpLimitedHingeConstraintData)c.m_data).m_atoms;
                sb.AppendLine($"EXTRA hinge '{c.m_name}' ents=[{string.Join(",", c.m_entities.Select(e=>e.m_name))}] memSize={c.m_memSizeAndFlags} ref={c.m_referenceCount} listeners=({c.m_listeners.m_size},{c.m_listeners.m_capacityAndFlags}) dataMem={c.m_data.m_memSizeAndFlags} dataRef={c.m_data.m_referenceCount} tA={a.m_transforms.m_transformA} tB={a.m_transforms.m_transformB} limit=[{a.m_angLimit.m_minAngle},{a.m_angLimit.m_maxAngle}] axis={a.m_angLimit.m_limitAxis} enabled={a.m_angLimit.m_isEnabled} tau={a.m_angLimit.m_angularLimitsTauFactor} motor={a.m_angMotor.m_isEnabled} motorAxis={a.m_angMotor.m_motorAxis} friction={a.m_angFriction.m_maxFrictionTorque} 2d=({a.m_2dAng.m_freeRotationAxis}) bs=({a.m_ballSocket.m_solvingMethod},{a.m_ballSocket.m_maxImpulse})");
            }
            foreach (var c in sys.m_constraints.Where(c => c.m_data is hkpRagdollConstraintData).Take(1))
            {
                var a = ((hkpRagdollConstraintData)c.m_data).m_atoms;
                sb.AppendLine($"EXTRA ragdoll '{c.m_name}' memSize={c.m_memSizeAndFlags} ref={c.m_referenceCount} tA={a.m_transforms.m_transformA} tB={a.m_transforms.m_transformB} cone=(en={a.m_coneLimit.m_isEnabled},mode={a.m_coneLimit.m_angleMeasurementMode},min={a.m_coneLimit.m_minAngle},tau={a.m_coneLimit.m_angularLimitsTauFactor}) plane=(en={a.m_planesLimit.m_isEnabled},mode={a.m_planesLimit.m_angleMeasurementMode},twist={a.m_planesLimit.m_twistAxisInA},ref={a.m_planesLimit.m_refAxisInB},mem={a.m_planesLimit.m_memOffsetToAngleOffset}) twist=(en={a.m_twistLimit.m_isEnabled},tau={a.m_twistLimit.m_angularLimitsTauFactor}) motors=({a.m_ragdollMotors.m_isEnabled},{a.m_ragdollMotors.m_initializedOffset},{a.m_ragdollMotors.m_previousTargetAnglesOffset}) motorRefs={string.Join(",", a.m_ragdollMotors.m_motors.Select(x=>x?.GetType().Name ?? "null"))}");
            }
            var inst2 = file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkaRagdollInstance>().First();
            sb.AppendLine($"EXTRA instance same bodies as system: {inst2.m_rigidBodies.Zip(sys.m_rigidBodies).All(p => ReferenceEquals(p.First, p.Second))}; constraint objects shared: {inst2.m_constraints.Intersect(sys.m_constraints).Count()}; instance constraint[0] data kind {inst2.m_constraints[0].m_data?.GetType().Name} sys constraint[0] {sys.m_constraints[0].m_data?.GetType().Name}; order instance=[{string.Join(",", inst2.m_constraints.Take(6).Select(c=>c.m_name))}] system=[{string.Join(",", sys.m_constraints.Take(6).Select(c=>c.m_name))}]");
            var mp = file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkaSkeletonMapper>().ToList();
            foreach (var m in mp) sb.AppendLine($"EXTRA mapper memSize={m.m_memSizeAndFlags} ref={m.m_referenceCount} simple0={m.m_mapping.m_simpleMappings[0].m_aFromBTransform} simple1={m.m_mapping.m_simpleMappings[1].m_aFromBTransform} unmapped=[{string.Join(",", m.m_mapping.m_unmappedBones)}]");
            var rc = file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkMemoryResourceContainer>().First();
            var rag = rc.m_children[1]; var h = rag.m_resourceHandles[1]; var si = (hkpShapeInfo)h.m_variant!;
            sb.AppendLine($"EXTRA shapeInfo shape same as body0 shape: {ReferenceEquals(si.m_shape, sys.m_rigidBodies[0].m_collidable.m_shape)} hier={si.m_isHierarchicalCompound} hkd={si.m_hkdShapesCollected} names={si.m_childShapeNames.Count} childT={si.m_childTransforms.Count} transform={si.m_transform} memSize={si.m_memSizeAndFlags} ref={si.m_referenceCount}; handle0 variant same as body0: {ReferenceEquals(rag.m_resourceHandles[0].m_variant, sys.m_rigidBodies[0])} refs={h.m_references.Count} containerMem={rc.m_memSizeAndFlags} ragMem={rag.m_memSizeAndFlags} handleMem={h.m_memSizeAndFlags}");
            sb.AppendLine($"EXTRA root variants: {string.Join(" | ", file.Root.m_namedVariants.Select(v => $"{v.m_name}:{v.m_className}"))} container memSize={file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().First().m_memSizeAndFlags} physData memSize={file.Root.m_namedVariants.Select(v => v.m_variant).OfType<hkpPhysicsData>().First().m_memSizeAndFlags} sysMem={sys.m_memSizeAndFlags} instMem={inst2.m_memSizeAndFlags}");
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath(), "sabrecat_hkx_dump.txt"), sb.ToString());
    }
}
