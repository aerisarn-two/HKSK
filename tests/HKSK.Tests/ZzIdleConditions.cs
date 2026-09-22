using HKSK.Cache;
using HKSK.Model;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// Every idle that can send an attack event, tied to its project through the behaviour file it
// names (inherited from the parent when blank), with its effective condition: its own
// conditions and every ancestor's, since a child is only reached when its parents pass.
public sealed class ZzIdleConditions
{
    [MastersFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));

        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();
        foreach (string m in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, m);
            if (!File.Exists(path)) continue;
            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var i in mod.IdleAnimations) idles[i.FormKey] = i;
        }

        IIdleAnimationGetter? Parent(IIdleAnimationGetter i) =>
            i.RelatedIdles.Count > 0 && !i.RelatedIdles[0].IsNull && idles.TryGetValue(i.RelatedIdles[0].FormKey, out var p) ? p : null;

        IIdleAnimationGetter? Previous(IIdleAnimationGetter i) =>
            i.RelatedIdles.Count > 1 && !i.RelatedIdles[1].IsNull && idles.TryGetValue(i.RelatedIdles[1].FormKey, out var p) ? p : null;

        // the siblings tried before this one, nearest first: it is reached only if all of them failed
        IEnumerable<IIdleAnimationGetter> Before(IIdleAnimationGetter i)
        {
            var seen = new HashSet<FormKey>();
            for (var at = Previous(i); at is not null && seen.Add(at.FormKey); at = Previous(at)) yield return at;
        }

        IEnumerable<IIdleAnimationGetter> Up(IIdleAnimationGetter i)
        {
            var seen = new HashSet<FormKey>();
            for (var at = i; at is not null && seen.Add(at.FormKey); at = Parent(at)) yield return at;
        }

        string? FolderOf(IIdleAnimationGetter i)
        {
            foreach (var at in Up(i))
            {
                string? f = at.Filename?.DataRelativePath.Path;
                if (string.IsNullOrEmpty(f)) continue;
                string norm = f.Replace('\\', '/').ToLowerInvariant();
                if (norm.StartsWith("meshes/")) norm = norm[7..];
                int b = norm.IndexOf("/behaviors/", StringComparison.Ordinal);
                return b < 0 ? Path.GetDirectoryName(norm)?.Replace('\\', '/') : norm[..b];
            }
            return null;
        }

        static string Show(IConditionGetter c)
        {
            string fn = c.Data.GetType().Name.Replace("ConditionData", "").Replace("BinaryOverlay", "");
            string value = c is IConditionFloatGetter cf ? $"{cf.ComparisonValue:0.##}" : "?";
            string op = c.CompareOperator switch
            {
                CompareOperator.EqualTo => "==", CompareOperator.NotEqualTo => "!=",
                CompareOperator.GreaterThan => ">", CompareOperator.GreaterThanOrEqualTo => ">=",
                CompareOperator.LessThan => "<", CompareOperator.LessThanOrEqualTo => "<=", _ => "?",
            };
            string or = c.Flags.HasFlag(Condition.Flag.OR) ? " OR" : "";
            return $"{fn}{op}{value}{or}";
        }

        // project stem -> meshes-relative folder
        var folderOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in cache.Actors())
            if (project.Folder is { } f)
                folderOf[project.Name] = Path.GetRelativePath(Corpus.Root!, f).Replace('\\', '/').ToLowerInvariant();

        var L = new List<string>();
        var summary = new List<string>();
        var verdicts = new List<string>();

        static bool Is(IConditionGetter c, string fn) => c.Data.GetType().Name.StartsWith(fn, StringComparison.Ordinal);
        static float Value(IConditionGetter c) => c is IConditionFloatGetter cf ? cf.ComparisonValue : float.NaN;

        foreach (var p in shipped.Projects.OrderBy(x => x.Stem, StringComparer.OrdinalIgnoreCase))
        {
            if (!folderOf.TryGetValue(p.Stem, out string? folder)) continue;

            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
                flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);
            if (flags.Count == 0) continue;

            var mine = idles.Values.Where(i => !string.IsNullOrEmpty(i.AnimationEvent)
                                               && flags.ContainsKey(i.AnimationEvent)
                                               && string.Equals(FolderOf(i), folder, StringComparison.OrdinalIgnoreCase)).ToList();

            L.Add($"======== {p.Stem}   ({folder})   {flags.Count} attack events, {mine.Count} idles send one");
            foreach ((string ev, int flag) in flags.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var these = mine.Where(i => string.Equals(i.AnimationEvent, ev, StringComparison.OrdinalIgnoreCase)).ToList();
                if (these.Count == 0) { L.Add($"   [{flag}] {ev,-36} -- no idle: chosen from the race's attack data alone"); summary.Add($"{flag}\t{p.Stem}\t{ev}\tnone"); continue; }

                foreach (var i in these)
                {
                    var chain = Up(i).ToList();
                    string own = string.Join(" & ", i.Conditions.Select(Show));
                    string above = string.Join(" | ", chain.Skip(1).Where(a => a.Conditions.Count > 0)
                        .Select(a => $"{a.EditorID}: {string.Join(" & ", a.Conditions.Select(Show))}"));
                    L.Add($"   [{flag}] {ev,-36} {i.EditorID,-38} own: {(own.Length == 0 ? "-" : own)}");
                    if (above.Length > 0) L.Add($"          {"",-36} {"",-38} above: {above}");
                    L.Add($"          {"",-36} {"",-38} path: {string.Join(" < ", chain.Select(a => a.EditorID))}");
                    foreach (var level in chain)
                    {
                        var earlier = Before(level).ToList();
                        if (earlier.Count == 0) continue;
                        L.Add($"          {"",-36} {"",-38} unless {level.EditorID} loses to: " +
                              string.Join(" | ", earlier.Select(e => $"{e.EditorID}[{(e.Conditions.Count == 0 ? "always" : string.Join(" & ", e.Conditions.Select(Show)))}]")));
                    }
                    summary.Add($"{flag}\t{p.Stem}\t{ev}\t{own} || {above}");

                    // what this idle requires of the actor's movement, reading own and ancestor
                    // conditions, and the earlier siblings it is only tried after
                    var required = chain.SelectMany(a => a.Conditions).ToList();
                    var beaten = chain.SelectMany(Before).SelectMany(b => b.Conditions).ToList();
                    bool sprint = required.Any(c => Is(c, "IsSprinting") && c.CompareOperator == CompareOperator.EqualTo && Value(c) == 1);
                    bool movingBySpeed =
                        required.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.GreaterThan or CompareOperator.GreaterThanOrEqualTo && Value(c) >= 1)
                        || beaten.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.LessThan or CompareOperator.LessThanOrEqualTo);
                    bool standing = required.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.LessThan or CompareOperator.LessThanOrEqualTo)
                        || required.Any(c => Is(c, "GetMovementDirection") && c.CompareOperator == CompareOperator.EqualTo && Value(c) == 0);
                    bool direction = required.Any(c => Is(c, "GetMovementDirection") && c.CompareOperator == CompareOperator.EqualTo && Value(c) >= 1);
                    verdicts.Add($"{flag}\t{p.Stem}\t{ev}\t{i.EditorID}\t{(sprint ? 1 : 0)}\t{(movingBySpeed ? 1 : 0)}\t{(standing ? 1 : 0)}\t{(direction ? 1 : 0)}");
                }
            }
            L.Add("");
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idleconditions.txt", string.Join("\n", L));
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idlesummary.tsv", string.Join("\n", summary));
        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/idleverdicts.tsv", string.Join("\n", verdicts));
        foreach (var m in mods) m.Dispose();
    }
}
