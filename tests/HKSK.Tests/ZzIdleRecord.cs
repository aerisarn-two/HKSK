using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;
public sealed class ZzIdleRecord
{
    [MastersFact]
    public void Look()
    {
        var L = new List<string>();
        var wanted = (Environment.GetEnvironmentVariable("EVENTS") ?? "idle_A_left_longTrans,IdleNeutralLeft,IdleCombatLookingAroundStart,IdleLeverFloorPull,IdleChairFrontEnter").Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filenames = new Dictionary<string, int>();
        int total = 0, withFile = 0;
        foreach (string m in Masters.Order)
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Masters.DataFolder!, m), SkyrimRelease.SkyrimSE);
            foreach (var i in mod.IdleAnimations)
            {
                total++;
                string file = i.Filename ?? "";
                if (file.Length > 0) { withFile++; filenames[Path.GetFileName(file.Replace('\\', '/'))] = filenames.GetValueOrDefault(Path.GetFileName(file.Replace('\\', '/'))) + 1; }
                if (wanted.Contains(i.AnimationEvent ?? ""))
                    L.Add($"{m} {i.EditorID}: event {i.AnimationEvent} file '{file}' flags {i.Flags} related {string.Join(",", i.RelatedIdles.Select(r => r.FormKey.ToString()))}");
            }
        }
        L.Add($"idle records {total}, with a filename {withFile}; filenames: {string.Join(", ", filenames.OrderByDescending(k => k.Value).Take(15).Select(k => $"{k.Key} x{k.Value}"))}");
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idlerecord.txt", string.Join("\n", L));
    }
}
