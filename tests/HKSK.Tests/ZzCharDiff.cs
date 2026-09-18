using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The character file of every project: properties, IK drivers, mirroring, controller info.
// The six that flag attacks against the 43 that do not.
public sealed class ZzCharDiff
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var flagged = shipped.Projects
            .Where(p => p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Any(a => a.MovingAttack != 0))
            .Select(p => p.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<(bool Flag, string Project, string Line, List<string> Props)>();

        foreach (var project in cache.Actors())
        {
            if (!project.HasCache || project.Folder is null) continue;

            if (project.Character is not { } character) continue;
            hkbCharacterData data = character.Data;

            var sd = data.m_stringData;
            var props = (sd?.m_characterPropertyNames ?? []).ToList();

            string line = $"footIk {(data.m_footIkDriverInfo is null ? "-" : "Y")}  handIk {(data.m_handIkDriverInfo is null ? "-" : "Y")}  " +
                          $"mirror {(data.m_mirroredSkeletonInfo is null ? "-" : "Y")}  scale {data.m_scale:0.##}  " +
                          $"props {props.Count,2}  anims {(sd?.m_animationNames?.Count ?? 0),4}  " +
                          $"syncA {(sd?.m_mirroredSyncPointSubstringsA?.Count ?? 0)}  " +
                          $"lods {(sd?.m_lodNames?.Count ?? 0)}  " +
                          $"up {data.m_modelUpMS.X:0.#},{data.m_modelUpMS.Y:0.#},{data.m_modelUpMS.Z:0.#}  " +
                          $"fwd {data.m_modelForwardMS.X:0.#},{data.m_modelForwardMS.Y:0.#},{data.m_modelForwardMS.Z:0.#}";

            rows.Add((flagged.Contains(project.Name), project.Name, line, props));
        }

        var L = new List<string>();
        foreach (var r in rows.OrderByDescending(r => r.Flag).ThenBy(r => r.Project, StringComparer.OrdinalIgnoreCase))
            L.Add($"{(r.Flag ? "F" : " ")} {r.Project,-30} {r.Line}");

        var six = rows.Where(r => r.Flag).ToList();
        var rest = rows.Where(r => !r.Flag).ToList();

        L.Add("");
        L.Add("character properties: on every one of the six, and on how many of the 43");
        var all = rows.SelectMany(r => r.Props).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach (string prop in all)
        {
            int a = six.Count(r => r.Props.Contains(prop, StringComparer.OrdinalIgnoreCase));
            int b = rest.Count(r => r.Props.Contains(prop, StringComparer.OrdinalIgnoreCase));
            string mark = a == six.Count && b == 0 ? "   <<< only the six" : a == 0 && b == rest.Count ? "   <<< only the rest" : "";
            L.Add($"   {prop,-34} six {a}/{six.Count}   rest {b}/{rest.Count}{mark}");
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/chardiff.txt", string.Join("\n", L));
    }
}
