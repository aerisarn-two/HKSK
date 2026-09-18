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
        File.WriteAllLines("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idle-events.txt", ev.OrderBy(e => e));
    }
}
