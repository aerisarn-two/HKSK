using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// Every bone index the sabre cat's character file and graphs carry, resolved
// against its rig, so the cat's remap knows what has to move.
public sealed class ZzBoneBound
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        if (corpus.Length == 0) return;
        string folder = Path.Combine(corpus, "actors", "sabrecat");
        var sb = new StringBuilder();
        var skeleton = HavokFile.Load(Path.Combine(folder, "character assets", "skeleton.hkx"));
        var rig = skeleton.All<hkaAnimationContainer>().First().m_skeletons[0];
        var rag = skeleton.All<hkaAnimationContainer>().First().m_skeletons[1];
        string B(int i) => i >= 0 && i < rig.m_bones.Count ? $"{i}:{rig.m_bones[i].m_name}" : $"{i}:?";
        string R(int i) => i >= 0 && i < rag.m_bones.Count ? $"{i}:{rag.m_bones[i].m_name}" : $"{i}:?";
        foreach (string path in Directory.GetFiles(folder, "*.hkx", SearchOption.AllDirectories).Order())
        {
            if (path.Contains("animations", StringComparison.OrdinalIgnoreCase) || path.EndsWith("skeleton.hkx", StringComparison.OrdinalIgnoreCase)) continue;
            HavokFile file;
            try { file = HavokFile.Load(path); } catch (Exception e) { sb.AppendLine($"# {path}: {e.Message}"); continue; }
            sb.AppendLine($"## {Path.GetRelativePath(folder, path)}");
            foreach (var c in file.All<hkbCharacterData>())
            {
                sb.AppendLine($"  characterData numBonesPerLod=[{string.Join(",", c.m_numBonesPerLod)}] scale={c.m_scale} up={c.m_modelUpMS} fwd={c.m_modelForwardMS} right={c.m_modelRightMS} controller=(h={c.m_characterControllerInfo.m_capsuleHeight},r={c.m_characterControllerInfo.m_capsuleRadius},filter=0x{c.m_characterControllerInfo.m_collisionFilterInfo:x})");
                sb.AppendLine($"  propertyNames=[{string.Join(",", c.m_stringData?.m_characterPropertyNames ?? [])}] propertyInfos=[{string.Join(",", c.m_characterPropertyInfos.Select(p => p.m_type))}] values: words={c.m_characterPropertyValues?.m_wordVariableValues.Count} quads={c.m_characterPropertyValues?.m_quadVariableValues.Count} variants=[{string.Join(",", (c.m_characterPropertyValues?.m_variantVariableValues ?? []).Select(v => v?.GetType().Name ?? "null"))}]");
                sb.AppendLine($"  rig={c.m_stringData?.m_rigName} ragdoll={c.m_stringData?.m_ragdollName} lods=[{string.Join(",", c.m_stringData?.m_lodNames ?? [])}] retarget=[{string.Join(",", c.m_stringData?.m_retargetingSkeletonMapperFilenames ?? [])}] mirrorA=[{string.Join(",", c.m_stringData?.m_mirroredSyncPointSubstringsA ?? [])}] mirrorB=[{string.Join(",", c.m_stringData?.m_mirroredSyncPointSubstringsB ?? [])}]");
                if (c.m_footIkDriverInfo is { } ik)
                {
                    sb.AppendLine($"  footIk up={ik.m_raycastDistanceUp} down={ik.m_raycastDistanceDown} ground={ik.m_originalGroundHeightMS} vOff={ik.m_verticalOffset} filter=0x{ik.m_collisionFilterInfo:x} fwdAlign={ik.m_forwardAlignFraction} sideAlign={ik.m_sidewaysAlignFraction} sideWidth={ik.m_sidewaysSampleWidth} lock={ik.m_lockFeetWhenPlanted} charUp={ik.m_useCharacterUpVector} narrow={ik.m_isQuadrupedNarrow}");
                    foreach (var leg in ik.m_legs)
                        sb.AppendLine($"    leg hip={B(leg.m_hipIndex)} knee={B(leg.m_kneeIndex)} ankle={B(leg.m_ankleIndex)} kneeAxis={leg.m_kneeAxisLS} footEnd={leg.m_footEndLS} planted={leg.m_footPlantedAnkleHeightMS} raised={leg.m_footRaisedAnkleHeightMS} max={leg.m_maxAnkleHeightMS} min={leg.m_minAnkleHeightMS} kneeDeg=[{leg.m_minKneeAngleDegrees},{leg.m_maxKneeAngleDegrees}] ankleDeg={leg.m_maxAnkleAngleDegrees}");
                }
                if (c.m_mirroredSkeletonInfo is { } mi)
                    sb.AppendLine($"  mirrored axis={mi.m_mirrorAxis} pairs=[{string.Join(",", mi.m_bonePairMap.Select((p, i) => p == i ? "" : $"{i}->{p}").Where(s => s.Length > 0))}] count={mi.m_bonePairMap.Count}");
            }
            foreach (var w in file.All<hkbBoneWeightArray>())
                sb.AppendLine($"  boneWeights n={w.m_boneWeights.Count} nonOne=[{string.Join(",", w.m_boneWeights.Select((v, i) => (v, i)).Where(x => x.v != 1f).Select(x => $"{B(x.i)}={x.v}"))}]");
            foreach (var a in file.All<hkbBoneIndexArray>())
                sb.AppendLine($"  boneIndices n={a.m_boneIndices.Count} [{string.Join(",", a.m_boneIndices.Select(i => B(i)))}]");
            foreach (var m in file.All<BSLookAtModifier>())
                sb.AppendLine($"  BSLookAt '{m.m_name}' bones=[{string.Join("; ", m.m_bones.Select(b => $"{B(b.m_index)} fwd={b.m_fwdAxisLS} limit={b.m_limitAngleDegrees} on={b.m_onGain} off={b.m_offGain} en={b.m_enabled}"))}] eyes=[{string.Join("; ", m.m_eyeBones.Select(b => $"{B(b.m_index)} fwd={b.m_fwdAxisLS}"))}] limit={m.m_limitAngleDegrees}");
            foreach (var m in file.All<hkbKeyframeBonesModifier>())
                sb.AppendLine($"  KeyframeBones '{m.m_name}' info=[{string.Join(",", m.m_keyframeInfo.Select(k => B(k.m_boneIndex)))}] list=[{string.Join(",", (m.m_keyframedBonesList?.m_boneIndices ?? []).Select(i => B(i)))}]");
            foreach (var m in file.All<hkbPoweredRagdollControlsModifier>())
                sb.AppendLine($"  PoweredRagdoll '{m.m_name}' bones=[{string.Join(",", (m.m_bones?.m_boneIndices ?? []).Select(i => R(i)))}] weights={m.m_boneWeights?.m_boneWeights.Count}");
            foreach (var m in file.All<hkbRigidBodyRagdollControlsModifier>())
                sb.AppendLine($"  RigidBodyRagdoll '{m.m_name}' bones=[{string.Join(",", (m.m_bones?.m_boneIndices ?? []).Select(i => R(i)))}]");
            foreach (var m in file.All<BSRagdollContactListenerModifier>())
                sb.AppendLine($"  RagdollContact '{m.m_name}' bones=[{string.Join(",", (m.m_bones?.m_boneIndices ?? []).Select(i => R(i)))}] rbs={m.m_ragdollRigidBodies.Count}");
            foreach (var m in file.All<hkbGetUpModifier>())
                sb.AppendLine($"  GetUp '{m.m_name}' root={B(m.m_rootBoneIndex)} other={B(m.m_otherBoneIndex)} another={B(m.m_anotherBoneIndex)} normal={m.m_groundNormal}");
            foreach (var m in file.All<hkbPoseMatchingGenerator>())
                sb.AppendLine($"  PoseMatching '{m.m_name}' root={B(m.m_rootBoneIndex)} other={B(m.m_otherBoneIndex)} another={B(m.m_anotherBoneIndex)} pelvis={B(m.m_pelvisIndex)} mode={m.m_mode}");
            foreach (var m in file.All<BSDirectAtModifier>())
                sb.AppendLine($"  DirectAt '{m.m_name}' source={B(m.m_sourceBoneIndex)} start={B(m.m_startBoneIndex)} end={B(m.m_endBoneIndex)}");
            foreach (var m in file.All<BSLimbIKModifier>())
                sb.AppendLine($"  LimbIK '{m.m_name}' start={B(m.m_startBoneIndex)} end={B(m.m_endBoneIndex)}");
            foreach (var m in file.All<BSComputeAddBoneAnimModifier>())
                sb.AppendLine($"  ComputeAddBoneAnim '{m.m_name}' bone={B(m.m_boneIndex)}");
            foreach (var m in file.All<hkbLookAtModifier>())
                sb.AppendLine($"  hkbLookAt '{m.m_name}' head={B(m.m_headIndex)} neck={B(m.m_neckIndex)}");
            foreach (var m in file.All<hkbFootIkModifier>())
                sb.AppendLine($"  FootIkModifier '{m.m_name}' legs=[{string.Join("; ", m.m_legs.Select(l => $"{B(l.m_hipIndex)},{B(l.m_kneeIndex)},{B(l.m_ankleIndex)}"))}]");
            foreach (var m in file.All<hkbFootIkControlsModifier>())
                sb.AppendLine($"  FootIkControls '{m.m_name}' legs={m.m_legs.Count} gains=(fwdError={m.m_controlData.m_gains.m_footPlantedGain},...)");
            foreach (var g in file.All<BSBoneSwitchGenerator>())
                sb.AppendLine($"  BoneSwitch '{g.m_name}' children={g.m_ChildrenA.Count} weights=[{string.Join(",", g.m_ChildrenA.Select(c => c.m_spBoneWeight?.m_boneWeights.Count))}]");
            var types = file.Objects.GroupBy(o => o.GetType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine($"  types: {string.Join(", ", types)}");
        }
        foreach (var anim in new[] { "WalkForward.hkx", "MT_Idle.hkx", "Attack1.hkx" })
        {
            string animPath = Directory.GetFiles(folder, "*.hkx", SearchOption.AllDirectories).First(f => Path.GetFileName(f).Equals(anim, StringComparison.OrdinalIgnoreCase));
            var (binding, tracks) = UncompressedAnimation.Shape(animPath);
            var root = (hkRootLevelContainer)Util.ReadHKX(animPath);
            var c = root.m_namedVariants.Select(v => v.m_variant).OfType<hkaAnimationContainer>().First();
            var a = c.m_animations[0];
            sb.AppendLine($"ANIM {anim}: binding={binding.Count} tracks={tracks} type={a.GetType().Name} duration={a.m_duration} floatTracks={a.m_numberOfFloatTracks} annotations={a.m_annotationTracks.Count} bindings={c.m_bindings.Count} skeletonName='{c.m_bindings[0].m_originalSkeletonName}' blendHint={c.m_bindings[0].m_blendHint} floatBinding={c.m_bindings[0].m_floatTrackToFloatSlotIndices.Count} skeletons={c.m_skeletons.Count} extracted={a.m_extractedMotion?.GetType().Name} annTracks=[{string.Join(" | ", a.m_annotationTracks.Take(3).Select(t => $"'{t.m_trackName}' n={t.m_annotations.Count}"))}] nonEmptyAnn={a.m_annotationTracks.Count(t => t.m_annotations.Count > 0)}");
        }
        File.WriteAllText(Path.Combine(outDir, "sabrecat_bonebound.txt"), sb.ToString());
    }
}
