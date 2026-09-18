using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// What the projects that use the moving-attack flag have that the others do not.
public sealed class ZzWhoFlags
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));

        // the split cache under animationdata/ is a pre-DLC snapshot: being absent from it
        // is the cheapest test of "this creature came with a DLC"
        var preDlc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string split = Path.Combine(Corpus.Root!, "animationdata");
        if (Directory.Exists(split))
            foreach (string d in Directory.GetDirectories(split))
                preDlc.Add(Path.GetFileName(d).Replace("data", "", StringComparison.OrdinalIgnoreCase));

        var rows = new List<string>();
        rows.Add($"{"project",-30} {"flag",4} {"atk",4} {"clips",6} {"moving",6} {"maxTravel",9} {"DLC",4} {"vars: sprint/dir/driven/footIK",6}");

        foreach (var p in shipped.Projects.OrderByDescending(x => x.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Count(a => a.MovingAttack != 0)))
        {
            var actor = cache.OpenActor(p.Stem);
            var path = cache.FindProjectFile(p.Stem);
            if (actor is null || path is null) continue;

            int flagged = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Count(a => a.MovingAttack != 0);
            int attacks = p.Sets.Sets.Sum(s => s.Attacks.Attacks.Count);

            var travels = actor.Clips.Select(c => c.Slot?.Motion?.Travel ?? 0f).ToList();
            int moving = travels.Count(t => t > 50f);
            float max = travels.Count > 0 ? travels.Max() : 0f;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in ProjectWalk.Of(path).Steps)
                if (s.Node is hkbBehaviorGraph g)
                    foreach (string v in g.m_data?.m_stringData?.m_variableNames ?? []) names.Add(v);

            string has =
                (names.Any(n => n.Contains("Sprint", StringComparison.OrdinalIgnoreCase)) ? "S" : "-") +
                (names.Contains("Direction") ? "D" : "-") +
                (names.Contains("bAnimationDriven") ? "A" : "-") +
                (names.Any(n => n.Contains("FootIK", StringComparison.OrdinalIgnoreCase)) ? "F" : "-") +
                (names.Any(n => n.Contains("Speed", StringComparison.OrdinalIgnoreCase)) ? "V" : "-");

            bool dlc = !preDlc.Contains(p.Stem);

            rows.Add($"{p.Stem,-30} {flagged,4} {attacks,4} {actor.Clips.Count,6} {moving,6} {max,9:0} {(dlc ? "DLC" : "base"),4}  {has}");
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/whoflags.txt", string.Join("\n", rows));
    }
}
