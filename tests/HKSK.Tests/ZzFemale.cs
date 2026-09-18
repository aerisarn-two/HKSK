using HKSK.Model;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzFemale
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();
        foreach (var name in new[] { "DefaultMale", "DefaultFemale" })
        {
            var ch = cache.OpenActor(name)!.Character!;
            var s = (HKX2.hkbCharacterStringData)typeof(HKSK.Havok.CharacterFile).GetProperty("Strings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ch)!;
            L.Add($"{name}: names {s.m_animationNames.Count} deformable {s.m_deformableSkinNames.Count} rigid {s.m_rigidSkinNames.Count}");
            for (int i = 0; i < s.m_animationNames.Count; i++)
                if (s.m_animationNames[i].Contains("Chair_IdleBaseVar1", StringComparison.OrdinalIgnoreCase))
                    L.Add($"  [{i}] {s.m_animationNames[i]} ");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/female.txt", string.Join("\n", L));
    }
}
