using HKSK.Cache;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// Everything the masters say about the races of each project, so the six that use the
// moving-attack flag can be held against the other 43.
public sealed class ZzRaceDiff
{
    [MastersFact]
    public void Look()
    {
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flagged = shipped.Projects
            .Where(p => p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Any(a => a.MovingAttack != 0))
            .Select(p => p.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var races = new Dictionary<FormKey, IRaceGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string master in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, master);
            if (!File.Exists(path)) continue;
            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (IRaceGetter r in mod.Races) races[r.FormKey] = r;
        }

        // project stem -> races that use it
        var byProject = new Dictionary<string, List<IRaceGetter>>(StringComparer.OrdinalIgnoreCase);
        foreach (IRaceGetter race in races.Values)
            foreach (string? graph in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path })
            {
                if (string.IsNullOrEmpty(graph)) continue;
                string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));
                if (!byProject.TryGetValue(stem, out var list)) byProject[stem] = list = [];
                if (!list.Contains(race)) list.Add(race);
            }

        var L = new List<string>
        {
            $"{"",1} {"project",-28} {"races",5} {"atk",4} {"unarmedReach",14} {"unarmedDmg",11}  {"angles (attack/strike)",24}  attack-data flags",
        };

        foreach (var p in shipped.Projects.OrderByDescending(x => flagged.Contains(x.Stem)).ThenBy(x => x.Stem, StringComparer.OrdinalIgnoreCase))
        {
            var list = byProject.GetValueOrDefault(p.Stem) ?? [];
            if (list.Count == 0) { L.Add($"{(flagged.Contains(p.Stem) ? "F" : " ")} {p.Stem,-28} (no race points at it)"); continue; }

            var reach = list.Select(r => r.UnarmedReach).Distinct().Order().ToList();
            var dmg = list.Select(r => r.UnarmedDamage).Distinct().Order().ToList();
            var atk = list.SelectMany(r => r.Attacks).ToList();
            var angles = atk.Where(a => a.AttackData is not null)
                .Select(a => $"{a.AttackData!.AttackAngle:0.#}/{a.AttackData.StrikeAngle:0.#}").Distinct().Order().Take(4);
            var af = atk.Select(a => a.AttackData?.Flags.ToString() ?? "-").Distinct().Order();

            L.Add($"{(flagged.Contains(p.Stem) ? "F" : " ")} {p.Stem,-28} {list.Count,5} {atk.Count,4} " +
                  $"{string.Join(",", reach.Select(x => $"{x:0.#}")),14} {string.Join(",", dmg.Select(x => $"{x:0.#}")),11}  " +
                  $"{string.Join(" ", angles),24}  {string.Join(" | ", af)}");
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/racediff.txt", string.Join("\n", L));
        foreach (var mod in mods) mod.Dispose();
    }
}
