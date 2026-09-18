using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The blenders above the flagged attacks: are they parametric, what is the axis, and what
// drives the parameter?
public sealed class ZzBlendParam
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();
        string[] wanted =
        [
            "BackhandBlend", "HowlExplodeBlend", "LeftAttackForwardBlend", "RightAttackBlend",
            "LeftAttackRunningDirectionalBlend", "1HM_Forward_AttackLeft_Blend", "1HM_Right_AttackLeft_Blend",
            "2HW_Forward_AttackLeftNPC_Blend", "H2H_Forward_AttackLeft_Blend", "MLh_1HM_AttackState_Blend",
            "PlayerStaggerStandingBlend_MT", "L1_AttackBlend", "R1_AttackBlend",
        ];

        foreach (string stem in new[] { "DefaultMale", "WerewolfBeastProject", "AtronachStormProject" })
        {
            string? path = cache.FindProjectFile(stem);
            if (path is null) continue;
            var walk = ProjectWalk.Of(path);

            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];

            L.Add($"======== {stem}");
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbBlenderGenerator bl || !wanted.Contains(bl.m_name, StringComparer.OrdinalIgnoreCase)) continue;

                var names = vars.GetValueOrDefault(s.File) ?? [];
                string Bound(IHavokObject o, string member)
                {
                    var set = o switch
                    {
                        hkbNode n => n.m_variableBindingSet,
                        hkbBlenderGeneratorChild c => c.m_variableBindingSet,
                        _ => null,
                    };
                    foreach (var b in set?.m_bindings ?? [])
                        if (b.m_memberPath == member && b.m_variableIndex >= 0 && b.m_variableIndex < names.Length)
                            return names[b.m_variableIndex];
                    return "(constant)";
                }

                var arms = bl.m_children.Select((c, i) =>
                    $"#{i} w={c?.m_weight:0.##}{(c is null ? "" : Bound(c, "weight") is "(constant)" ? "" : " <-" + Bound(c, "weight"))}"
                    + $" [{(c?.m_generator as hkbNode)?.m_name ?? "?"}]");

                L.Add($"   {bl.m_name,-36} flags 0x{bl.m_flags:x}  param {bl.m_blendParameter:0.##} <- {Bound(bl, "blendParameter")}  " +
                      $"cyclic {bl.m_minCyclicBlendParameter:0.##}..{bl.m_maxCyclicBlendParameter:0.##}  sync {bl.m_indexOfSyncMasterChild}");
                foreach (string arm in arms) L.Add($"        {arm}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/blendparam.txt", string.Join("\n", L));
    }
}
