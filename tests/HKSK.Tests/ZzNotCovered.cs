using System.Text;
using HKSK.Behavior;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The modules docs/behavior-assembly.md §7 left out: the pose-matched get-up, the
// foot-IK driver, the look-at bones, the controller capsule -- what each needs of the
// skeleton, read per creature so a generator can fill them from bone names.
public sealed class ZzNotCovered
{
    private static readonly string Out =
        Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.Combine(Path.GetTempPath(), "census");

    [CorpusFact]
    public void Look()
    {
        Directory.CreateDirectory(Out);
        var cache = SkyrimCache.Load(Corpus.Root!);
        var sb = new StringBuilder();
        foreach (var actor in cache.Actors())
        {
            if (actor.ProjectFile is null || actor.Character is null) continue;
            var walk = ProjectWalk.Of(actor.ProjectFile.File.Path);
            if (walk.Steps.Count == 0) continue;
            string[] bones = [];
            try
            {
                if (actor.SkeletonPath is { } sp && File.Exists(sp))
                    bones = HavokFile.Load(sp).First<hkaSkeleton>()?.m_bones.Select(b => b.m_name).ToArray() ?? [];
            }
            catch { }
            string B(int i) => i < 0 ? "-" : i < bones.Length ? $"{i}:{bones[i]}" : $"{i}:?";
            sb.AppendLine($"######## {actor.Name}  bones={bones.Length}  scale={actor.Character.Data.m_scale}");
            var cd = actor.Character.Data;
            var cc = cd.m_characterControllerInfo;
            sb.AppendLine($"controller: capsuleHeight={cc.m_capsuleHeight} radius={cc.m_capsuleRadius} filter={cc.m_collisionFilterInfo:x} up={cd.m_modelUpMS} fwd={cd.m_modelForwardMS}");
            if (cd.m_footIkDriverInfo is { } fik)
            {
                sb.AppendLine($"footIk: legs={fik.m_legs.Count} rayUp={fik.m_raycastDistanceUp} rayDown={fik.m_raycastDistanceDown} vOff={fik.m_verticalOffset} groundH={fik.m_originalGroundHeightMS} lockPlanted={fik.m_lockFeetWhenPlanted} charUp={fik.m_useCharacterUpVector} quadNarrow={fik.m_isQuadrupedNarrow} fwdAlign={fik.m_forwardAlignFraction} sideAlign={fik.m_sidewaysAlignFraction}");
                foreach (var leg in fik.m_legs)
                    sb.AppendLine($"  leg hip={B(leg.m_hipIndex)} knee={B(leg.m_kneeIndex)} ankle={B(leg.m_ankleIndex)} kneeAxis={leg.m_kneeAxisLS} footEnd={leg.m_footEndLS} planted={leg.m_footPlantedAnkleHeightMS} raised={leg.m_footRaisedAnkleHeightMS} min={leg.m_minAnkleHeightMS} max={leg.m_maxAnkleHeightMS} knee[{leg.m_minKneeAngleDegrees},{leg.m_maxKneeAngleDegrees}] ankleMax={leg.m_maxAnkleAngleDegrees}");
            }
            else sb.AppendLine("footIk: none");
            sb.AppendLine($"handIk: {(cd.m_handIkDriverInfo is { } h ? h.m_hands.Count + " hands" : "none")}");

            foreach (var s in walk.Steps)
            {
                switch (s.Node)
                {
                    case hkbPoseMatchingGenerator pm:
                    {
                        var path = string.Join(" / ", walk.Ancestors(pm).Select(a => a.Node).OfType<hkbStateMachineStateInfo>().Select(x => x.m_name).Reverse());
                        var kids = pm.m_children.Select(c => (c.m_generator as hkbClipGenerator)?.m_animationName is { } an ? Path.GetFileName(an) : c.m_generator?.GetType().Name ?? "-");
                        sb.AppendLine($"posematch '{pm.m_name}' mode={pm.m_mode} flags={pm.m_flags} root={B(pm.m_rootBoneIndex)} other={B(pm.m_otherBoneIndex)} another={B(pm.m_anotherBoneIndex)} pelvis={B(pm.m_pelvisIndex)} blendSpeed={pm.m_blendSpeed} minSpeed={pm.m_minSpeedToSwitch} tNoErr={pm.m_minSwitchTimeNoError} tFull={pm.m_minSwitchTimeFullError} play={pm.m_startPlayingEventId} match={pm.m_startMatchingEventId} rot={pm.m_worldFromModelRotation} under [{path}] kids=[{string.Join(",", kids)}]");
                        break;
                    }
                    case BSLookAtModifier la:
                    {
                        var path = string.Join(" / ", walk.Ancestors(la).Select(a => a.Node).OfType<hkbStateMachineStateInfo>().Select(x => x.m_name).Reverse());
                        sb.AppendLine($"lookat '{la.m_name}' limit={la.m_limitAngleDegrees} thr={la.m_limitAngleThresholdDegrees} cont={la.m_continueLookOutsideOfLimit} on={la.m_onGain} off={la.m_offGain} boneGains={la.m_useBoneGains} cam={la.m_lookAtCamera} under [{path}] bones=[{string.Join(",", la.m_bones.Select(b => $"{B(b.m_index)}@{b.m_limitAngleDegrees}/{b.m_onGain}/{b.m_offGain}{(b.m_enabled ? "" : "!")}"))}] eyes=[{string.Join(",", la.m_eyeBones.Select(b => B(b.m_index)))}]");
                        break;
                    }
                    case hkbBlenderGeneratorChild bc when bc.m_boneWeights is not null || bc.m_variableBindingSet is not null:
                    {
                        var parent = walk.StepOf(bc)?.Parent as hkbBlenderGenerator;
                        var bw = bc.m_boneWeights;
                        var vars = new ProjectVariables(walk.Steps);
                        string binds = bc.m_variableBindingSet is { } set
                            ? string.Join(",", set.m_bindings.Select(b => $"{b.m_memberPath}<-{(b.m_bindingType == 1 ? "prop:" : "")}{vars.NameOf(s, b) ?? ("#" + b.m_variableIndex)}"))
                            : "";
                        sb.AppendLine($"boneweights blend='{parent?.m_name}' weight={bc.m_weight} inline={(bw is null ? "-" : bw.m_boneWeights.Count + "/" + bw.m_boneWeights.Count(w => w > 0f) + "nz")} bindings=[{binds}]");
                        break;
                    }
                    case hkbFootIkControlsModifier fc:
                    {
                        var path = string.Join(" / ", walk.Ancestors(fc).Select(a => a.Node).OfType<hkbStateMachineStateInfo>().Select(x => x.m_name).Reverse());
                        var g = fc.m_controlData.m_gains;
                        sb.AppendLine($"footIkControls '{fc.m_name}' legs={fc.m_legs.Count} under [{path}] onOff={g.m_onOffGain} ground↑={g.m_groundAscendingGain} ↓={g.m_groundDescendingGain} planted={g.m_footPlantedGain} raised={g.m_footRaisedGain} unlock={g.m_footUnlockGain} wfm={g.m_worldFromModelFeedbackGain} err={g.m_errorUpDownBias} align={g.m_alignWorldFromModelGain} hip={g.m_hipOrientationGain}");
                        break;
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(Out, "notcovered.txt"), sb.ToString());
    }
}
