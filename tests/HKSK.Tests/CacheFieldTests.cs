using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What each field of a cache entry actually holds, checked against the game.
/// </summary>
/// <remarks>
/// The format is undocumented and the field names came from earlier tools, so a
/// name is a hypothesis rather than a definition. These pin the ones that were
/// checked -- including two the shipped data contradicted, which had been
/// documented from the names alone.
/// </remarks>
public class CacheFieldTests
{
    /// <summary>
    /// A movement block's duration is the time of its last key.
    /// </summary>
    /// <remarks>
    /// Not the animation's duration, though it usually equals it: of the 6,674
    /// blocks whose animation could be read, all match the last key and 98.5%
    /// also match the animation.
    /// </remarks>
    [CorpusFact]
    public void MovementDurationIsTheTimeOfTheLastKey()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int checkedBlocks = 0;

        foreach (AnimationDataProject project in cache.AnimationData.Projects)
            foreach (ClipMovement movement in project.Movements?.Movements ?? [])
            {
                float last = Math.Max(
                    movement.Translations.Count > 0 ? movement.Translations[^1].Time : 0f,
                    movement.Rotations.Count > 0 ? movement.Rotations[^1].Time : 0f);

                Assert.Equal(last, movement.Duration, 3);
                checkedBlocks++;
            }

        Assert.Equal(6725, checkedBlocks);
    }

    /// <summary>
    /// The motion curve starts at the origin implicitly: nothing keys t=0.
    /// </summary>
    [CorpusFact]
    public void NoMovementBlockCarriesAKeyAtTimeZero()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int single = 0, total = 0;

        foreach (AnimationDataProject project in cache.AnimationData.Projects)
            foreach (ClipMovement movement in project.Movements?.Movements ?? [])
            {
                Assert.All(movement.Translations, t => Assert.True(t.Time > 0f));
                Assert.All(movement.Rotations, r => Assert.True(r.Time > 0f));

                total++;
                if (movement.Translations.Count == 1) single++;
            }

        // Most carry one key holding the whole displacement.
        Assert.Equal(6725, total);
        Assert.Equal(5769, single);
    }

    /// <summary>
    /// The event separator is unambiguous: no event name contains a colon.
    /// </summary>
    /// <remarks>
    /// Which is why <c>name:time</c> can be split at all. Names do carry dots --
    /// 3,941 of them -- because that is the behaviour's payload separator.
    /// </remarks>
    [CorpusFact]
    public void NoEventNameContainsTheSeparator()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var events = cache.AnimationData.Projects
            .SelectMany(p => p.Block.Clips)
            .SelectMany(c => c.Events)
            .ToList();

        Assert.Equal(36584, events.Count);
        Assert.DoesNotContain(events, e => e.Name.Contains(':'));
        Assert.Equal(3941, events.Count(e => e.Name.Contains('.')));
    }

    [CorpusFact]
    public void EverySetDeclaresVersionThreeAndMirroredIsAFlag()
    {
        var sets = Sets().ToList();

        Assert.Equal(990, sets.Count);
        Assert.All(sets, s => Assert.Equal("V3", s.Version));

        var mirrored = sets.SelectMany(s => s.Attacks.Attacks).Select(a => a.Mirrored).Distinct().Order();
        Assert.Equal([0, 1], mirrored);
    }

    /// <summary>
    /// Hand variables do not say whether a set attacks -- the correction this
    /// audit produced.
    /// </summary>
    /// <remarks>
    /// The name invites the reading that a set with hand variables is an attack
    /// set. It is not: 40 sets carry attacks with no hand variables, and 68
    /// carry hand variables with no attacks. The two are independent.
    /// </remarks>
    [CorpusFact]
    public void HandVariablesDoNotPredictAttacks()
    {
        var sets = Sets().ToList();

        Assert.Equal(40, sets.Count(s => s.HandVariables.Variables.Count == 0 && s.Attacks.Attacks.Count > 0));
        Assert.Equal(68, sets.Count(s => s.HandVariables.Variables.Count > 0 && s.Attacks.Attacks.Count == 0));
    }

    /// <summary>
    /// The variable list is not limited to hands, and the hand types run past
    /// the enum earlier tools documented.
    /// </summary>
    [CorpusFact]
    public void TheVariableListHoldsMoreThanHandTypes()
    {
        var variables = Sets().SelectMany(s => s.HandVariables.Variables).ToList();

        Assert.Equal(386, variables.Count);
        Assert.All(variables, v => Assert.True(v.Min <= v.Max));

        var names = variables.Select(v => v.Name).Distinct().Order().ToList();
        Assert.Contains("iRightHandType", names);
        Assert.Contains("iWantMountedWeaponAnims", names);

        // ck-cmd's equipped-type enum stops at 11. The game uses 12.
        var handTypes = variables
            .Where(v => v.Name.EndsWith("HandType", StringComparison.Ordinal))
            .SelectMany(v => new[] { v.Min, v.Max });

        Assert.Equal(12, handTypes.Max());
    }

    /// <summary>
    /// A set names its animations by checksum, in whole triples, and may name none.
    /// </summary>
    [CorpusFact]
    public void ChecksumBlocksAreWholeTriplesAndMayBeEmpty()
    {
        var sets = Sets().ToList();

        Assert.All(sets, s => Assert.Equal(0, s.Checksums.Entries.Count % 3));
        Assert.Equal(77, sets.Count(s => s.Checksums.Entries.Count == 0));
    }

    /// <summary>Every set file listed has a block, which is what delimits them.</summary>
    [CorpusFact]
    public void EverySetFileHasExactlyOneBlock()
    {
        AnimationSetDataFile sets = AnimationSetDataFile.Load(Corpus.AnimationSetData);

        Assert.All(sets.Projects, p => Assert.Equal(p.Sets.SetFiles.Count, p.Sets.Sets.Count));
    }

    /// <summary>
    /// The set data covers exactly the projects that carry a clip cache.
    /// </summary>
    /// <remarks>
    /// Not a subset and not an overlap: the same 49 projects on both sides. A
    /// project has animation set data if and only if it has an animation cache,
    /// and everything else in the animation data -- props, furniture, traps --
    /// is in neither.
    /// </remarks>
    [CorpusFact]
    public void TheSetDataCoversExactlyTheCachedProjects()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var cached = cache.AnimationData.Projects
            .Where(p => p.Block.HasAnimationCache)
            .Select(p => p.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var withSets = cache.SetData.Projects
            .Select(p => p.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(49, cached.Count);
        Assert.True(cached.SetEquals(withSets),
            $"only cached: [{string.Join(", ", cached.Except(withSets))}]; " +
            $"only sets: [{string.Join(", ", withSets.Except(cached))}]");
    }

    /// <summary>The set data's key names the project twice, in a fixed shape.</summary>
    [CorpusFact]
    public void EverySetDataKeyIsProjectDataBackslashProjectTxt()
    {
        AnimationSetDataFile sets = AnimationSetDataFile.Load(Corpus.AnimationSetData);

        Assert.All(sets.Projects, project =>
        {
            string[] parts = project.Name.Split('\\');

            Assert.Equal(2, parts.Length);
            Assert.EndsWith("Data", parts[0], StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".txt", parts[1], StringComparison.OrdinalIgnoreCase);
            Assert.Equal(parts[0][..^4], parts[1][..^4], ignoreCase: true);
        });
    }

    /// <summary>
    /// The names in the set data resolve against the behaviour graphs and the
    /// clip list, which is what says they are what they look like.
    /// </summary>
    /// <remarks>
    /// A name is only a hypothesis until it resolves somewhere. Hand variables
    /// resolve completely; the rest resolve but for Dawnguard and Dragonborn
    /// additions the base graphs never gained, which is the same drift the cache
    /// indices show.
    /// </remarks>
    [CorpusFact]
    public void TheNamesInTheSetDataResolveAgainstTheBehaviours()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int swapTotal = 0, swapKnown = 0;
        int eventTotal = 0, eventKnown = 0;
        int clipTotal = 0, clipKnown = 0;
        int variableTotal = 0, variableKnown = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (project.Sets is null || !project.HasHavok) continue;

            var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Havok.BehaviorFile behavior in project.Behaviors)
            {
                HKX2.hkbBehaviorGraphStringData? strings =
                    behavior.File.First<HKX2.hkbBehaviorGraphStringData>();

                if (strings is null) continue;

                foreach (string name in strings.m_eventNames) events.Add(name);
                foreach (string name in strings.m_variableNames) variables.Add(name);
            }

            var clips = project.Data.Block.Clips
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (ProjectAttackBlock set in project.Sets.Sets.Sets)
            {
                foreach (string swap in set.SwapEvents)
                {
                    swapTotal++;
                    if (events.Contains(swap)) swapKnown++;
                }

                foreach (AttackData attack in set.Attacks.Attacks)
                {
                    eventTotal++;
                    if (events.Contains(attack.EventName)) eventKnown++;

                    foreach (string clip in attack.Clips)
                    {
                        clipTotal++;
                        if (clips.Contains(clip)) clipKnown++;
                    }
                }

                foreach (HandVariable variable in set.HandVariables.Variables)
                {
                    variableTotal++;
                    if (variables.Contains(variable.Name)) variableKnown++;
                }
            }
        }

        // Behaviour variables, exactly.
        Assert.Equal(386, variableTotal);
        Assert.Equal(386, variableKnown);

        // The rest resolve but for the DLC drift.
        Assert.Equal(1921, swapTotal);
        Assert.Equal(1910, swapKnown);

        Assert.Equal(737, eventTotal);
        Assert.Equal(735, eventKnown);

        Assert.Equal(793, clipTotal);
        Assert.Equal(776, clipKnown);
    }

    private static IEnumerable<ProjectAttackBlock> Sets() =>
        AnimationSetDataFile.Load(Corpus.AnimationSetData).Projects.SelectMany(p => p.Sets.Sets);
}
