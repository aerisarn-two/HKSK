using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The twelve vanilla flags the derivation still misses: every variable their clips' ancestry
// touches, against the variables of the same projects' clear attacks.
public sealed class ZzLastTwelve
{
    private static readonly HashSet<string> Missed = new(StringComparer.OrdinalIgnoreCase)
    {
        "AtronachStormProject/attackPowerStart_ForwardAttack",
        "AtronachStormProject/attackPowerStart_StandingAttack",
        "AtronachStormProject/attackStart_Attack_Swipe",
        "NetchProject/attackStartLeft", "NetchProject/attackStartRight",
        "WitchlightProject/attackStart_Attack1",
        "WerewolfBeastProject/AttackStartLeftSide", "WerewolfBeastProject/AttackStartRightSide",
        "WerewolfBeastProject/AttackStartLeftRunningPower", "WerewolfBeastProject/AttackStartRightRunningPower",
        "DefaultMale/bashStart", "DefaultFemale/bashStart",
    };

    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string>();

        foreach (string stem in new[] { "AtronachStormProject", "NetchProject", "WitchlightProject", "WerewolfBeastProject", "DefaultMale" })
        {
            var p = shipped.Projects.FirstOrDefault(x => string.Equals(x.Stem, stem, StringComparison.OrdinalIgnoreCase));
            string? path = cache.FindProjectFile(stem);
            if (p is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var vars = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    vars[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                }
            string FileOf(IHavokObject n) => walk.Steps.FirstOrDefault(x => ReferenceEquals(x.Node, n)).File ?? "";

            IEnumerable<IHavokObject> Kids(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    string? q = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (q is not null && roots.TryGetValue(Path.GetFullPath(q), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
                foreach (var kid in Kids(s.Node))
                {
                    if (!parents.TryGetValue(kid, out var l)) parents[kid] = l = [];
                    if (!l.Any(x => ReferenceEquals(x, s.Node))) l.Add(s.Node);
                }

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var l)) byName[c.m_name] = l = [];
                    l.Add(c);
                }

            // every variable the ancestry of an attack's clips touches, within a few levels
            SortedSet<string> Touched(List<hkbClipGenerator> clips, int limit)
            {
                var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var q = new Queue<(IHavokObject Node, int Depth)>();
                foreach (var c in clips) { seen.Add(c); q.Enqueue((c, 0)); }

                while (q.Count > 0)
                {
                    (IHavokObject at, int d) = q.Dequeue();
                    if (d >= limit) continue;
                    foreach (var up in parents.GetValueOrDefault(at) ?? [])
                    {
                        if (!seen.Add(up)) continue;
                        var names = vars.GetValueOrDefault(FileOf(up)) ?? [];

                        void Bind(IHavokObject n)
                        {
                            var set = n switch
                            {
                                hkbNode hn => hn.m_variableBindingSet,
                                hkbBlenderGeneratorChild ch => ch.m_variableBindingSet,
                                _ => null,
                            };
                            foreach (var b in set?.m_bindings ?? [])
                                if (b.m_variableIndex >= 0 && b.m_variableIndex < names.Length) found.Add(names[b.m_variableIndex]);
                        }

                        Bind(up);
                        if (up is hkbModifierGenerator mg && mg.m_modifier is not null)
                        {
                            var st = new Stack<IHavokObject>(); st.Push(mg.m_modifier);
                            var seenM = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                            while (st.Count > 0)
                            {
                                var n2 = st.Pop();
                                if (!seenM.Add(n2) || (n2 is hkbGenerator && !ReferenceEquals(n2, mg.m_modifier))) continue;
                                Bind(n2);
                                foreach (var (_, _, k) in HavokEdges.Of(n2)) st.Push(k);
                            }
                        }

                        q.Enqueue((up, d + 1));
                    }
                }

                return found;
            }

            var rows = new List<(bool Flag, bool Miss, string Event, SortedSet<string> Vars)>();
            var seenEv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seenEv.Add(a.EventName)) continue;
                var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (clips.Count == 0) continue;
                rows.Add((a.MovingAttack != 0, Missed.Contains($"{stem}/{a.EventName}"), a.EventName, Touched(clips, 4)));
            }

            var missed = rows.Where(r => r.Miss).ToList();
            var clear = rows.Where(r => !r.Flag).ToList();

            L.Add($"======== {stem}   ({missed.Count} missed flags, {clear.Count} clear attacks)");
            foreach (var r in missed)
                L.Add($"   MISSED {r.Event,-34} {string.Join(" ", r.Vars)}");
            foreach (var r in clear)
                L.Add($"   clear  {r.Event,-34} {string.Join(" ", r.Vars)}");

            if (missed.Count > 0)
            {
                var common = missed.Select(r => r.Vars).Aggregate((a, b) => new SortedSet<string>(a.Intersect(b, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));
                var onClear = clear.SelectMany(r => r.Vars).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string only = string.Join(", ", common.Except(onClear, StringComparer.OrdinalIgnoreCase));
                L.Add($"   >>> on every missed flag and no clear attack: {(only.Length == 0 ? "(none)" : only)}");
                L.Add($"   >>> on every missed flag, clear or not: {string.Join(", ", common)}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/lasttwelve.txt", string.Join("\n", L));
    }
}
