using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// m_name (what the set data lists) against m_animationName (the file on disk).
public sealed class ZzClipNames
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();

        foreach (string stem in new[] { "WerewolfBeastProject", "AtronachStormProject", "NetchProject", "WitchlightProject", "DefaultMale" })
        {
            string? path = cache.FindProjectFile(stem);
            if (path is null) continue;

            L.Add($"======== {stem}");
            int differ = 0, total = 0;
            foreach (var s in ProjectWalk.Of(path).Steps)
            {
                if (s.Node is not hkbClipGenerator c) continue;
                total++;
                string stemOfAnim = Path.GetFileNameWithoutExtension(c.m_animationName.Replace('\\', '/'));
                if (!string.Equals(c.m_name, stemOfAnim, StringComparison.OrdinalIgnoreCase)) differ++;
            }

            L.Add($"   {total} clip generators, {differ} whose m_name is not the animation's stem");

            foreach (var s in ProjectWalk.Of(path).Steps)
            {
                if (s.Node is not hkbClipGenerator c) continue;
                if (!c.m_name.Contains("SprintAllFours", StringComparison.OrdinalIgnoreCase)
                    && !c.m_name.Contains("AttackLeftSide", StringComparison.OrdinalIgnoreCase)
                    && !c.m_name.Contains("RunForwardAttackLeftSyncPower", StringComparison.OrdinalIgnoreCase)
                    && c.m_name is not ("PowerAttack_F" or "PowerAttack" or "NetchAttackLeft" or "Attack1" or "1HM_AttackLeft")) continue;
                L.Add($"     m_name {c.m_name,-40} m_animationName {c.m_animationName}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/clipnames.txt", string.Join("\n", L));
    }
}
