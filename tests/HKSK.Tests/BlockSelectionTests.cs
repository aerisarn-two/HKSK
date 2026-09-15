using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What decides which movement types get a speed block, and what does not.
/// </summary>
/// <remarks>
/// A project declares <c>iState_&lt;movement type&gt;</c> constants for more movement
/// types than its speed table has blocks for -- 145 against 78. The obvious
/// question is what separates them, and the answer is that nothing in the three
/// admissible inputs does, which these establish by counterexample rather than by
/// failing to find one.
/// </remarks>
public sealed class BlockSelectionTests
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

    private static HashSet<int> KeysOf(SkyrimCache cache, string project) =>
        cache.SpeedData!.Block(project)!.Entries
            .Where(e => e.Records.Count > 0)
            .Select(e => (int)e.Key)
            .ToHashSet();

    /// <summary>
    /// Two projects share one behaviour file, one movement-type list and one set of
    /// constants, and ship different blocks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>DraugrProject</c> and <c>DraugrSkeletonProject</c> both root at
    /// <c>actors/draugr/behaviors/draugrbehavior.hkx</c> -- the same file on disk,
    /// not a copy -- and so declare the same twelve constants naming the same
    /// twelve movement types. The draugr ships six blocks and the skeleton one.
    /// </para>
    /// <para>
    /// So which movement types get a block is <strong>not a function</strong> of
    /// the behaviour, the constants or the movement types: here are two projects
    /// agreeing on every one of those and disagreeing on the answer. It is an
    /// authoring decision recorded nowhere but in the speed table itself, which
    /// means a rebuild can reproduce a block it is given but cannot be asked which
    /// blocks to write.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TwoProjectsWithIdenticalInputsShipDifferentBlocks()
    {
        var projects = Load();

        (string _, ProjectWalk draugrWalk, BehaviorRoot draugrRoot, SkyrimCache cache) =
            projects.First(p => p.Name == "DraugrProject");
        (string _, ProjectWalk skeletonWalk, BehaviorRoot skeletonRoot, _) =
            projects.First(p => p.Name == "DraugrSkeletonProject");

        // literally the same file
        Assert.Equal(
            Path.GetFullPath(draugrRoot.BehaviorFile),
            Path.GetFullPath(skeletonRoot.BehaviorFile),
            ignoreCase: true);

        var draugr = StateConstants.Of(draugrWalk, draugrRoot);
        var skeleton = StateConstants.Of(skeletonWalk, skeletonRoot);

        Assert.Equal(12, draugr.Count);
        Assert.Equal(draugr.OrderBy(c => c.Key), skeleton.OrderBy(c => c.Key));

        // and different answers
        Assert.Equal([0, 3, 4, 5, 6, 7], KeysOf(cache, "DraugrProject").Order());
        Assert.Equal([0], KeysOf(cache, "DraugrSkeletonProject").Order());
    }

    /// <summary>
    /// Having a movement-type record does not separate them either.
    /// </summary>
    /// <remarks>
    /// All 78 blocked constants name a movement type the masters carry, and so do
    /// 64 of the 67 unblocked ones. Only three name nothing: the cow's misspelt
    /// <c>CowSiwmDefault</c> and <c>CombatSpider_MT</c> in the two spider projects.
    /// A MOVT record is necessary and nowhere near sufficient.
    /// </remarks>
    [MastersFact]
    public void AMovementTypeRecordDoesNotSeparateThem()
    {
        var movements = Masters.Read();
        int blocked = 0, blockedNamed = 0, unblocked = 0, unblockedNamed = 0;
        var nameless = new List<string>();

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            HashSet<int> keys = KeysOf(cache, name);

            foreach ((string constant, int value) in StateConstants.Of(walk, root))
            {
                string movement = constant["iState_".Length..];
                bool known = movements.ContainsKey(movement);

                if (keys.Contains(value))
                {
                    blocked++;
                    if (known) blockedNamed++;
                }
                else
                {
                    unblocked++;
                    if (known) unblockedNamed++; else nameless.Add(movement);
                }
            }
        }

        Assert.Equal(78, blocked);
        Assert.Equal(78, blockedNamed);      // every block names a real movement type
        Assert.Equal(67, unblocked);
        Assert.Equal(64, unblockedNamed);    // and so does almost every non-block

        Assert.Equal(["CombatSpider_MT", "CombatSpider_MT", "CowSiwmDefault"], nameless.Order());
    }

    /// <summary>
    /// Nor does the movement type merely repeating one that is already blocked.
    /// </summary>
    /// <remarks>
    /// A plausible rule -- skip a movement type whose eight speeds duplicate one
    /// already sampled -- accounts for 11 of the 67. The other 53 ask for speeds no
    /// blocked type in their project asks for, including the falmer's own default
    /// at 100.44/361.24 and <c>NPCAttacking</c> at 80.1/288.
    /// </remarks>
    [MastersFact]
    public void NorDoesDuplicatingAnAlreadyBlockedMovementType()
    {
        var movements = Masters.Read();
        int unblocked = 0, duplicates = 0, distinct = 0, nameless = 0;

        foreach ((string name, ProjectWalk walk, BehaviorRoot root, SkyrimCache cache) in Load())
        {
            HashSet<int> keys = KeysOf(cache, name);
            var constants = StateConstants.Of(walk, root);

            List<MovementType> blocked = [.. constants
                .Where(c => keys.Contains(c.Value))
                .Select(c => movements.GetValueOrDefault(c.Key["iState_".Length..]))];

            foreach ((string constant, int value) in constants)
            {
                if (keys.Contains(value)) continue;

                unblocked++;
                if (!movements.TryGetValue(constant["iState_".Length..], out MovementType mine)) { nameless++; continue; }

                if (blocked.Any(b => Same(b, mine))) duplicates++; else distinct++;
            }
        }

        Assert.Equal(67, unblocked);
        Assert.Equal(3, nameless);
        Assert.Equal(11, duplicates);
        Assert.Equal(53, distinct);
    }

    /// <summary>
    /// Where both draugr projects ship a block, the two are byte for byte the
    /// same.
    /// </summary>
    /// <remarks>
    /// The one key they share, 0, carries 19 records and 192 points in each, with
    /// the same headings, the same point counts and the same values throughout. So
    /// the sweep is deterministic: identical inputs give an identical block, and
    /// what differs between these two projects is only <em>which</em> keys were
    /// swept, never what the sweep produced.
    /// </remarks>
    [CorpusFact]
    public void WhereBothDraugrProjectsShipABlockTheBlocksAreIdentical()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        SpeedEntry draugr = cache.SpeedData!.Block("DraugrProject")!.Entries.First(e => e.Key == 0);
        SpeedEntry skeleton = cache.SpeedData.Block("DraugrSkeletonProject")!.Entries.First(e => e.Key == 0);

        Assert.Equal(19, draugr.Records.Count);
        Assert.Equal(19, skeleton.Records.Count);
        Assert.Equal(192, draugr.Records.Sum(r => r.Points.Count));

        foreach ((SpeedRecord a, SpeedRecord b) in draugr.Records.Zip(skeleton.Records))
        {
            Assert.Equal(a.Direction, b.Direction);
            Assert.Equal(a.Points, b.Points);
        }
    }

    /// <summary>
    /// The player's two sexes share a graph and differ in one block, and the
    /// animation cache says exactly why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 14 keys each, 13 blocks identical, and key 0 -- the default movement type --
    /// differing at 226 points, 2% at the median and 14.4% at the worst. The error
    /// is nothing at all straight forward and largest straight backward.
    /// </para>
    /// <para>
    /// That is not a mystery and needs no input beyond the three: 43 of the
    /// player's cached clip motions differ between the sexes, and the backward
    /// locomotion clips are among them. <c>MT_WalkBackward</c> travels 83.92 for the
    /// male and 93.43 for the female over the same 1.1333 seconds, so the rung
    /// speeds are 74.05 and 82.44 -- a ratio of 1.1133. The shipped tables at that
    /// heading are 73.19 and 81.49, which is the same ratio to four figures.
    /// </para>
    /// <para>
    /// Both halves of the speed are cached: <c>ClipMovement.Duration</c> beside the
    /// translation keys. So the root motion in the cache <em>is</em> sufficient, and
    /// the difference between the two tables is fully accounted for by inputs the
    /// rebuild already has.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void ThePlayersSexesDifferInOneBlockAndTheCacheSaysWhy()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        ActorProject male = cache.OpenActor("DefaultMale")!;
        ActorProject female = cache.OpenActor("DefaultFemale")!;

        // the cached motion is not the same: duration and travel both move
        int differing = 0;
        for (int i = 0; i < male.Animations.Count; i++)
        {
            ClipMovement? a = male.Animations[i].Motion, b = female.Animations[i].Motion;
            if (a is null || b is null) continue;

            if (a.Duration != b.Duration ||
                a.Translations.Count != b.Translations.Count ||
                !a.Translations.Zip(b.Translations).All(p => p.First == p.Second))
                differing++;
        }

        Assert.Equal(43, differing);

        // the backward walk, which is where the tables disagree most
        ClipMovement mw = male.Animation("MT_WalkBackward")!.Motion!;
        ClipMovement fw = female.Animation("MT_WalkBackward")!.Motion!;

        Assert.Equal(mw.Duration, fw.Duration, 4);          // same length
        Assert.NotEqual(mw.Travel, fw.Travel);              // different distance

        double predicted = (fw.Travel / fw.Duration) / (mw.Travel / mw.Duration);

        // and the shipped tables move by that ratio at that heading
        SpeedEntry m = cache.SpeedData!.Block("DefaultMale")!.Entries.First(e => e.Key == 0);
        SpeedEntry f = cache.SpeedData.Block("DefaultFemale")!.Entries.First(e => e.Key == 0);

        SpeedRecord back = m.Records.First(r => MathF.Abs(r.Direction - 0.5f) < 0.01f);
        SpeedRecord backF = f.Records.First(r => MathF.Abs(r.Direction - 0.5f) < 0.01f);

        SpeedPoint pm = back.Points.First(p => MathF.Abs(p.X - 74f) < 0.01f);
        SpeedPoint pf = backF.Points[back.Points.IndexOf(pm)];

        double observed = pf.Y / pm.Y;

        Assert.Equal(predicted, observed, 3);
    }

    private static bool Same(MovementType a, MovementType b) =>
        a.ForwardWalk == b.ForwardWalk && a.ForwardRun == b.ForwardRun &&
        a.BackWalk == b.BackWalk && a.BackRun == b.BackRun &&
        a.LeftWalk == b.LeftWalk && a.LeftRun == b.LeftRun &&
        a.RightWalk == b.RightWalk && a.RightRun == b.RightRun;
}
