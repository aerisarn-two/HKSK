using HKSK.Cache;
using HKSK.Model;
using Xunit;
namespace HKSK.Tests;

// The engine measures an attack's reach from the root motion of the clips the entry names
// (0x140442a80). So: what is that number, flagged and clear?
public sealed class ZzAttackTravel
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var rows = new List<(string Project, string Event, int Flag, float Travel, int Found)>();

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            if (actor is null) continue;

            var travel = actor.Clips
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seen.Add(a.EventName)) continue;
                var found = a.Clips.Where(travel.ContainsKey).ToList();
                if (found.Count == 0) continue;
                rows.Add((p.Stem, a.EventName, a.MovingAttack, found.Max(c => travel[c]), found.Count));
            }
        }

        var L = new List<string> { $"attack events with cached clips: {rows.Count} (flagged {rows.Count(r => r.Flag != 0)})", "" };
        foreach (float t in new[] { 1f, 10f, 25f, 50f, 100f })
        {
            int a1 = rows.Count(r => r.Flag != 0 && r.Travel < t), a0 = rows.Count(r => r.Flag == 0 && r.Travel < t);
            L.Add($"   named clips travel < {t,5}:  flagged {a1,3} of {rows.Count(r => r.Flag != 0),3}    clear {a0,3} of {rows.Count(r => r.Flag == 0),3}");
        }

        L.Add("");
        L.Add("flagged, by travel:");
        foreach (var r in rows.Where(r => r.Flag != 0).OrderBy(r => r.Travel))
            L.Add($"   {r.Travel,8:0.0}  {r.Project,-24} {r.Event}");

        L.Add("");
        L.Add("every attack event and its named clips' travel:");
        foreach (var r in rows.OrderBy(r => r.Project).ThenBy(r => r.Event))
            L.Add($"   [{r.Flag}] {r.Travel,8:0.0}  {r.Project,-24} {r.Event}");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/travel.txt", string.Join("\n", L));
    }
}
