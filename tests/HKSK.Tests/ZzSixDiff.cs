using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Any name -- behaviour variable, event, character property, node class -- that every one of
// the six flagging projects has and none of the 43 do, or the other way round.
public sealed class ZzSixDiff
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flagged = shipped.Projects
            .Where(p => p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Any(a => a.MovingAttack != 0))
            .Select(p => p.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var have = new Dictionary<string, (bool Flag, HashSet<string> Vars, HashSet<string> Events, HashSet<string> Props, HashSet<string> Kinds)>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in cache.Actors())
        {
            if (!project.HasCache || project.ProjectFile is null) continue;
            string? path = cache.FindProjectFile(project.Name);
            if (path is null) continue;

            var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kinds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in ProjectWalk.Of(path).Steps)
            {
                kinds.Add(s.Node.GetType().Name);
                if (s.Node is not hkbBehaviorGraph g) continue;
                foreach (string v in g.m_data?.m_stringData?.m_variableNames ?? []) vars.Add(v);
                foreach (string e in g.m_data?.m_stringData?.m_eventNames ?? []) events.Add(e);
            }

            var props = new HashSet<string>(
                project.Character?.Data.m_stringData?.m_characterPropertyNames ?? [], StringComparer.OrdinalIgnoreCase);

            have[project.Name] = (flagged.Contains(project.Name), vars, events, props, kinds);
        }

        var six = have.Values.Where(v => v.Flag).ToList();
        var rest = have.Values.Where(v => !v.Flag).ToList();

        var L = new List<string> { $"{six.Count} flagging projects against {rest.Count} others", "" };

        void Compare(string label, Func<(bool Flag, HashSet<string> Vars, HashSet<string> Events, HashSet<string> Props, HashSet<string> Kinds), HashSet<string>> pick)
        {
            var all = six.Concat(rest).SelectMany(pick).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var onlySix = all.Where(n => six.All(s => pick(s).Contains(n)) && rest.All(r => !pick(r).Contains(n))).ToList();
            var onlyRest = all.Where(n => six.All(s => !pick(s).Contains(n)) && rest.All(r => pick(r).Contains(n))).ToList();
            var mostlySix = all.Where(n => six.All(s => pick(s).Contains(n)) && rest.Count(r => pick(r).Contains(n)) <= 4).ToList();
            var noneSix = all.Where(n => six.All(s => !pick(s).Contains(n)) && rest.Count(r => pick(r).Contains(n)) >= rest.Count - 4).ToList();

            L.Add($"== {label}: {all.Count} distinct");
            L.Add($"   on all six and none of the rest : {(onlySix.Count == 0 ? "(none)" : string.Join(", ", onlySix))}");
            L.Add($"   on all the rest and none of six : {(onlyRest.Count == 0 ? "(none)" : string.Join(", ", onlyRest))}");
            L.Add($"   on all six, on at most 4 others : {(mostlySix.Count == 0 ? "(none)" : string.Join(", ", mostlySix.Take(20)))}");
            L.Add($"   on no six, on all but 4 others  : {(noneSix.Count == 0 ? "(none)" : string.Join(", ", noneSix.Take(20)))}");
            L.Add("");
        }

        Compare("behaviour variables", v => v.Vars);
        Compare("behaviour events", v => v.Events);
        Compare("character properties", v => v.Props);
        Compare("node classes used", v => v.Kinds);

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/sixdiff.txt", string.Join("\n", L));
    }
}
