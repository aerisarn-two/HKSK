using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// Which creatures hover? Candidate signals: a speed-parametric locomotion blend anywhere in the
// graph, how far their locomotion clips travel, foot IK, and the race's flags.
public sealed class ZzHover
{
    [MastersFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));

        var races = new Dictionary<FormKey, IRaceGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, m);
            if (!File.Exists(path)) continue;
            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var r in mod.Races) races[r.FormKey] = r;
        }
        var raceFlags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in races.Values)
            if (race.BehaviorGraph.Male?.File.DataRelativePath.Path is { Length: > 0 } g)
            {
                string stem = Path.GetFileNameWithoutExtension(g.Replace('\\', '/'));
                raceFlags.TryAdd(stem, race.Flags.ToString());
            }

        var L = new List<string> { $"{"project",-30} {"flag/atk",8} {"speedBlends",11} {"locoMaxTravel",13} {"footIk",6}  race flags" };
        foreach (var p in shipped.Projects.OrderBy(x => x.Stem))
        {
            string? file = cache.FindProjectFile(p.Stem);
            var actor = cache.OpenActor(p.Stem);
            if (file is null || actor is null) continue;
            var attacks = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).GroupBy(a => a.EventName, StringComparer.OrdinalIgnoreCase).ToList();
            if (attacks.Count == 0) continue;
            int flagged = attacks.Count(g => g.Any(a => a.MovingAttack != 0));

            var walk = ProjectWalk.Of(file);
            var vars = new Dictionary<string, string[]>();
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g) vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];

            // speed-parametric blends and the travel of the clips under them: the animated locomotion
            var travelOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);
            int blends = 0; float maxTravel = 0;
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbBlenderGenerator bl) continue;
                var names = vars.GetValueOrDefault(s.File) ?? [];
                bool onSpeed = (bl.m_variableBindingSet?.m_bindings ?? []).Any(b => b.m_memberPath == "blendParameter"
                    && b.m_variableIndex >= 0 && b.m_variableIndex < names.Length && names[b.m_variableIndex].Contains("speed", StringComparison.OrdinalIgnoreCase));
                if (!onSpeed) continue;
                blends++;
                foreach (var c in bl.m_children)
                    if (c?.m_generator is hkbClipGenerator clip) maxTravel = Math.Max(maxTravel, travelOf.GetValueOrDefault(clip.m_name));
            }

            bool footIk = actor.Character?.Data.m_footIkDriverInfo is not null;
            L.Add($"{p.Stem,-30} {flagged,3}/{attacks.Count,-4} {blends,11} {maxTravel,13:0} {(footIk ? "Y" : "-"),6}  {raceFlags.GetValueOrDefault(p.Stem, "?")}");
        }
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/hover.txt", string.Join("\n", L));
        foreach (var m in mods) m.Dispose();
    }
}
