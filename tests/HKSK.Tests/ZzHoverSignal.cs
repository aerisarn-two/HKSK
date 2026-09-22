using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// A hovering creature: no blender parametric on speed, and the clips under its direction
// blends -- its locomotion -- carry no root motion. Flyers fail the second test.
public sealed class ZzHoverSignal
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string> { $"{"project",-30} {"speedBlends",11} {"dirBlends",9} {"dirClips",8} {"dirMaxTravel",12} {"paramBlends",11}  flagged/attacks" };
        foreach (var p in shipped.Projects.OrderBy(x => x.Stem))
        {
            string? file = cache.FindProjectFile(p.Stem); var actor = cache.OpenActor(p.Stem);
            if (file is null || actor is null) continue;
            var walk = ProjectWalk.Of(file);
            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps) if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
            var travelOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);
            int speed = 0, dir = 0, param = 0, dirClips = 0; float dirMax = 0;
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbBlenderGenerator bl) continue;
                var names = vars.GetValueOrDefault(s.File) ?? [];
                string? bound = (bl.m_variableBindingSet?.m_bindings ?? []).Where(b => b.m_memberPath == "blendParameter" && b.m_variableIndex >= 0 && b.m_variableIndex < names.Length).Select(b => names[b.m_variableIndex]).FirstOrDefault();
                bool parametric = (bl.m_flags & 0x1) != 0 || bound is not null;
                if (!parametric) continue;
                param++;
                if (bound?.Contains("speed", StringComparison.OrdinalIgnoreCase) == true) speed++;
                if (bound?.Contains("direction", StringComparison.OrdinalIgnoreCase) == true || bound is null)
                {
                    dir++;
                    foreach (var c in bl.m_children)
                    {
                        // clips anywhere under the arm
                        var st = new Stack<IHavokObject>(); if (c?.m_generator is not null) st.Push(c.m_generator);
                        var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                        while (st.Count > 0)
                        {
                            var n = st.Pop(); if (!seen.Add(n)) continue;
                            if (n is hkbClipGenerator clip) { dirClips++; dirMax = Math.Max(dirMax, travelOf.GetValueOrDefault(clip.m_name)); }
                            foreach (var (_, _, k) in HKSK.Havok.HavokEdges.Of(n)) st.Push(k);
                        }
                    }
                }
            }
            int attacks = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Select(a => a.EventName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int flagged = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Where(a => a.MovingAttack != 0).Select(a => a.EventName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (speed == 0 || dirMax < 50) L.Add($"{p.Stem,-30} {speed,11} {dir,9} {dirClips,8} {dirMax,12:0} {param,11}  {flagged}/{attacks}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/hoversignal.txt", string.Join("\n", L));
    }
}
