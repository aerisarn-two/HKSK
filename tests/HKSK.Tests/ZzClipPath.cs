using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzClipPath
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var actor = cache.OpenActor("DefaultMale")!;
        var walk = ProjectWalk.Of(cache.FindProjectFile("DefaultMale")!);
        var L = new List<string>();
        foreach (var s in walk.Steps)
            if (s.Node is hkbClipGenerator c && (c.m_animationName.Contains("chair_idlebasevar1", StringComparison.OrdinalIgnoreCase) || c.m_name.Contains("chair_idlebasevar1", StringComparison.OrdinalIgnoreCase)))
                L.Add($"clip {c.m_name} anim {c.m_animationName} file {s.FileName}");
        foreach (var a in actor.Character!.AnimationNames)
            if (a.Contains("chair_idlebasevar1", StringComparison.OrdinalIgnoreCase)) L.Add("character: " + a);
        L.Add("character list size " + actor.Character.AnimationNames.Count + ", clips with no '\\\\' " + walk.Steps.Count(s => s.Node is hkbClipGenerator c && !c.m_animationName.Contains('\\')));
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/clippath.txt", string.Join("\n", L));
    }
}
