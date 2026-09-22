using HKSK.Cache;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// The race attack data behind each werewolf and player attack event: what the engine chooses by.
public sealed class ZzWerewolfAtkd
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
        var cache = mods.ToImmutableLinkCache();

        var L = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in mods.SelectMany(m => m.Races))
        {
            string? graph = race.BehaviorGraph.Male?.File.DataRelativePath.Path;
            if (graph is null) continue;
            string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));

            foreach (var atk in race.Attacks)
            {
                string key = $"{stem}/{atk.AttackEvent}";
                if (!seen.Add(key)) continue;
                var d = atk.AttackData;
                string type = d?.AttackType.TryResolve(cache)?.EditorID ?? "-";
                if (!flags.TryGetValue(key, out int f)) continue;
                L.Add($"{f}\t{stem}\t{atk.AttackEvent}\t{type}\t{d?.AttackAngle:0}\t{d?.Flags}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/atkd.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
