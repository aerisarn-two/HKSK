using System.Reflection;
using HKSK.Cache;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// The graph variables an attack's idle chain tests with GetGraphVariableInt/Bool/Float.
public sealed class ZzGraphVarIdles
{
    [MastersFact]
    public void Look()
    {
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

        IEnumerable<IIdleAnimationGetter> Up(IIdleAnimationGetter i)
        {
            var seen = new HashSet<FormKey>();
            for (IIdleAnimationGetter? at = i; at is not null && seen.Add(at.FormKey);)
            {
                yield return at;
                at = at.RelatedIdles.Count > 0 && !at.RelatedIdles[0].IsNull && idles.TryGetValue(at.RelatedIdles[0].FormKey, out var n) ? n : null;
            }
        }

        static string Describe(IConditionGetter c)
        {
            var d = c.Data;
            string fn = d.GetType().Name.Replace("ConditionData", "").Replace("BinaryOverlay", "");
            // any string-valued property of the condition data is its variable name
            string arg = string.Join(",", d.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .Select(p => p.GetValue(d) as string).Where(v => !string.IsNullOrEmpty(v)));
            string value = c is IConditionFloatGetter f ? f.ComparisonValue.ToString("0.##") : "?";
            return $"{fn}({arg}) {c.CompareOperator} {value}";
        }

        string[] events = ["attackStart", "AttackStartLeft", "AttackStartRight", "attackStartLeftHand", "AttackStartH2HLeft", "AttackStartH2HRight",
            "AttackStartLeftSprinting", "AttackStartRightSprinting", "AttackStartDualSprinting", "AttackStartBackHand",
            "AttackStartLeftPower", "AttackStartLeftRunningPower", "AttackPowerStart_Left", "attackStartPowerStanding"];
        var L = new List<string>();
        foreach (var i in idles.Values.Where(i => events.Contains(i.AnimationEvent, StringComparer.OrdinalIgnoreCase)).OrderBy(i => i.AnimationEvent))
        {
            var vars = Up(i).SelectMany(a => a.Conditions.Select(c => (a.EditorID, c)))
                .Where(x => x.c.Data.GetType().Name.Contains("GraphVariable"))
                .Select(x => $"{x.EditorID}: {Describe(x.c)}");
            L.Add($"{i.AnimationEvent,-28} {i.EditorID,-34} {string.Join(" | ", vars)}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/graphvaridles.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
