using System.Reflection;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

public sealed class ZzIdleShape
{
    [MastersFact]
    public void Look()
    {
        var L = new List<string>();
        foreach (var p in typeof(IIdleAnimationGetter).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Concat(typeof(IIdleAnimationGetter).GetInterfaces().SelectMany(i => i.GetProperties())))
            L.Add($"{p.Name} : {p.PropertyType.Name}");

        using var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, "Skyrim.esm"), SkyrimRelease.SkyrimSE);
        foreach (var i in mod.IdleAnimations.Where(i => i.AnimationEvent is "attackStart" or "AttackStartLeftPower").Take(3))
        {
            L.Add($"--- {i.EditorID}  event {i.AnimationEvent}  filename '{i.Filename}'  related {string.Join(",", i.RelatedIdles.Select(r => r.FormKey.ToString()))}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idleshape.txt", string.Join("\n", L.Distinct()));
    }
}
