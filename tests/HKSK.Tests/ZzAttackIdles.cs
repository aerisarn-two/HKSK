using HKSK.Cache;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// Do any idle records carry an attack event, and does anything on them follow the flag?
public sealed class ZzAttackIdles
{
    [MastersFact]
    public void Look()
    {
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in shipped.Projects)
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
                flag[a.EventName] = Math.Max(flag.GetValueOrDefault(a.EventName), a.MovingAttack);

        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, m);
            if (!File.Exists(path)) continue;
            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var i in mod.IdleAnimations) idles[i.FormKey] = i;
        }

        var L = new List<string>();
        foreach (var i in idles.Values.OrderBy(i => i.AnimationEvent))
            if (!string.IsNullOrEmpty(i.AnimationEvent) && flag.TryGetValue(i.AnimationEvent, out int f))
            {
                var conds = i.Conditions.Select(c =>
                {
                    string fn = c.Data.GetType().Name.Replace("ConditionData", "").Replace("BinaryOverlay", "");
                    string cmp = c switch
                    {
                        IConditionFloatGetter cf => $"{c.CompareOperator} {cf.ComparisonValue:0.##}",
                        _ => c.CompareOperator.ToString(),
                    };
                    return $"{fn} {cmp}";
                });
                L.Add($"[{f}] {i.AnimationEvent,-34} {i.EditorID,-38} {string.Join("; ", conds)}");
            }

        L.Insert(0, $"{L.Count} idle records carry an attack event");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/attackidles.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
