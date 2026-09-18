using HKSK.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzRaceAttacks
{
    [MastersFact]
    public void Look()
    {
        var sets = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var byProject = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var racesOf = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var races = new Dictionary<Mutagen.Bethesda.Plugins.FormKey, IRaceGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, m), SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var r in mod.Races) races[r.FormKey] = r;
        }
        foreach (var r in races.Values)
            foreach (var g in new[] { r.BehaviorGraph.Male?.File.DataRelativePath.Path, r.BehaviorGraph.Female?.File.DataRelativePath.Path })
            {
                if (string.IsNullOrEmpty(g)) continue;
                string stem = Path.GetFileNameWithoutExtension(g.Replace('\\', '/'));
                if (!byProject.TryGetValue(stem, out var ev)) { byProject[stem] = ev = new(StringComparer.OrdinalIgnoreCase); racesOf[stem] = new(); }
                racesOf[stem].Add(r.EditorID ?? "");
                foreach (var a in r.Attacks) if (a.AttackEvent is { Length: > 0 } e) ev.Add(e);
            }
        var L = new List<string>();
        int both = 0, onlyShipped = 0, onlyRace = 0;
        foreach (var p in sets.Projects)
        {
            var shipped = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Select(a => a.EventName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var race = byProject.GetValueOrDefault(p.Stem) ?? new(StringComparer.OrdinalIgnoreCase);
            both += shipped.Intersect(race, StringComparer.OrdinalIgnoreCase).Count();
            var os = shipped.Except(race, StringComparer.OrdinalIgnoreCase).ToList();
            var or = race.Except(shipped, StringComparer.OrdinalIgnoreCase).ToList();
            onlyShipped += os.Count; onlyRace += or.Count;
            L.Add($"{p.Stem,-32} shipped {shipped.Count,3} race {race.Count,3} both {shipped.Intersect(race, StringComparer.OrdinalIgnoreCase).Count(),3} | only shipped: {string.Join(",", os)} | only race: {string.Join(",", or)} | races: {string.Join(",", racesOf.GetValueOrDefault(p.Stem) ?? [])}");
        }
        L.Insert(0, $"distinct events per project: both {both}, only in the set data {onlyShipped}, only in race attack data {onlyRace}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/raceattacks.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
