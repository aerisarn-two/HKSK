using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzIdleList
{
    [MastersFact]
    public void Look()
    {
        var ev = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string m in Masters.Order)
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, m), SkyrimRelease.SkyrimSE);
            foreach (var i in mod.IdleAnimations) if (!string.IsNullOrEmpty(i.AnimationEvent)) ev.Add(i.AnimationEvent);
        }
        File.WriteAllLines("/tmp/hksk-dumps/idle-events.txt", ev.OrderBy(e => e));
    }
}
