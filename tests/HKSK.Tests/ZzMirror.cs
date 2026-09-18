using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMirror
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var table = new Dictionary<string, int>();
        var ex = new Dictionary<string, List<string>>();
        foreach (var p in shipped.Projects)
        {
            var file = cache.FindProjectFile(p.Stem);
            if (file is null) continue;
            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in ProjectWalk.Of(file).Steps)
                if (s.Node is hkbClipGenerator c)
                {
                    if (!byName.TryGetValue(c.m_name, out var l)) byName[c.m_name] = l = [];
                    l.Add(c);
                }
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                    string bits = clips.Count == 0 ? "no clip found"
                        : clips.All(c => (c.m_flags & 4) != 0) ? "all clips mirrored"
                        : clips.Any(c => (c.m_flags & 4) != 0) ? "some clips mirrored" : "no clip mirrored";
                    string key = $"flag {a.Mirrored} / {bits}";
                    table[key] = table.GetValueOrDefault(key) + 1;
                    if (!ex.TryGetValue(key, out var e)) ex[key] = e = [];
                    if (e.Count < 6) e.Add($"{p.Stem} {a.EventName} [{string.Join(",", a.Clips)}] flags {string.Join(",", clips.Select(c => c.m_flags))}");
                }
        }
        var L = new List<string>();
        foreach (var kv in table.OrderBy(k => k.Key)) { L.Add($"{kv.Value,5}  {kv.Key}"); L.AddRange(ex[kv.Key].Select(x => "         " + x)); }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/mirror.txt", string.Join("\n", L));
    }
}
