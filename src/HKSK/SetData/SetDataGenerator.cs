using HKSK.Cache;
using HKSK.Model;
using HKX2;

namespace HKSK.SetData;

/// <summary>
/// The events the masters say the game can send, which the behaviour files cannot.
/// </summary>
/// <param name="Idle">
/// Every idle record's animation event. The set lookup is only ever keyed on one of
/// these: an idle chosen directly, or the idle an action resolves to
/// (<c>docs/animation-set-data.md</c> §4.3).
/// </param>
/// <param name="Equip">
/// The idle events an equip or draw resolves to. The equip path passes the new hand types
/// along with the key, so these are the events weapon sets are keyed on.
/// </param>
/// <param name="Attacks">
/// Each project's attack events, from the attack data of the races that use it, by
/// project stem.
/// </param>
public sealed record GameEvents(
    IReadOnlySet<string> Idle,
    IReadOnlySet<string> Equip,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Attacks);

/// <summary>
/// Writes <c>animationsetdatasinglefile.txt</c> from what the game needs of it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing is read from a shipped set data file.</strong> The sets are decided
/// by how the executable uses them (<c>docs/animation-set-data.md</c> §4) rather than by
/// reproducing the shipped file, which is a record of how it was edited:
/// </para>
/// <list type="bullet">
/// <item>a lookup is keyed on an idle event, optionally with hand types, and loads the
/// files of the first set that matches -- so every event the graph handles gets a set
/// holding every file that event can lead to;</item>
/// <item>the set with no swap events is what an actor's graph loads when it is built --
/// so the base set holds everything no event set brings in;</item>
/// <item>a race reads attacks from the set its hand types select, or from a project's
/// only set -- so a project that never chooses by hand type gets one set holding all
/// of it, and one that does carries its attacks on the weapon sets.</item>
/// </list>
/// <para>
/// A clip the data misses still loads when it first plays, late (§4.4), so the rule
/// errs towards listing: a file in more sets than it needs costs memory, and a file in
/// too few costs a clip that starts without its animation.
/// </para>
/// </remarks>
public static class SetDataGenerator
{
    /// <summary>The single set of a project that never chooses by hand type.</summary>
    public const string WholeSetName = "FullCharacter.txt";

    /// <summary>The set with no swap events, in a project that has others.</summary>
    public const string BaseSetName = "Base.txt";

    /// <summary>The default for how far a set covering several weapons may outgrow one.</summary>
    public const double DefaultSlack = 1.5;

    /// <summary>Builds the whole file.</summary>
    /// <param name="cache">The game's animation cache, with the meshes folder its behaviours live in.</param>
    /// <param name="events">What the masters say can be sent.</param>
    /// <param name="slack">
    /// How much larger than one weapon's files a set may grow to cover several weapons.
    /// 1 splits a set wherever two weapons load different files; larger values give fewer,
    /// bigger sets.
    /// </param>
    public static AnimationSetDataFile Generate(SkyrimCache cache, GameEvents events, double slack = DefaultSlack)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(events);
        if (!(slack >= 1)) throw new ArgumentOutOfRangeException(nameof(slack), "a set cannot be smaller than the files one weapon loads");
        if (cache.MeshesFolder is null)
            throw new ArgumentException("the cache has to be read from a meshes folder, to find the animation files", nameof(cache));

        var file = new AnimationSetDataFile();

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasCache) continue;
            if (Build(cache.MeshesFolder, project, events, slack) is not { } sets) continue;

            file.Projects.Add(new AnimationSetDataProject { Name = $"{project.Name}Data\\{project.Name}.txt", Sets = sets });
        }

        return file;
    }

    internal static ProjectAttackListBlock? Build(string meshes, ActorProject project, GameEvents events, double slack)
    {
        if (project.Folder is null || project.ProjectFile is null) return null;
        if (GraphReach.Of(project.ProjectFile.File.Path) is not { } reach) return null;

        var files = new ProjectFiles(meshes, project);
        IReadOnlySet<string> attackEvents = events.Attacks.GetValueOrDefault(project.Name)
                                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return reach.ChoosesByHand
            ? ByHand(reach, files, events, attackEvents, slack)
            : Whole(reach, files, attackEvents);
    }

    // A project that never chooses by hand type: one set, everything in it.
    private static ProjectAttackListBlock Whole(GraphReach reach, ProjectFiles files, IReadOnlySet<string> attackEvents)
    {
        reach.SetHands(0, 0);

        var set = new ProjectAttackBlock();
        foreach (string path in files.All) set.Checksums.Add(path);
        set.Attacks.Attacks.AddRange(Attacks(reach, attackEvents));

        return new ProjectAttackListBlock { SetFiles = [WholeSetName], Sets = [set] };
    }

    private static List<AttackData> Attacks(GraphReach reach, IReadOnlySet<string> attackEvents)
    {
        var attacks = new List<AttackData>();

        foreach (string name in attackEvents.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            List<hkbClipGenerator> clips = reach.ClipsOfAttack(name);
            if (clips.Count == 0) continue;

            // a clip is named once, as the race looks it up by name: two branches holding a
            // clip of the same name are one entry
            attacks.Add(new AttackData { EventName = name, MovingAttack = 0, Clips = [.. clips.Select(c => c.m_name).Distinct(StringComparer.OrdinalIgnoreCase)] });
        }

        return attacks;
    }

    /// <summary>Hand combinations that load the same files, and what they must agree on besides.</summary>
    private sealed record Cell(List<(int, int)> Hands, FileSet Files, string Fixed);

    /// <summary>
    /// Folds cells together while the files they load stay within <paramref name="slack"/>
    /// of the largest: a set loads one weapon's worth of files and a little of the others',
    /// in place of one set per combination that differs by a file.
    /// </summary>
    private static List<Cell> Merge(IEnumerable<Cell> cells, double slack)
    {
        var clusters = new List<(Cell Cell, int Largest)>();

        foreach (Cell cell in cells.OrderByDescending(c => c.Files.Count))
        {
            int at = clusters.FindIndex(k =>
                k.Cell.Fixed == cell.Fixed &&
                k.Cell.Files.UnionCount(cell.Files) <= slack * Math.Max(k.Largest, cell.Files.Count));

            if (at < 0)
            {
                clusters.Add((new Cell([.. cell.Hands], cell.Files.Clone(), cell.Fixed), cell.Files.Count));
                continue;
            }

            clusters[at].Cell.Hands.AddRange(cell.Hands);
            clusters[at].Cell.Files.UnionWith(cell.Files);
        }

        return [.. clusters.Select(k => k.Cell)];
    }

    private sealed record Candidate(string Name, List<string> Events, List<HandVariable> Hands, List<string> Files, List<AttackData> Attacks);

    // A project that chooses by hand type: a base set, weapon sets, and a set per event.
    private static ProjectAttackListBlock ByHand(
        GraphReach reach, ProjectFiles files, GameEvents events, IReadOnlySet<string> attackEvents, double slack)
    {
        IReadOnlyList<(int Right, int Left)> combinations = HandCombinations.All;

        var fromRoot = new Dictionary<(int, int), FileSet>();
        var attacks = new Dictionary<(int, int), List<AttackData>>();
        var handled = new Dictionary<(int, int), List<string>>();
        var regionOf = new Dictionary<(int, int), Dictionary<string, FileSet>>();
        var bySignature = new Dictionary<string, (FileSet Everything, List<string> Handled, Dictionary<string, FileSet> Regions)>();

        foreach ((int right, int left) in combinations)
        {
            StateGraph graph = StateGraph.Build(reach, right, left, events.Idle);
            attacks[(right, left)] = Attacks(reach, attackEvents);

            if (!bySignature.TryGetValue(graph.Signature, out var seen))
            {
                FileSet home = files.Of(graph.ClipsOf(graph.Home));
                var regions = new Dictionary<string, FileSet>(StringComparer.OrdinalIgnoreCase);
                List<string> here = [];

                foreach (string name in events.Idle.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
                {
                    if (!graph.Handles(name)) continue;
                    here.Add(name);

                    FileSet region = files.Of(graph.ClipsOf(graph.UntilHome(name)));
                    region.ExceptWith(home);
                    regions[name] = region;
                }

                bySignature[graph.Signature] = seen = (files.Of(graph.ClipsOf(graph.Everything())), here, regions);
            }

            fromRoot[(right, left)] = seen.Everything;
            handled[(right, left)] = seen.Handled;
            regionOf[(right, left)] = seen.Regions;
        }

        // what every weapon a character can hold reaches; what is left of a combination's reach is its own
        FileSet common = Intersect(HandCombinations.Holdable.Select(h => fromRoot[h]));

        // an event depends on the weapon when its region's files do
        var byHand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in regionOf.Values.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            if (regionOf.Values.Select(r => r.GetValueOrDefault(name)?.Key() ?? "").Distinct().Count() > 1)
                byHand.Add(name);

        var after = regionOf.Values.SelectMany(r => r.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);

        FileSet After(string name, (int, int) hands) =>
            (byHand.Contains(name)
                ? regionOf[hands].GetValueOrDefault(name)
                : regionOf.Values.Select(r => r.GetValueOrDefault(name)).First(r => r is not null))?.Clone() ?? new FileSet();

        var candidates = new List<Candidate>();

        // weapon sets: what only this weapon reaches, what equipping it leads to, its attacks
        var weaponCells = new List<Cell>();
        var weaponOf = new Dictionary<string, (List<string> Events, List<AttackData> Attacks)>();
        foreach ((int, int) hands in combinations)
        {
            List<string> equip = [.. handled[hands].Where(events.Equip.Contains)];
            FileSet content = fromRoot[hands].Clone();
            content.ExceptWith(common);
            foreach (string name in equip) content.UnionWith(After(name, hands));
            if (content.Count == 0 && attacks[hands].Count == 0) continue;

            // what may not be merged across: the keys, and the attacks, which the race
            // reads for exactly these hands
            string fixedPart = string.Join("|", equip) + "#" +
                               string.Join("|", attacks[hands].Select(a => a.EventName + ":" + string.Join(",", a.Clips)));
            weaponOf.TryAdd(fixedPart, (equip, attacks[hands]));
            weaponCells.Add(new Cell([hands], content, fixedPart));
        }

        foreach (Cell cell in Merge(weaponCells, slack))
        {
            (List<string> equip, List<AttackData> attackList) = weaponOf[cell.Fixed];
            foreach (var box in HandCombinations.Cover(cell.Hands))
                candidates.Add(new Candidate(
                    $"Weapon_R{box.RightMin}-{box.RightMax}_L{box.LeftMin}-{box.LeftMax}.txt",
                    equip, Hands(box), files.Paths(cell.Files), attackList));
        }

        // event sets: what an idle event leads to, by weapon only where that matters
        foreach (string name in after.Where(n => !events.Equip.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!byHand.Contains(name))
            {
                FileSet content = After(name, default);
                if (content.Count > 0)
                    candidates.Add(new Candidate($"{name}.txt", [name], [], files.Paths(content), []));
                continue;
            }

            List<(int, int)> handledBy = [.. combinations.Where(h => handled[h].Contains(name, StringComparer.OrdinalIgnoreCase))];
            var cells = handledBy.Select(h => new Cell([h], After(name, h), "")).Where(c => c.Files.Count > 0);

            foreach (Cell cell in Merge(cells, slack))
            {
                if (cell.Hands.Count == handledBy.Count)
                {
                    candidates.Add(new Candidate($"{name}.txt", [name], [], files.Paths(cell.Files), []));
                    continue;
                }

                foreach (var box in HandCombinations.Cover(cell.Hands))
                    candidates.Add(new Candidate(
                        $"{name}_R{box.RightMin}-{box.RightMax}_L{box.LeftMin}-{box.LeftMax}.txt",
                        [name], Hands(box), files.Paths(cell.Files), []));
            }
        }

        // one set per identical content: its events are every key that brings it in
        var merged = new List<Candidate>();
        foreach (var same in candidates.GroupBy(c =>
                     string.Join("|", c.Hands.Select(h => $"{h.Name}{h.Min}-{h.Max}")) + "#" + string.Join("|", c.Files) + "#" +
                     string.Join("|", c.Attacks.Select(a => a.EventName + ":" + string.Join(",", a.Clips)))))
        {
            Candidate head = same.First();
            merged.Add(head with
            {
                Events = [.. same.SelectMany(c => c.Events).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e, StringComparer.OrdinalIgnoreCase)],
            });
        }

        var inSets = new HashSet<string>(merged.SelectMany(c => c.Files), StringComparer.OrdinalIgnoreCase);
        var block = new ProjectAttackListBlock();

        var baseSet = new ProjectAttackBlock();
        foreach (string path in files.All.Where(p => !inSets.Contains(p))) baseSet.Checksums.Add(path);
        block.SetFiles.Add(BaseSetName);
        block.Sets.Add(baseSet);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BaseSetName };
        // The race asks for attacks with hand types and no key, and takes the first set whose
        // ranges hold (§4.6): a set split by weapon for an idle event holds no attacks, and
        // placed first it answered for the weapon set behind it. Sets with attacks go first.
        foreach (Candidate c in merged.OrderByDescending(c => c.Attacks.Count > 0).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            string name = c.Name;
            for (int n = 2; !names.Add(name); n++) name = $"{Path.GetFileNameWithoutExtension(c.Name)}_{n}.txt";

            var set = new ProjectAttackBlock { SwapEvents = c.Events };
            set.HandVariables.Variables.AddRange(c.Hands);
            set.Attacks.Attacks.AddRange(c.Attacks);
            foreach (string path in c.Files) set.Checksums.Add(path);

            block.SetFiles.Add(name);
            block.Sets.Add(set);
        }

        return block;
    }

    private static List<HandVariable> Hands((int RightMin, int RightMax, int LeftMin, int LeftMax) box) =>
    [
        new HandVariable(GraphReach.Left, box.LeftMin, box.LeftMax),
        new HandVariable(GraphReach.Right, box.RightMin, box.RightMax),
    ];

    private static FileSet Intersect(IEnumerable<FileSet> sets)
    {
        FileSet? all = null;
        foreach (FileSet set in sets)
        {
            if (all is null) all = set.Clone();
            else all.IntersectWith(set);
        }

        return all ?? new FileSet();
    }
}
