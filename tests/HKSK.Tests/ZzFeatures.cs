using HKSK.Cache;
using HKSK.Model;
using HKSK.SetData;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace HKSK.Tests;

// One row per (project, attack event) with every feature this investigation found, for
// fitting the moving-attack flag outside: the behaviour rule, the idle tree, root motion,
// the race's attack data and movement types.
public sealed class ZzFeatures
{
    [MastersFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));

        // ---- masters
        var mods = new List<ISkyrimModDisposableGetter>();
        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var races = new Dictionary<FormKey, IRaceGetter>();
        foreach (string m in Masters.Order)
        {
            string path = Path.Combine(Masters.DataFolder!, m);
            if (!File.Exists(path)) continue;
            var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            mods.Add(mod);
            foreach (var i in mod.IdleAnimations) idles[i.FormKey] = i;
            foreach (var r in mod.Races) races[r.FormKey] = r;
        }
        var link = mods.ToImmutableLinkCache();

        IEnumerable<IIdleAnimationGetter> Chain(IIdleAnimationGetter idle, int which)
        {
            var seen = new HashSet<FormKey>();
            for (IIdleAnimationGetter? at = idle; at is not null && seen.Add(at.FormKey);)
            {
                yield return at;
                at = at.RelatedIdles.Count > which && !at.RelatedIdles[which].IsNull
                     && idles.TryGetValue(at.RelatedIdles[which].FormKey, out var n) ? n : null;
            }
        }

        static string? FolderOf(string path)
        {
            string norm = path.Replace('\\', '/').ToLowerInvariant();
            if (norm.StartsWith("meshes/", StringComparison.Ordinal)) norm = norm[7..];
            int b = norm.IndexOf("/behaviors/", StringComparison.Ordinal);
            return b >= 0 ? norm[..b] : Path.GetDirectoryName(norm)?.Replace('\\', '/');
        }

        string? IdleFolder(IIdleAnimationGetter i)
        {
            foreach (var at in Chain(i, 0))
                if (at.Filename?.DataRelativePath.Path is { Length: > 0 } f) return FolderOf(f);
            return null;
        }

        static bool Is(IConditionGetter c, string fn) => c.Data.GetType().Name.StartsWith(fn, StringComparison.Ordinal);
        static float Val(IConditionGetter c) => c is IConditionFloatGetter f ? f.ComparisonValue : float.NaN;

        // folder -> stems, and stem -> races
        var stemsIn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var racesOf = new Dictionary<string, List<IRaceGetter>>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in races.Values)
            foreach (string? g in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path }.Distinct())
            {
                if (string.IsNullOrEmpty(g)) continue;
                string stem = Path.GetFileNameWithoutExtension(g.Replace('\\', '/'));
                if (FolderOf(g) is { } folder)
                {
                    if (!stemsIn.TryGetValue(folder, out var s)) stemsIn[folder] = s = new(StringComparer.OrdinalIgnoreCase);
                    s.Add(stem);
                }
                if (!racesOf.TryGetValue(stem, out var l)) racesOf[stem] = l = [];
                if (!l.Contains(race)) l.Add(race);
            }

        // (stem, event) -> idle requirement flags
        var idleOf = new Dictionary<(string, string), (bool Sprint, bool Moving, bool Standing, bool Direction, bool Any)>();
        foreach (var i in idles.Values)
        {
            if (string.IsNullOrEmpty(i.AnimationEvent) || IdleFolder(i) is not { } folder || !stemsIn.TryGetValue(folder, out var stems)) continue;
            var chain = Chain(i, 0).ToList();
            var need = chain.SelectMany(a => a.Conditions).ToList();
            var beaten = chain.SelectMany(a => Chain(a, 1).Skip(1)).SelectMany(b => b.Conditions).ToList();
            bool sprint = need.Any(c => Is(c, "IsSprinting") && c.CompareOperator == CompareOperator.EqualTo && Val(c) == 1);
            bool moving = need.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.GreaterThan or CompareOperator.GreaterThanOrEqualTo && Val(c) >= 1)
                          || beaten.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.LessThan or CompareOperator.LessThanOrEqualTo);
            bool standing = need.Any(c => Is(c, "GetMovementSpeed") && c.CompareOperator is CompareOperator.LessThan or CompareOperator.LessThanOrEqualTo)
                            || need.Any(c => Is(c, "GetMovementDirection") && c.CompareOperator == CompareOperator.EqualTo && Val(c) == 0);
            bool direction = need.Any(c => Is(c, "GetMovementDirection") && c.CompareOperator == CompareOperator.EqualTo && Val(c) >= 1);
            foreach (string stem in stems)
            {
                var k = (stem.ToLowerInvariant(), i.AnimationEvent.ToLowerInvariant());
                var had = idleOf.GetValueOrDefault(k);
                idleOf[k] = (had.Sprint | sprint, had.Moving | moving, had.Standing | standing, had.Direction | direction, true);
            }
        }

        var rows = new List<string> { "flag\tproject\tevent\tgraph\tidleSprint\tidleMoving\tidleStanding\tidleDirection\thasIdle\ttravel\tspeed\tpower\tbash\tleft\tatkType\tchance\tangle\tprojHasSpeedBlend\tprojLocoTravel\twalkMovt\trunMovt\tflyMovt\tknock\tstagger" };

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            string? file = cache.FindProjectFile(p.Stem);
            if (actor is null || file is null || GraphReach.Of(file) is not { } reach) continue;

            var flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
                flags[a.EventName] = Math.Max(flags.GetValueOrDefault(a.EventName), a.MovingAttack);
            if (flags.Count == 0) continue;

            var travelOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);
            var speedOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion is { Duration: > 0.01f } m ? m.Travel / m.Duration : 0f), StringComparer.OrdinalIgnoreCase);

            // does the creature animate its locomotion? travelling clips outside attacks
            var attackClips = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).SelectMany(a => a.Clips).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loco = actor.Clips.Where(c => !attackClips.Contains(c.Name)).Select(c => c.Slot?.Motion?.Travel ?? 0f).ToList();
            float locoTravel = loco.Count == 0 ? 0 : loco.Count(t => t > 50) / (float)loco.Count;

            // race attack data and movement types
            var raceList = racesOf.GetValueOrDefault(p.Stem) ?? [];
            string Movt(Func<IRaceGetter, IFormLinkNullableGetter<IMovementTypeGetter>> pick) =>
                string.Join("/", raceList.Select(r => pick(r).TryResolve(link)?.EditorID ?? "-").Distinct().Take(2));
            string walk = Movt(r => r.BaseMovementDefaultWalk), run = Movt(r => r.BaseMovementDefaultRun), fly = Movt(r => r.BaseMovementDefaultFly);

            // the behaviour's clips for the event, over the hand types the project chooses by
            var hands = reach.ChoosesByHand ? HandCombinations.All : [(0, 0)];
            var clipsByEvent = new Dictionary<string, HashSet<HKX2.hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            var graphFlag = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool projSpeedBlend = false;
            foreach ((int right, int left) in hands)
            {
                reach.SetHands(right, left);
                foreach (string ev in flags.Keys)
                {
                    var clips = reach.ClipsOfAttack(ev);
                    if (clips.Count == 0) continue;
                    if (!clipsByEvent.TryGetValue(ev, out var set)) clipsByEvent[ev] = set = new(ReferenceEqualityComparer.Instance);
                    foreach (var c in clips) set.Add(c);
                    if (!graphFlag.GetValueOrDefault(ev) && reach.TravelChosenBySpeed(clips)) graphFlag[ev] = true;
                }
            }

            foreach ((string ev, int flag) in flags)
            {
                var entryClips = p.Sets.Sets.SelectMany(s => s.Attacks.Attacks).Where(a => string.Equals(a.EventName, ev, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(a => a.Clips).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var known = entryClips.Where(travelOf.ContainsKey).ToList();
                float travel = known.Count == 0 ? -1 : known.Max(c => travelOf[c]);
                float speed = known.Count == 0 ? -1 : known.Max(c => speedOf[c]);

                var atk = raceList.SelectMany(r => r.Attacks).Where(a => string.Equals(a.AttackEvent, ev, StringComparison.OrdinalIgnoreCase) && a.AttackData is not null).Select(a => a.AttackData!).ToList();
                bool power = atk.Any(d => d.Flags.HasFlag(Mutagen.Bethesda.Skyrim.AttackData.Flag.PowerAttack));
                bool bash = atk.Any(d => d.Flags.HasFlag(Mutagen.Bethesda.Skyrim.AttackData.Flag.BashAttack));
                bool leftF = atk.Any(d => d.Flags.HasFlag(Mutagen.Bethesda.Skyrim.AttackData.Flag.LeftAttack));
                string type = string.Join("/", atk.Select(d => d.AttackType.TryResolve(link)?.EditorID ?? "-").Distinct());
                string chance = string.Join("/", atk.Select(d => d.Chance.ToString("0.##")).Distinct());
                string angle = string.Join("/", atk.Select(d => d.AttackAngle.ToString("0")).Distinct());
                string knock = string.Join("/", atk.Select(d => d.Knockdown.ToString("0.##")).Distinct());
                string stagger = string.Join("/", atk.Select(d => d.Stagger.ToString("0.##")).Distinct());

                var idle = idleOf.GetValueOrDefault((p.Stem.ToLowerInvariant(), ev.ToLowerInvariant()));
                rows.Add(string.Join("\t", flag, p.Stem, ev, graphFlag.GetValueOrDefault(ev) ? 1 : 0,
                    idle.Sprint ? 1 : 0, idle.Moving ? 1 : 0, idle.Standing ? 1 : 0, idle.Direction ? 1 : 0, idle.Any ? 1 : 0,
                    travel.ToString("0.0"), speed.ToString("0.0"), power ? 1 : 0, bash ? 1 : 0, leftF ? 1 : 0,
                    type.Length == 0 ? "none" : type, chance.Length == 0 ? "none" : chance, angle.Length == 0 ? "none" : angle,
                    projSpeedBlend ? 1 : 0, locoTravel.ToString("0.00"), walk, run, fly,
                    knock.Length == 0 ? "none" : knock, stagger.Length == 0 ? "none" : stagger));
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/features.tsv", string.Join("\n", rows));
        foreach (var m in mods) m.Dispose();
    }
}
