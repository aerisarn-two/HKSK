using HKSK.Behavior;
using HKSK.Cache;
using HKX2;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The <c>iState_&lt;movement type&gt;</c> constants against the speed table's keys.
/// </summary>
/// <remarks>
/// These constants are the dictionary between the two: a block keyed 2 and a
/// variable <c>iState_Falmer1HMWalk</c> holding 2 say that block belongs to that
/// movement type.
/// </remarks>
public sealed class StateConstantTests
{
    private static List<(string Name, ProjectWalk Walk, BehaviorRoot Root, SkyrimCache Cache)> Load()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var projects = new List<(string, ProjectWalk, BehaviorRoot, SkyrimCache)>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            projects.Add((at.Name, walk, BehaviorRoot.Of(at.ProjectFile!)!.Value, cache));
        }

        return projects;
    }

    /// <summary>
    /// Every key in every speed block is the value of a declared constant -- 78 of
    /// 78.
    /// </summary>
    /// <remarks>
    /// This is the relation that actually holds, and it holds exactly. A block's
    /// key is never a number the graph has not declared a name for, so the table
    /// can always be read back to movement types.
    /// </remarks>
    [CorpusFact]
    public void EveryTableKeyIsADeclaredConstant()
    {
        int keys = 0, declared = 0;
        var orphans = new List<string>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            var constants = StateConstants.Of(walk, root);

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                if (entry.Records.Count == 0) continue;

                keys++;
                if (StateConstants.MovementTypeOf(constants, (int)entry.Key) is not null) declared++;
                else orphans.Add($"{name}: key {entry.Key}");
            }
        }

        Assert.Equal(78, keys);
        Assert.Equal(78, declared);
        Assert.Empty(orphans);
    }

    /// <summary>
    /// The counts do <strong>not</strong> match: the constants are a strict
    /// superset of the keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 145 constants across the 41 root graphs against 78 keys, and the two counts
    /// agree in only 18 projects. 67 declared movement types have no block at all.
    /// </para>
    /// <para>
    /// The first-person rig is the clearest case -- it declares 19, sharing
    /// <c>0_Master</c> with the player, and ships a table with one entry. So a
    /// project declaring a movement type says nothing about that movement type
    /// having been sampled, and anything sizing the table from the variable list
    /// would write 67 blocks nobody asked for.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheConstantsAreAStrictSupersetOfTheKeys()
    {
        int constants = 0, keys = 0, agreeing = 0, spare = 0;

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            var declared = StateConstants.Of(walk, root);

            var blocks = cache.SpeedData!.Block(name)!.Entries
                .Where(e => e.Records.Count > 0)
                .Select(e => (int)e.Key)
                .ToHashSet();

            constants += declared.Count;
            keys += blocks.Count;
            if (declared.Count == blocks.Count) agreeing++;
            spare += declared.Values.Distinct().Count(v => !blocks.Contains(v));
        }

        Assert.Equal(145, constants);
        Assert.Equal(78, keys);
        Assert.Equal(18, agreeing);     // and 23 that do not
        Assert.Equal(67, spare);
    }

    /// <summary>
    /// The constants must come from the root graph, or a shared file inflates them.
    /// </summary>
    /// <remarks>
    /// <c>quadrupedbehavior.hkx</c> carries the union of every quadruped's movement
    /// types, so the dog reaches 18 names across its files while its own root graph
    /// declares 2. Counted across files the corpus has 310 of these; counted where
    /// they belong, 145.
    /// </remarks>
    [CorpusFact]
    public void CountingAcrossFilesInflatesTheConstants()
    {
        int everywhere = 0, inRoot = 0;

        foreach ((_, ProjectWalk walk, BehaviorRoot root, _) in Load())
        {
            var variables = new ProjectVariables(walk.Steps);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in variables.Files)
                foreach (string variable in variables.Of(file))
                    if (variable.StartsWith("iState_", StringComparison.OrdinalIgnoreCase))
                        names.Add(variable);

            everywhere += names.Count;
            inRoot += StateConstants.Of(walk, root).Count;
        }

        Assert.Equal(310, everywhere);
        Assert.Equal(145, inRoot);
    }

    /// <summary>A project never gives two movement types the same key.</summary>
    /// <remarks>
    /// Name to value is one to one in all 41, so a key read back from the table
    /// names exactly one movement type and the dictionary is unambiguous in both
    /// directions.
    /// </remarks>
    [CorpusFact]
    public void TheKeysAreUniqueWithinAProject()
    {
        int projects = 0;

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, _) in Load())
        {
            var constants = StateConstants.Of(walk, root);

            Assert.Equal(constants.Count, constants.Values.Distinct().Count());
            projects++;
        }

        Assert.Equal(41, projects);
    }

    /// <summary>The falmer, read end to end.</summary>
    [CorpusFact]
    public void TheFalmersKeysNameItsMovementTypes()
    {
        (string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) =
            Load().First(p => p.Name == "FalmerProject");

        var constants = StateConstants.Of(walk, root);

        Assert.Equal("Falmer1HMWalk", StateConstants.MovementTypeOf(constants, 2));
        Assert.Equal("Falmer1HMRun", StateConstants.MovementTypeOf(constants, 3));

        // and the table's keys are a subset of what it declares
        var keys = cache.SpeedData!.Block(name)!.Entries
            .Where(e => e.Records.Count > 0)
            .Select(e => (int)e.Key)
            .ToList();

        Assert.All(keys, k => Assert.NotNull(StateConstants.MovementTypeOf(constants, k)));
        Assert.True(keys.Count < constants.Count, "the falmer ships a block for every movement type it declares");
    }

    /// <summary>
    /// Every constant is a plain 32-bit integer with no role, and its initial value
    /// is the key.
    /// </summary>
    /// <remarks>
    /// All 145 are type 3 (<c>INT32</c>) with role 0, so nothing about them is
    /// special-cased in the graph: the value sitting in
    /// <c>m_variableInitialValues.m_wordVariableValues</c> is simply what the
    /// variable holds, and nothing ever writes it. The values run from 0 to 101.
    /// </remarks>
    [CorpusFact]
    public void TheConstantsAreIntegersHoldingTheirKey()
    {
        int constants = 0, integers = 0, plainRole = 0, lowest = int.MaxValue, highest = int.MinValue;

        foreach ((_, ProjectWalk walk, BehaviorRoot root, _) in Load())
        {
            string wanted = Path.GetFullPath(root.BehaviorFile);

            foreach (ProjectStep step in walk.Steps)
            {
                if (step.Node is not hkbBehaviorGraph graph) continue;
                if (!string.Equals(Path.GetFullPath(step.File), wanted, StringComparison.OrdinalIgnoreCase)) continue;

                var names = graph.m_data?.m_stringData?.m_variableNames;
                var values = graph.m_data?.m_variableInitialValues?.m_wordVariableValues;
                var infos = graph.m_data?.m_variableInfos;
                if (names is null || values is null || infos is null) continue;

                for (int i = 0; i < names.Count; i++)
                {
                    if (!names[i].StartsWith("iState_", StringComparison.OrdinalIgnoreCase)) continue;

                    constants++;
                    if (infos[i].m_type == 3) integers++;
                    if (infos[i].m_role.m_role == 0) plainRole++;

                    lowest = Math.Min(lowest, values[i].m_value);
                    highest = Math.Max(highest, values[i].m_value);
                }
            }
        }

        Assert.Equal(145, constants);
        Assert.Equal(145, integers);
        Assert.Equal(145, plainRole);
        Assert.Equal(0, lowest);
        Assert.Equal(101, highest);
    }

    /// <summary>
    /// The ten creatures sharing one behaviour file are given a decade of keys
    /// each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>quadrupedbehavior.hkx</c> is shared by ten projects, and because a key is
    /// only meaningful against the graph reading it, their keys have to be kept
    /// apart by hand. They are, on a decade: bear 0, cow 10, deer 20, dog 30, goat
    /// 40, horker 50, mammoth 70, sabre cat 80, skeever 90, wolf 100 -- with the
    /// creature's second movement type at the decade plus one. 60 is skipped, and
    /// it is the horse's, which keeps its own file.
    /// </para>
    /// <para>
    /// So a key is not a small ordinal and a project's first key is not 0. Anything
    /// numbering a new creature's states from zero collides with the bear.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheSharedQuadrupedFileAllocatesADecadePerCreature()
    {
        var defaults = new Dictionary<string, int>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, _) in Load())
        {
            bool shared = walk.Steps.Any(s => Path.GetFileNameWithoutExtension(s.File)
                .Equals("quadrupedbehavior", StringComparison.OrdinalIgnoreCase));
            if (!shared) continue;

            var constants = StateConstants.Of(walk, root);
            defaults[name] = constants.First(c => c.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase) &&
                                                  !c.Key.Contains("Swim", StringComparison.OrdinalIgnoreCase) &&
                                                  !c.Key.Contains("Siwm", StringComparison.OrdinalIgnoreCase)).Value;
        }

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["BearProject"] = 0,
                ["HighlandCowProject"] = 10,
                ["DeerProject"] = 20,
                ["DogProject"] = 30,
                ["GoatProject"] = 40,
                ["HorkerProject"] = 50,
                ["MammothProject"] = 70,
                ["SabreCatProject"] = 80,
                ["SkeeverProject"] = 90,
                ["WolfProject"] = 100,
            },
            defaults);
    }

    /// <summary>
    /// The cow's swim state is numbered 0, which is the bear's default.
    /// </summary>
    /// <remarks>
    /// Every other creature in the shared file puts its second movement type at its
    /// own decade plus one -- the bear's swim is 1, the horker's 51, the skeever's
    /// 91. The cow's is 0, so in a file shared with the bear it names the bear's
    /// default. The variable is also misspelt <c>iState_CowSiwmDefault</c>, which
    /// suggests the same line was fumbled once.
    /// </para>
    /// <para>
    /// It does no harm in the shipped game because the cow ships a block only for
    /// key 10, so nothing ever samples the colliding key -- but it means a key is
    /// not unique across the creatures sharing a file, only within one.
    /// </remarks>
    [CorpusFact]
    public void TheCowsSwimStateCollidesWithTheBearsDefault()
    {
        var projects = Load();

        var cow = StateConstants.Of(
            projects.First(p => p.Name == "HighlandCowProject").Walk,
            projects.First(p => p.Name == "HighlandCowProject").Root);

        var bear = StateConstants.Of(
            projects.First(p => p.Name == "BearProject").Walk,
            projects.First(p => p.Name == "BearProject").Root);

        Assert.Equal(0, cow["iState_CowSiwmDefault"]);    // sic
        Assert.Equal(0, bear["iState_BearDefault"]);
        Assert.Equal(1, bear["iState_BearSwimDefault"]);  // where the cow's should have been, at 11

        // and the collision is harmless only because the cow never ships that block
        SkyrimCache cache = projects[0].Cache;
        var keys = cache.SpeedData!.Block("HighlandCowProject")!.Entries
            .Where(e => e.Records.Count > 0).Select(e => (int)e.Key).ToList();

        Assert.Equal([10], keys);
    }

    /// <summary>
    /// The falmer's running movement type has no block, which is why its walking
    /// one has to cover both gaits.
    /// </summary>
    /// <remarks>
    /// It declares four constants and ships two blocks. <c>iState_Falmer1HMWalk</c>
    /// is 2 and has one; <c>iState_Falmer1HMRun</c> is 3 and has none, and neither
    /// does <c>iState_FalmerDefault</c> at 0. So the gait-spanning records under key
    /// 2 are not an oddity of the sampler -- the run family was simply never
    /// sampled separately, and key 2 is the only place its speeds could go.
    /// </remarks>
    [CorpusFact]
    public void TheFalmerShipsNoBlockForItsRunningMovementType()
    {
        var projects = Load();
        (string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) =
            projects.First(p => p.Name == "FalmerProject");

        var constants = StateConstants.Of(walk, root);
        Assert.Equal(4, constants.Count);

        var keys = cache.SpeedData!.Block(name)!.Entries
            .Where(e => e.Records.Count > 0).Select(e => (int)e.Key).ToHashSet();

        Assert.Equal([1, 2], keys.Order());

        Assert.Contains(constants["iState_Falmer1HMWalk"], keys);      // 2
        Assert.DoesNotContain(constants["iState_Falmer1HMRun"], keys); // 3
        Assert.DoesNotContain(constants["iState_FalmerDefault"], keys); // 0
    }

    /// <summary>
    /// Every one of the 78 blocks resolves, key to constant to movement-type
    /// record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the chain the whole rebuild depends on, checked end to end with
    /// nothing assumed at either join:
    /// </para>
    /// <code>
    /// speed block key   ->   iState_&lt;name&gt; holding that value   ->   MOVT record named &lt;name&gt;
    ///              30   ->   iState_DogDefault = 30              ->   DogDefault, fwd 74.54/500.14
    ///               2   ->   iState_Falmer1HMWalk = 2            ->   Falmer1HMWalk, fwd 100.44/175.77
    /// </code>
    /// <para>
    /// 78 of 78 at both joins. The constant's stored value is the block's key
    /// exactly, and stripping <c>iState_</c> from the variable's name gives a
    /// movement type the masters actually carry -- so a block can always be taken
    /// back to the speeds the engine was asked for, which is what makes the
    /// movement types usable as an input rather than a guess.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void EveryBlockResolvesToAMovementTypeInTheMasters()
    {
        var movements = Masters.Read();
        int blocks = 0, keyed = 0, named = 0;
        var unresolved = new List<string>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            var constants = StateConstants.Of(walk, root);

            foreach (SpeedEntry entry in cache.SpeedData!.Block(name)!.Entries)
            {
                if (entry.Records.Count == 0) continue;

                blocks++;
                int key = (int)entry.Key;

                string? movement = StateConstants.MovementTypeOf(constants, key);
                if (movement is null) { unresolved.Add($"{name}: key {key} has no constant"); continue; }

                // the constant's stored value is the key, not merely near it
                Assert.Equal(key, constants["iState_" + movement]);
                keyed++;

                if (movements.ContainsKey(movement)) named++;
                else unresolved.Add($"{name}: key {key} names '{movement}', absent from the masters");
            }
        }

        Assert.Equal(78, blocks);
        Assert.Equal(78, keyed);
        Assert.Equal(78, named);
        Assert.Empty(unresolved);
    }

    /// <summary>Two of the joins, spelled out.</summary>
    [MastersFact]
    public void TheDogAndTheFalmerResolveToTheirStatedSpeeds()
    {
        var movements = Masters.Read();
        var projects = Load();

        (string _, ProjectWalk dogWalk, BehaviorRoot dogRoot, SkyrimCache cache) =
            projects.First(p => p.Name == "DogProject");

        var dog = StateConstants.Of(dogWalk, dogRoot);

        Assert.Equal("DogDefault", StateConstants.MovementTypeOf(dog, 30));
        Assert.Equal(74.54f, movements["DogDefault"].ForwardWalk, 2);

        // the dog's block really is keyed 30 and not 0
        Assert.Equal([30], cache.SpeedData!.Block("DogProject")!.Entries
            .Where(e => e.Records.Count > 0).Select(e => (int)e.Key));

        (string _, ProjectWalk falmerWalk, BehaviorRoot falmerRoot, _) =
            projects.First(p => p.Name == "FalmerProject");

        var falmer = StateConstants.Of(falmerWalk, falmerRoot);

        Assert.Equal("Falmer1HMWalk", StateConstants.MovementTypeOf(falmer, 2));
        Assert.Equal(100.44f, movements["Falmer1HMWalk"].ForwardWalk, 2);
        Assert.Equal(175.77f, movements["Falmer1HMWalk"].ForwardRun, 2);
    }

    /// <summary>
    /// A constant with no block never shares its value with a key of the same
    /// project, but very often with a key of another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Of the 67 constants with no block, <strong>none</strong> has a value that is
    /// also a key of its own project -- which is forced rather than lucky, since
    /// name to value is one to one within a project. So inside one project the
    /// question "is this number a block?" has a clean answer.
    /// </para>
    /// <para>
    /// Across projects it does not: 42 of the 67 hold a number that <em>is</em> a
    /// key somewhere else. The value 1 is a blockless second gait twelve times and
    /// a real key six times. A key is therefore only meaningful against the project
    /// that declared it, which is the same lesson as the shared quadruped file and
    /// the per-file variable indices.
    /// </para>
    /// <para>
    /// The two distributions also sit in different places. Keys pile up on 0 -- 30
    /// of the 78, each creature's default -- while the blockless ones pile up on 1,
    /// the secondary gait or stance: sprint, run, swim, combat, blocking. What
    /// Bethesda sampled is overwhelmingly the default movement type.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void ABlocklessConstantNeverClashesWithinItsProject()
    {
        var projects = Load();

        var everyKey = new HashSet<int>();
        var each = new List<(HashSet<int> Keys, IReadOnlyDictionary<string, int> Constants)>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in projects)
        {
            var keys = cache.SpeedData!.Block(name)!.Entries
                .Where(e => e.Records.Count > 0)
                .Select(e => (int)e.Key)
                .ToHashSet();

            each.Add((keys, StateConstants.Of(walk, root)));
            foreach (int key in keys) everyKey.Add(key);
        }

        int spare = 0, clashHere = 0, keyElsewhere = 0;
        var spareValues = new Dictionary<int, int>();
        var keyValues = new Dictionary<int, int>();

        foreach ((HashSet<int> keys, IReadOnlyDictionary<string, int> constants) in each)
            foreach (int value in constants.Values)
            {
                if (keys.Contains(value))
                {
                    keyValues.TryGetValue(value, out int used);
                    keyValues[value] = used + 1;
                    continue;
                }

                spare++;
                spareValues.TryGetValue(value, out int count);
                spareValues[value] = count + 1;

                if (keys.Contains(value)) clashHere++;
                if (everyKey.Contains(value)) keyElsewhere++;
            }

        Assert.Equal(67, spare);
        Assert.Equal(0, clashHere);       // never within a project
        Assert.Equal(42, keyElsewhere);   // but usually somewhere

        Assert.Equal(30, keyValues[0]);   // keys cluster on the default
        Assert.Equal(12, spareValues[1]); // blockless ones on the second gait
    }

    /// <summary>
    /// The falmer is the only creature that ships no block for its own default
    /// movement type.
    /// </summary>
    /// <remarks>
    /// 34 projects declare a plainly named <c>Default</c> constant and 33 of them
    /// ship a block for it. The falmer declares <c>iState_FalmerDefault = 0</c> and
    /// ships blocks for 1 and 2 only, so its default and its running movement type
    /// both go unsampled while a bow stance and a walking stance do not. Whatever
    /// swept these tables was driven by something other than the movement type
    /// list.
    /// </remarks>
    [CorpusFact]
    public void OnlyTheFalmerShipsNoBlockForItsDefault()
    {
        var missing = new List<string>();
        int withADefault = 0;

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            var defaults = StateConstants.Of(walk, root)
                .Where(c => c.Key.EndsWith("Default", StringComparison.OrdinalIgnoreCase) &&
                            !c.Key.Contains("Swim", StringComparison.OrdinalIgnoreCase) &&
                            !c.Key.Contains("Siwm", StringComparison.OrdinalIgnoreCase) &&
                            !c.Key.Contains("Run", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (defaults.Count == 0) continue;
            withADefault++;

            var keys = cache.SpeedData!.Block(name)!.Entries
                .Where(e => e.Records.Count > 0).Select(e => (int)e.Key).ToHashSet();

            foreach ((string constant, int value) in defaults)
                if (!keys.Contains(value)) missing.Add(name);
        }

        Assert.Equal(34, withADefault);
        Assert.Equal(["FalmerProject"], missing);
    }
}
