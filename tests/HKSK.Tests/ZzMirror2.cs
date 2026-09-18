using HKSK.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzMirror2
{
    [MastersFact]
    public void Look()
    {
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flags = new Dictionary<(string, string), HashSet<string>>();
        var mods = new List<ISkyrimModDisposableGetter>();
        var races = new Dictionary<Mutagen.Bethesda.Plugins.FormKey, IRaceGetter>();
        foreach (string m in Masters.Order) { var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, m), SkyrimRelease.SkyrimSE); mods.Add(mod); foreach (var r in mod.Races) races[r.FormKey] = r; }
        foreach (var r in races.Values)
            foreach (var g in new[] { r.BehaviorGraph.Male?.File.DataRelativePath.Path, r.BehaviorGraph.Female?.File.DataRelativePath.Path })
            {
                if (string.IsNullOrEmpty(g)) continue;
                string stem = Path.GetFileNameWithoutExtension(g.Replace('\\', '/')).ToLowerInvariant();
                foreach (var a in r.Attacks)
                {
                    if (a.AttackEvent is not { Length: > 0 } e || a.AttackData is null) continue;
                    var key = (stem, e.ToLowerInvariant());
                    if (!flags.TryGetValue(key, out var set)) flags[key] = set = [];
                    set.Add(a.AttackData.Flags.ToString());
                }
            }
        var table = new Dictionary<string, int>();
        var L = new List<string>();
        foreach (var p in shipped.Projects)
            foreach (var set in p.Sets.Sets)
                foreach (var a in set.Attacks.Attacks)
                {
                    var f = flags.GetValueOrDefault((p.Stem.ToLowerInvariant(), a.EventName.ToLowerInvariant()));
                    string k = $"set-data flag {a.Mirrored} / race flags {(f is null ? "none" : string.Join(" | ", f.OrderBy(x => x)))}";
                    table[k] = table.GetValueOrDefault(k) + 1;
                }
        foreach (var kv in table.OrderBy(k => k.Key)) L.Add($"{kv.Value,5}  {kv.Key}");
        L.Add("== flag 1 attacks by project");
        foreach (var p in shipped.Projects)
        {
            var on = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Where(a => a.Mirrored != 0).Select(a => a.EventName).Distinct().ToList();
            var off = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Where(a => a.Mirrored == 0).Select(a => a.EventName).Distinct().ToList();
            if (on.Count > 0) L.Add($"  {p.Stem}: on {string.Join(",", on)} | off {string.Join(",", off.Take(10))}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/mirror2.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
