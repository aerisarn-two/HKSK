using HKSK.Cache;
using Xunit;

namespace HKSK.Tests;

public sealed class ZzSplitSets
{
    [CorpusFact]
    public void Look()
    {
        string root = Corpus.Root!;
        var single = AnimationSetDataFile.Load(Path.Combine(root, "animationsetdatasinglefile.txt"));
        var L = new List<string>();
        int same = 0, differ = 0;
        foreach (string dir in Directory.GetDirectories(Path.Combine(root, "animationsetdata")).Order())
        {
            string projectDataName = Path.GetFileName(dir);
            var proj = single.Projects.FirstOrDefault(p => string.Equals(p.Name.Split('\\')[0], projectDataName, StringComparison.OrdinalIgnoreCase));
            if (proj is null) { L.Add($"{projectDataName}: not in single file"); continue; }
            foreach (string f in Directory.GetFiles(dir).Order())
            {
                string name = Path.GetFileName(f);
                var set = proj.Sets.Set(name);
                if (set is null) continue;   // the project list file itself
                var split = ProjectAttackBlock.Read(new LineCursor(File.ReadAllLines(f)));
                bool eq = split.Checksums.Entries.SequenceEqual(set.Checksums.Entries)
                       && split.SwapEvents.SequenceEqual(set.SwapEvents)
                       && split.Attacks.Attacks.Count == set.Attacks.Attacks.Count;
                if (eq) same++; else { differ++; L.Add($"{projectDataName}/{name}: split anims {split.Checksums.Entries.Count / 3} attacks {split.Attacks.Attacks.Count} | single anims {set.Checksums.Entries.Count / 3} attacks {set.Attacks.Attacks.Count}"); }
            }
        }
        L.Insert(0, $"sets identical in split and single file: {same}, differing: {differ}");
        File.WriteAllText("/tmp/splitsets.txt", string.Join("\n", L));
    }
}
