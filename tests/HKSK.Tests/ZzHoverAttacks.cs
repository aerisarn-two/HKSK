using System.Numerics;
using HKSK.Cache;
using HKSK.Model;
using Xunit;
namespace HKSK.Tests;

// The creatures with no speed-parametric locomotion: every attack clip's root motion as the
// engine reads it -- end translation, duration, the HitFrame time and the translation there --
// and the travel of their non-attack clips.
public sealed class ZzHoverAttacks
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        string[] stems = ["AtronachStormProject", "WitchlightProject", "WispProject", "IceWraithProject", "ChaurusFlyer", "DragonProject", "DwarvenSpiderCenturionProject", "NetchProject"];
        var L = new List<string>();

        static Vector3 At(ClipMovement m, float t)
        {
            Vector3 prevV = Vector3.Zero; float prevT = 0;
            foreach (var k in m.Translations.OrderBy(k => k.Time))
            {
                if (t <= k.Time)
                    return k.Time <= prevT ? k.Value : Vector3.Lerp(prevV, k.Value, (t - prevT) / (k.Time - prevT));
                prevV = k.Value; prevT = k.Time;
            }
            return prevV;
        }

        foreach (string stem in stems)
        {
            var p = shipped.Projects.First(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            var actor = cache.OpenActor(stem)!;
            var attackClips = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).SelectMany(a => a.Clips).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var loco = actor.Clips.Where(c => !attackClips.Contains(c.Name) && c.Slot?.Motion is not null)
                .Select(c => (c.Name, T: c.Slot!.Motion!.Travel, D: c.Slot.Motion.Duration)).ToList();
            var moving = loco.Where(x => x.T > 50).OrderByDescending(x => x.T).ToList();
            L.Add($"======== {stem}   non-attack clips {loco.Count}, travelling {moving.Count}: {string.Join(", ", moving.Take(6).Select(x => $"{x.Name} {x.T:0}/{x.D:0.#}s"))}");

            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).GroupBy(a => a.EventName, StringComparer.OrdinalIgnoreCase))
            {
                int flag = a.Max(x => x.MovingAttack);
                foreach (string name in a.SelectMany(x => x.Clips).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var clip = actor.Clips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (clip?.Slot?.Motion is not { } m) { L.Add($"   [{flag}] {a.Key,-34} {name,-30} (no motion in the cache)"); continue; }
                    var end = m.Translations.Count == 0 ? Vector3.Zero : m.Translations.OrderBy(k => k.Time).Last().Value;
                    float? hit = clip.Entry.Events.FirstOrDefault(e => string.Equals(e.Name, "HitFrame", StringComparison.OrdinalIgnoreCase))?.Time;
                    var atHit = hit is { } h ? At(m, h) : Vector3.Zero;
                    L.Add($"   [{flag}] {a.Key,-34} {name,-30} dur {m.Duration,5:0.00}s  end ({end.X,6:0},{end.Y,6:0},{end.Z,6:0}) = {end.Length(),5:0}  " +
                          $"hit {(hit is { } t ? $"{t,5:0.00}s" : "  none")}  at hit {atHit.Length(),5:0}  speed {(m.Duration > 0 ? end.Length() / m.Duration : 0),5:0}/s");
                }
            }
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/hoverattacks.txt", string.Join("\n", L));
    }
}
