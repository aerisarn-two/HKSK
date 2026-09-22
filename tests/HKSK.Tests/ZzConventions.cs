using System.Numerics;
using System.Text;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// The values vanilla puts in the fields a graph editor has to fill, and the
// geometry behind the sabre cat's foot IK and look-at, checked against its rig.
public sealed class ZzConventions
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        if (corpus.Length == 0) return;
        string folder = Path.Combine(corpus, "actors", "sabrecat");
        var sb = new StringBuilder();
        string Find(string name) => Directory.GetFiles(folder, "*.hkx", SearchOption.AllDirectories).First(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        foreach (string name in new[] { "quadrupedbehavior.hkx", "sabrecatbehavior.hkx", "noncombatidle.hkx" })
        {
            var file = HavokFile.Load(Find(name));
            var data = file.First<hkbBehaviorGraphData>()!;
            var strings = data.m_stringData!;
            sb.AppendLine($"## {name}");
            sb.AppendLine($"  graph variableMode={file.First<hkbBehaviorGraph>()!.m_variableMode} attributeDefaults={data.m_attributeDefaults.Count} wordMin={data.m_wordMinVariableValues.Count} wordMax={data.m_wordMaxVariableValues.Count} eventInfos={data.m_eventInfos.Count} eventFlags=[{string.Join(",", data.m_eventInfos.Select(e => e.m_flags).Distinct())}]");
            for (int i = 0; i < Math.Min(6, data.m_variableInfos.Count); i++)
                sb.AppendLine($"  var[{i}] {strings.m_variableNames[i]} type={data.m_variableInfos[i].m_type} role=({data.m_variableInfos[i].m_role.m_role},{data.m_variableInfos[i].m_role.m_flags}) init={data.m_variableInitialValues?.m_wordVariableValues.ElementAtOrDefault(i)?.m_value} min={data.m_wordMinVariableValues.ElementAtOrDefault(i)?.m_value} max={data.m_wordMaxVariableValues.ElementAtOrDefault(i)?.m_value}");
            sb.AppendLine($"  variable types: {string.Join(",", data.m_variableInfos.GroupBy(v => v.m_type).Select(g => $"{g.Key}x{g.Count()}"))} roles: {string.Join(",", data.m_variableInfos.GroupBy(v => (v.m_role.m_role, v.m_role.m_flags)).Select(g => $"{g.Key}x{g.Count()}"))}");
            sb.AppendLine($"  characterPropertyInfos={data.m_characterPropertyInfos.Count} names={string.Join(",", strings.m_characterPropertyNames.Take(5))}");
            foreach (var sm in file.All<hkbStateMachine>().Take(40))
                sb.AppendLine($"  SM '{sm.m_name}' start={sm.m_startStateId} mode={sm.m_startStateMode} self={sm.m_selfTransitionMode} maxSim={sm.m_maxSimultaneousTransitions} wrap={sm.m_wrapAroundStateId} sync={sm.m_syncVariableIndex} rtp={sm.m_returnToPreviousStateEventId} rnd={sm.m_randomTransitionEventId} higher={sm.m_transitionToNextHigherStateEventId} lower={sm.m_transitionToNextLowerStateEventId} changeEvent={sm.m_eventToSendWhenStateOrTransitionChanges.m_id} chooser={sm.m_startStateChooser?.GetType().Name} userData={sm.m_userData} states=[{string.Join(",", sm.m_states.Select(s => $"{s.m_stateId}:{s.m_name}:p{s.m_probability}:e{s.m_enable}:enter{s.m_enterNotifyEvents?.m_events.Count}:exit{s.m_exitNotifyEvents?.m_events.Count}:listeners{s.m_listeners.Count}"))}] wild={sm.m_wildcardTransitions?.m_transitions.Count}");
            var seen = new HashSet<string>();
            foreach (var arr in file.All<hkbStateMachineTransitionInfoArray>())
                foreach (var t in arr.m_transitions)
                {
                    string key = $"flags=0x{t.m_flags:x} prio={t.m_priority} fromNested={t.m_fromNestedStateId} toNested={t.m_toNestedStateId} trig=({t.m_triggerInterval.m_enterEventId},{t.m_triggerInterval.m_exitEventId},{t.m_triggerInterval.m_enterTime},{t.m_triggerInterval.m_exitTime}) init=({t.m_initiateInterval.m_enterEventId},{t.m_initiateInterval.m_exitEventId},{t.m_initiateInterval.m_enterTime},{t.m_initiateInterval.m_exitTime}) effect={(t.m_transition is hkbBlendingTransitionEffect b ? $"{b.m_name}:d{b.m_duration}:f0x{b.m_flags:x}:end{b.m_endMode}:curve{b.m_blendCurve}:self{b.m_selfTransitionMode}:ev{b.m_eventMode}:start{b.m_toGeneratorStartTimeFraction}" : t.m_transition?.GetType().Name ?? "null")} cond={(t.m_condition as hkbExpressionCondition)?.m_expression}";
                    if (seen.Add(key)) sb.AppendLine($"  transition {key}");
                }
            var clipKinds = file.All<hkbClipGenerator>().GroupBy(c => $"mode={c.m_mode} flags={c.m_flags} binding={c.m_animationBindingIndex} speed={c.m_playbackSpeed} enforced={c.m_enforcedDuration} userFrac={c.m_userControlledTimeFraction} start={c.m_startTime} crop=({c.m_cropStartAmountLocalTime},{c.m_cropEndAmountLocalTime}) userData={c.m_userData} triggers={c.m_triggers?.m_triggers.Count}").Select(g => $"{g.Key} x{g.Count()}");
            sb.AppendLine($"  clips: {string.Join(" | ", clipKinds)}");
            foreach (var c in file.All<hkbClipGenerator>().Where(c => c.m_triggers is { m_triggers.Count: > 0 }).Take(4))
                sb.AppendLine($"  clip '{c.m_name}' triggers=[{string.Join("; ", c.m_triggers!.m_triggers.Select(t => $"{strings.m_eventNames.ElementAtOrDefault(t.m_event.m_id)}@{t.m_localTime}{(t.m_relativeToEndOfClip ? "fromEnd" : "")} acyclic={t.m_acyclic} ann={t.m_isAnnotation} payload={t.m_event.m_payload?.GetType().Name}"))}]");
            foreach (var b in file.All<hkbBlenderGenerator>().Take(3))
                sb.AppendLine($"  blend '{b.m_name}' flags=0x{b.m_flags:x} threshold={b.m_referencePoseWeightThreshold} param={b.m_blendParameter} min={b.m_minCyclicBlendParameter} max={b.m_maxCyclicBlendParameter} syncMaster={b.m_indexOfSyncMasterChild} subtract={b.m_subtractLastChild} children=[{string.Join(",", b.m_children.Select(ch => $"w{ch.m_weight}:wfm{ch.m_worldFromModelWeight}:bw{ch.m_boneWeights?.m_boneWeights.Count}:bind{ch.m_variableBindingSet?.m_bindings.Count}"))}] bindings=[{string.Join(",", (b.m_variableBindingSet?.m_bindings ?? []).Select(x => $"{x.m_memberPath}<-{strings.m_variableNames.ElementAtOrDefault(x.m_variableIndex)}:t{x.m_bindingType}:bit{x.m_bitIndex}"))}] indexToEnable={b.m_variableBindingSet?.m_indexOfBindingToEnable}");
            foreach (var m in file.All<hkbEvaluateExpressionModifier>().Take(2))
                sb.AppendLine($"  eem '{m.m_name}' enable={m.m_enable} userData={m.m_userData} exprs=[{string.Join("; ", m.m_expressions!.m_expressionsData.Select(e => $"{e.m_expression} var={e.m_assignmentVariableIndex} ev={e.m_assignmentEventIndex} mode={e.m_eventMode}"))}]");
            foreach (var m in file.All<BSEventEveryNEventsModifier>().Take(2))
                sb.AppendLine($"  everyN '{m.m_name}' check={strings.m_eventNames.ElementAtOrDefault(m.m_eventToCheckFor.m_id)} send={strings.m_eventNames.ElementAtOrDefault(m.m_eventToSend.m_id)} n={m.m_numberOfEventsBeforeSend} min={m.m_minimumNumberOfEventsBeforeSend} rnd={m.m_randomizeNumberOfEvents}");
            foreach (var m in file.All<hkbModifierGenerator>().Take(2))
                sb.AppendLine($"  modgen '{m.m_name}' userData={m.m_userData} modifier={m.m_modifier?.GetType().Name}");
            foreach (var m in file.All<BSIsActiveModifier>().Take(2))
                sb.AppendLine($"  isActive '{m.m_name}' enable={m.m_enable} a0={m.m_bIsActive0} inv0={m.m_bInvertActive0} bindings=[{string.Join(",", (m.m_variableBindingSet?.m_bindings ?? []).Select(x => $"{x.m_memberPath}<-{strings.m_variableNames.ElementAtOrDefault(x.m_variableIndex)}:t{x.m_bindingType}"))}]");
            foreach (var s in file.All<hkbStateMachineStateInfo>().Where(s => s.m_enterNotifyEvents is { m_events.Count: > 0 } || s.m_exitNotifyEvents is { m_events.Count: > 0 }).Take(2))
                sb.AppendLine($"  state '{s.m_name}' enter=[{string.Join(",", (s.m_enterNotifyEvents?.m_events ?? []).Select(e => $"{strings.m_eventNames.ElementAtOrDefault(e.m_id)}:{e.m_payload?.GetType().Name}"))}] exit=[{string.Join(",", (s.m_exitNotifyEvents?.m_events ?? []).Select(e => strings.m_eventNames.ElementAtOrDefault(e.m_id)))}]");
            var syncClip = file.All<BSSynchronizedClipGenerator>().FirstOrDefault();
            if (syncClip != null) sb.AppendLine($"  sync '{syncClip.m_name}' role={syncClip.m_SyncAnimPrefix} ");
        }

        // ---- geometry: foot IK legs and look-at axes against the rig
        var skeleton = HavokFile.Load(Find("skeleton.hkx"));
        var rig = skeleton.All<hkaAnimationContainer>().First().m_skeletons[0];
        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < world.Length; i++)
        {
            var p = rig.m_referencePose[i];
            var local = Matrix4x4.CreateScale(p.M31, p.M32, p.M33) * Matrix4x4.CreateFromQuaternion(new Quaternion(p.M21, p.M22, p.M23, p.M24)) * Matrix4x4.CreateTranslation(p.M11, p.M12, p.M13);
            int parent = rig.m_parentIndices[i];
            world[i] = parent >= 0 ? local * world[parent] : local;
        }
        var character = HavokFile.Load(Find("sabrecat.hkx")).First<hkbCharacterData>();
        var charFile = Directory.GetFiles(folder, "sabrecat.hkx", SearchOption.AllDirectories).First(f => f.Contains("characters", StringComparison.OrdinalIgnoreCase));
        character = HavokFile.Load(charFile).First<hkbCharacterData>()!;
        sb.AppendLine("## geometry");
        foreach (var leg in character.m_footIkDriverInfo!.m_legs)
        {
            Vector3 hip = world[leg.m_hipIndex].Translation, knee = world[leg.m_kneeIndex].Translation, ankle = world[leg.m_ankleIndex].Translation;
            Vector3 n = Vector3.Normalize(Vector3.Cross(knee - hip, ankle - knee));
            Matrix4x4.Invert(world[leg.m_kneeIndex], out var inv);
            Vector3 nLocal = Vector3.TransformNormal(n, inv);
            sb.AppendLine($"  leg {rig.m_bones[leg.m_hipIndex].m_name}: hipZ={hip.Z:F1} kneeZ={knee.Z:F1} ankleZ={ankle.Z:F1} planted={leg.m_footPlantedAnkleHeightMS} raised={leg.m_footRaisedAnkleHeightMS} max={leg.m_maxAnkleHeightMS} derivedKneeAxis=({nLocal.X:F2},{nLocal.Y:F2},{nLocal.Z:F2}) stored={leg.m_kneeAxisLS} bend={MathF.Acos(Vector3.Dot(Vector3.Normalize(knee - hip), Vector3.Normalize(ankle - knee))) * 180 / MathF.PI:F1}deg");
        }
        var quad = HavokFile.Load(Find("quadrupedbehavior.hkx"));
        foreach (var la in quad.All<BSLookAtModifier>().Where(m => m.m_name.StartsWith("SabreCat")))
            foreach (var b in la.m_bones)
            {
                Matrix4x4.Invert(world[b.m_index], out var inv);
                Vector3 fwd = Vector3.TransformNormal(new Vector3(0, 1, 0), inv);
                Vector3 up = Vector3.TransformNormal(new Vector3(0, 0, 1), inv);
                sb.AppendLine($"  lookat {rig.m_bones[b.m_index].m_name}: derivedFwd=({fwd.X:F3},{fwd.Y:F3},{fwd.Z:F3}) derivedUp=({up.X:F3},{up.Y:F3},{up.Z:F3}) stored={b.m_fwdAxisLS}");
            }
        File.WriteAllText(Path.Combine(outDir, "sabrecat_conventions.txt"), sb.ToString());
    }
}
