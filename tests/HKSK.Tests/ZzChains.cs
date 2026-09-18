using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// The ten flagged clips with no blender above them: every distinct chain of parent classes
// from the clip up to a root, over every parent, cycles cut per path.
public sealed class ZzChains
{
    private static readonly (string Project, string Clip)[] Wanted =
    [
        ("AtronachStormProject", "PowerAttack_F"),
        ("AtronachStormProject", "PowerAttack"),
        ("NetchProject", "NetchAttackLeft"),
        ("WitchlightProject", "Attack1"),
        ("WerewolfBeastProject", "WW AttackLeftSide.hkx"),
        ("WerewolfBeastProject", "WW AttackRightSide.hkx"),
        ("WerewolfBeastProject", "WW RunForwardAttackLeftSyncPower.hkx"),
        ("WerewolfBeastProject", "WW RunForwardAttackRightSyncPower.hkx"),
        ("WerewolfBeastProject", "WW SprintAllFoursAttackBoth.hkx00"),
        ("WerewolfBeastProject", "WW SprintAllFoursAttackBothOut.hkx00"),
    ];

    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var L = new List<string>();
        var allMods = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var perClip = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var group in Wanted.GroupBy(w => w.Project))
        {
            string? path = cache.FindProjectFile(group.Key);
            if (path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph { m_rootGenerator: not null } g) roots.TryAdd(s.File, g.m_rootGenerator);

            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                IEnumerable<IHavokObject> kids = s.Node is hkbBehaviorReferenceGenerator r
                    ? HavokPath.Resolve(folder, r.m_behaviorName) is { } q && roots.TryGetValue(Path.GetFullPath(q), out var root) ? [root] : []
                    : HavokEdges.Of(s.Node).Select(e => e.Item3);

                foreach (var kid in kids)
                {
                    if (!parents.TryGetValue(kid, out var list)) parents[kid] = list = [];
                    if (!list.Any(x => ReferenceEquals(x, s.Node))) list.Add(s.Node);
                }
            }

            foreach ((_, string clipName) in group)
            {
                var clips = walk.Steps
                    .Where(s => s.Node is hkbClipGenerator c && string.Equals(c.m_name, clipName, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Node)
                    .ToList();

                L.Add($"======== {group.Key}  {clipName}   ({clips.Count} clip generator{(clips.Count == 1 ? "" : "s")} of that name)");

                var chains = new Dictionary<string, int>(StringComparer.Ordinal);
                var mods = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int truncated = 0;

                // every modifier a hkbModifierGenerator on the chain attaches, through the lists
                void Modifiers(IHavokObject m)
                {
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                    var st = new Stack<IHavokObject>();
                    st.Push(m);
                    while (st.Count > 0)
                    {
                        var at = st.Pop();
                        if (!seen.Add(at)) continue;
                        if (at is hkbGenerator) continue;                 // not down another branch
                        if (at is hkbModifier && at is not hkbModifierList)
                            mods[at.GetType().Name] = mods.GetValueOrDefault(at.GetType().Name) + 1;
                        foreach (var (_, _, c) in HavokEdges.Of(at)) st.Push(c);
                    }
                }

                foreach (var clip in clips)
                    Walk(clip, [], []);

                foreach (var (chain, count) in chains.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
                    L.Add($"   x{count,-3} {chain}");
                L.Add($"   modifiers on the chain: {(mods.Count == 0 ? "(none)" : string.Join(", ", mods.Select(m => $"{m.Key} x{m.Value}")))}");
                foreach (var m in mods.Keys) allMods[m] = allMods.GetValueOrDefault(m) + 1;
                perClip[$"{group.Key}/{clipName}"] = [.. mods.Keys];
                if (truncated > 0) L.Add($"   ({truncated} path(s) cut at depth 40)");
                L.Add("");

                void Walk(IHavokObject at, List<string> so_far, HashSet<IHavokObject> onPath)
                {
                    if (!onPath.Add(at)) return;

                    var ups = parents.GetValueOrDefault(at);
                    if (ups is null || ups.Count == 0)
                    {
                        string chain = string.Join(" < ", so_far);
                        chains[chain] = chains.GetValueOrDefault(chain) + 1;
                    }
                    else if (so_far.Count >= 40)
                    {
                        truncated++;
                    }
                    else
                    {
                        foreach (var up in ups)
                        {
                            if (up is hkbModifierGenerator mg && mg.m_modifier is not null) Modifiers(mg.m_modifier);
                            if (up is hkbModifier bare && up is not hkbModifierList) mods[up.GetType().Name] = mods.GetValueOrDefault(up.GetType().Name) + 1;
                            so_far.Add(Short(up));
                            Walk(up, so_far, onPath);
                            so_far.RemoveAt(so_far.Count - 1);
                        }
                    }

                    onPath.Remove(at);
                }
            }
        }

        L.Add("======== every modifier class on the ten chains, and how many of the ten have it");
        foreach (var (m, n) in allMods.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
            L.Add($"   {n,2}/10  {m}");

        L.Add("");
        L.Add("======== modifiers shared by all ten");
        foreach (string m in allMods.Where(x => x.Value == perClip.Count).Select(x => x.Key)) L.Add($"   {m}");
        if (!allMods.Any(x => x.Value == perClip.Count)) L.Add("   (none)");

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/chains.txt", string.Join("\n", L));

        static string Short(IHavokObject n) => n.GetType().Name;
    }
}
