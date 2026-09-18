using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzIdles
{
    [MastersFact]
    public void Look()
    {
        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, m), SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var i in mod.IdleAnimations) idles[i.FormKey] = i;
        }
        var L = new List<string> { $"idle records {idles.Count}, distinct events {idles.Values.Select(i => i.AnimationEvent).Where(e => !string.IsNullOrEmpty(e)).Distinct(StringComparer.OrdinalIgnoreCase).Count()}" };
        string Chain(IIdleAnimationGetter i)
        {
            var parts = new List<string>();
            var cur = i; int guard = 0;
            while (cur is not null && guard++ < 20)
            {
                parts.Add($"{cur.EditorID}({cur.AnimationEvent})");
                var parent = cur.RelatedIdles.FirstOrDefault();
                cur = parent is not null && idles.TryGetValue(parent.FormKey, out var p) ? p : null;
            }
            return string.Join(" <- ", parts);
        }
        foreach (var i in idles.Values.Where(i => (i.AnimationEvent ?? "").Contains("Equip", StringComparison.OrdinalIgnoreCase)))
            L.Add(Chain(i));
        // roots: idles with no parent
        L.Add("== roots");
        foreach (var i in idles.Values.Where(i => i.RelatedIdles.Count == 0 || i.RelatedIdles[0].IsNull))
            L.Add($"{i.EditorID} ({i.AnimationEvent}) flags={i.Flags}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idles.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
