using System.Reflection;
using HKSK.Cache;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// Every field of every race attack (ATKD + ATKE), for every race whose graph uses a project,
// held against the moving-attack flag of that project's event. If the Creation Kit wrote any
// of these from the flag, one of them follows it exactly.
public sealed class ZzAtkdFields
{
    [MastersFact]
    public void Look()
    {
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in shipped.Projects)
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
                flags[$"{p.Stem}/{a.EventName}"] = Math.Max(flags.GetValueOrDefault($"{p.Stem}/{a.EventName}"), a.MovingAttack);

        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, m);
            if (File.Exists(path)) mods.Add(SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE));
        }
        var link = mods.ToImmutableLinkCache();

        // later masters win
        var races = new Dictionary<FormKey, IRaceGetter>();
        foreach (var m in mods) foreach (var r in m.Races) races[r.FormKey] = r;

        var props = typeof(IAttackDataGetter).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0).ToList();

        // one row per race attack whose project and event we know the flag of
        var rows = new List<(bool Flag, string Key, string Race, Dictionary<string, string> Values)>();
        foreach (var race in races.Values)
            foreach (string? graph in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path }.Distinct())
            {
                if (string.IsNullOrEmpty(graph)) continue;
                string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));
                foreach (var atk in race.Attacks)
                {
                    string key = $"{stem}/{atk.AttackEvent}";
                    if (!flags.TryGetValue(key, out int f) || atk.AttackData is not { } d) continue;

                    var values = new Dictionary<string, string>();
                    foreach (var p in props)
                    {
                        object? v = p.GetValue(d);
                        values[p.Name] = v switch
                        {
                            IFormLinkGetter l => l.IsNull ? "null" : (l.TryResolve<ISkyrimMajorRecordGetter>(link, out var rec) ? rec.EditorID ?? l.FormKey.ToString() : l.FormKey.ToString()),
                            float x => x.ToString("0.###"),
                            null => "null",
                            _ => v.ToString() ?? "",
                        };
                    }
                    rows.Add((f != 0, key, race.EditorID ?? "", values));
                }
            }

        var L = new List<string> { $"{rows.Count} race attacks over {rows.Select(r => r.Key).Distinct().Count()} (project, event) pairs; {rows.Count(r => r.Flag)} flagged", "" };

        // for each field: does any value set separate the flag? score the best single value
        foreach (var p in props)
        {
            var byValue = rows.GroupBy(r => r.Values[p.Name])
                .Select(g => (Value: g.Key, On: g.Count(r => r.Flag), Off: g.Count(r => !r.Flag)))
                .OrderByDescending(x => x.On).ToList();
            bool pure = byValue.All(x => x.On == 0 || x.Off == 0);
            L.Add($"== {p.Name}  ({byValue.Count} values){(pure ? "   <<< EVERY VALUE IS PURELY FLAGGED OR CLEAR" : "")}");
            foreach (var x in byValue.Where(x => x.On > 0).Take(6))
                L.Add($"      {x.Value,-40} flagged {x.On,3}  clear {x.Off,4}");
        }

        // one row per (project, event): the values the races agree on, or "mixed"
        L.Add("");
        L.Add("==== per (project, event), values agreed by every race using it ====");
        var keyed = rows.GroupBy(r => r.Key).Select(g => (Key: g.Key, Flag: g.First().Flag,
            Values: props.ToDictionary(p => p.Name, p => g.Select(r => r.Values[p.Name]).Distinct().Count() == 1 ? g.First().Values[p.Name] : "mixed"))).ToList();
        L.Add($"{keyed.Count} pairs, {keyed.Count(k => k.Flag)} flagged");
        foreach (var p in props)
        {
            var byValue = keyed.GroupBy(k => k.Values[p.Name])
                .Select(g => (Value: g.Key, On: g.Count(k => k.Flag), Off: g.Count(k => !k.Flag)))
                .OrderByDescending(x => x.On).ToList();
            L.Add($"== {p.Name}");
            foreach (var x in byValue.Where(x => x.On > 0).Take(5))
                L.Add($"      {x.Value,-40} flagged {x.On,3}  clear {x.Off,4}");
        }
        L.Add("");
        L.Add("==== identical ATKD rows carrying different flags ====");
        var groups = keyed.Where(k => k.Values.Values.All(v => v != "mixed"))
            .GroupBy(k => string.Join("|", props.Where(p => p.Name != "Spell").Select(p => k.Values[p.Name])))
            .Where(g => g.Any(k => k.Flag) && g.Any(k => !k.Flag)).ToList();
        L.Add($"{groups.Count} ATKD rows (every field but the spell) shared by a flagged and a clear attack");
        foreach (var g in groups.Take(8))
        {
            L.Add($"   {g.Key}");
            foreach (var k in g.Take(6)) L.Add($"      [{(k.Flag ? 1 : 0)}] {k.Key}");
        }

        L.Add("");
        L.Add("Chance per flagged pair:");
        foreach (var k in keyed.Where(k => k.Flag).OrderBy(k => k.Key)) L.Add($"      {k.Values["Chance"],-6} {k.Key}");
        L.Add("clear pairs with Chance 0:");
        foreach (var k in keyed.Where(k => !k.Flag && k.Values["Chance"] == "0").OrderBy(k => k.Key)) L.Add($"      {k.Key}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/atkdfields.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
