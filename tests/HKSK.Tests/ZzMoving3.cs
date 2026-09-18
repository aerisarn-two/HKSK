using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMoving3
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var table = new SortedDictionary<string, (int On, int Off, List<string> ExOn, List<string> ExOff)>();
        foreach (var p in shipped.Projects)
        {
            var file = cache.FindProjectFile(p.Stem);
            if (file is null) continue;
            var walk = ProjectWalk.Of(file);
            var clipSteps = walk.Steps.Where(s => s.Node is hkbClipGenerator).GroupBy(s => ((hkbClipGenerator)s.Node).m_name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            string Features(ProjectStep clip)
            {
                bool noMotion = false, partial = false, boneSwitch = false;
                foreach (var a in walk.Ancestors(clip.Node))
                {
                    if (a.Node is hkbBlenderGeneratorChild c)
                    {
                        if (c.m_worldFromModelWeight == 0f) noMotion = true;
                        if (c.m_boneWeights is { } bw && bw.m_boneWeights.Count > 0 && bw.m_boneWeights.Any(w => w < 1f)) partial = true;
                    }
                    if (a.Node is BSBoneSwitchGeneratorBoneData or BSBoneSwitchGenerator) boneSwitch = true;
                }
                return $"{(noMotion ? "motion-free child" : "-")} {(partial ? "partial bones" : "-")} {(boneSwitch ? "bone switch" : "-")}";
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    if (!seen.Add(a.EventName + "|" + a.Mirrored)) continue;
                    var steps = a.Clips.SelectMany(c => clipSteps.GetValueOrDefault(c) ?? []).ToList();
                    if (steps.Count == 0) continue;
                    var feats = steps.Select(Features).Distinct().ToList();
                    string key = feats.Count == 1 ? feats[0] : "mixed: " + string.Join(" / ", feats);
                    var row = table.GetValueOrDefault(key, (On: 0, Off: 0, ExOn: new List<string>(), ExOff: new List<string>()));
                    if (a.Mirrored != 0) { row.On++; if (row.ExOn.Count < 10) row.ExOn.Add($"{p.Stem}:{a.EventName}"); }
                    else { row.Off++; if (row.ExOff.Count < 10) row.ExOff.Add($"{p.Stem}:{a.EventName}"); }
                    table[key] = row;
                }
        }
        var L = new List<string> { "flag 1 / flag 0 (distinct attack events per project)" };
        foreach (var (k, v) in table) { L.Add($"  {k,-60} {v.On,4} / {v.Off,4}"); L.Add($"      on:  {string.Join(" ", v.ExOn)}"); L.Add($"      off: {string.Join(" ", v.ExOff)}"); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/moving3.txt", string.Join("\n", L));
    }
}
