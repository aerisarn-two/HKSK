using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Whether the animation cache or the authored rung weights describe the shipped
/// table better, on the keys the graph declares.
/// </summary>
public sealed class CacheDamageTests
{
    /// <summary>
    /// The cache is the right input, and two creatures are the exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rung carries two numbers that ought to agree: the weight the author gave
    /// it, and the speed the clip beneath it actually travels at. Replacing the
    /// second with the first everywhere is a way of asking which one the shipped
    /// file followed, and over the 6140 points of the declared keys the answer is
    /// emphatic -- <strong>the cache holds 5047 and the weights 3864</strong>.
    /// <c>VampireLord</c> goes from 223 of its 223 points to 7 and
    /// <c>BallistaCenturion</c> from 224 to 14, which is section 0's whole claim
    /// made visible: the table is there because the two numbers differ.
    /// </para>
    /// <para>
    /// <strong>Two creatures prefer the weights, and both have damaged root
    /// motion.</strong> <c>HorseProject</c> goes from 0 to 213 and
    /// <c>WerewolfBeastProject</c> from 28 to 67. The werewolf is the horse's case
    /// again: its forward clips deliver 0.9 and 197.15 where the rungs say 5 and
    /// 303.04 -- a constant 5.55 and 1.537, per animation rather than per rung --
    /// while its sideways arms deliver their weights to four figures, and four of
    /// its arms record no travel at all.
    /// </para>
    /// <para>
    /// So 491 of the points still missing are bounded by the cache and not by the
    /// model. That is not a licence to mend them: see
    /// <see cref="TheMastersCannotTellDamageFromTheDifferenceTheTableRecords"/>.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheCacheDescribesTheShippedTableBetterThanTheWeights()
    {
        (int cacheHeld, int weightHeld, int all, var byProject) = Compare(mend: false);

        Assert.Equal(6140, all);
        Assert.Equal(5047, cacheHeld);
        Assert.Equal(3864, weightHeld);

        Assert.Equal((0, 213), byProject["HorseProject"]);
        Assert.Equal((28, 67), byProject["WerewolfBeastProject"]);
        Assert.Equal((223, 7), byProject["VampireLord"]);
    }

    /// <summary>
    /// Damage cannot be told from the difference the table exists to record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The horse and the werewolf look identifiable from the inputs alone: their
    /// rung weights are their movement type's own speeds, to within a tenth of a
    /// percent, while the cache says the clip travels at something else entirely.
    /// Two inputs agreeing and the third contradicting both is a tempting rule, and
    /// mending only those rungs does help them -- the horse by 57 and the werewolf
    /// by 8.
    /// </para>
    /// <para>
    /// <strong><c>VampireLord</c> refutes it, 223 points to 90.</strong> Its rungs
    /// are authored at its movement type's speeds too, and its clips genuinely
    /// deliver something else, and the shipped table faithfully records the
    /// something else. The signature of a damaged cache and the signature of a
    /// creature the speed table was written for are the same signature.
    /// </para>
    /// <para>
    /// The rule is a net loss across the corpus and is deliberately not in the
    /// rebuild. It is kept here because the refutation is the point.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheMastersCannotTellDamageFromTheDifferenceTheTableRecords()
    {
        (int cacheHeld, int mendedHeld, _, var byProject) = Compare(mend: true);

        Assert.True(mendedHeld < cacheHeld, $"mending held {mendedHeld} against {cacheHeld}");

        Assert.Equal((0, 57), byProject["HorseProject"]);
        Assert.Equal((28, 36), byProject["WerewolfBeastProject"]);
        Assert.Equal((223, 90), byProject["VampireLord"]);
    }

    private static (int Cache, int Other, int All, Dictionary<string, (int, int)> ByProject) Compare(bool mend)
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var movements = Masters.Read();
        int cacheHeld = 0, otherHeld = 0, all = 0;
        Dictionary<string, (int, int)> byProject = [];

        foreach (CacheProject project in cache.OpenAll())
        {
            if (project is not ActorProject actor) continue;
            if (cache.SpeedData!.Block(project.Name) is not { } theirs) continue;
            if (cache.FindProjectFile(project.Name) is not { } path) continue;
            if (BehaviorRoot.Of(path) is not { } root) continue;

            ProjectWalk walk = ProjectWalk.Of(path);
            var built = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
                .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
                .Where(st => st.Arms.Count > 0)
                .ToList();

            var constants = StateConstants.Of(walk, root);
            int pCache = 0, pOther = 0, pAll = 0;

            foreach (SpeedEntry yours in theirs.Entries)
            {
                if (yours.Records.Count == 0) continue;
                if (built.FirstOrDefault(b => b.State.Key == (int)yours.Key).Arms is not { } arms) continue;

                string? name = StateConstants.MovementTypeOf(constants, (int)yours.Key);
                MovementType? type = name is not null && movements.TryGetValue(name, out MovementType mt) ? mt : null;

                var other = arms
                    .Select(a => (a.Direction, Ladder: mend ? Mend(a.Ladder, a.Direction, type) : Rescale(a.Ladder)))
                    .ToList();

                foreach (SpeedRecord rec in yours.Records)
                    foreach (SpeedPoint p in rec.Points)
                    {
                        if (p.Y <= 0f) continue;
                        pAll++;
                        double c = SpeedSampler.Sample(arms, rec.Direction, p.X - SpeedLadder.SamplerOffset);
                        double w = SpeedSampler.Sample(other, rec.Direction, p.X - SpeedLadder.SamplerOffset);
                        if (c > 0 && Math.Abs(c - p.Y) / p.Y <= 0.02) pCache++;
                        if (w > 0 && Math.Abs(w - p.Y) / p.Y <= 0.02) pOther++;
                    }
            }

            if (pAll == 0) continue;
            all += pAll; cacheHeld += pCache; otherHeld += pOther;
            byProject[project.Name] = (pCache, pOther);
        }

        return (cacheHeld, otherHeld, all, byProject);
    }

    /// <summary>Every rung made to deliver its own weight, heading kept.</summary>
    private static SpeedLadder Rescale(SpeedLadder ladder) =>
        new(ladder.Rungs.Select(r =>
            r.Delivered <= 0f || r.Weight <= 0f ? r : r with { Travel = r.Travel * (r.Weight / r.Delivered) }))
        { Name = ladder.Name, Synchronised = ladder.Synchronised };

    /// <summary>Only the rungs authored at the movement type's own speeds.</summary>
    private static SpeedLadder Mend(SpeedLadder ladder, float heading, MovementType? movement)
    {
        if (movement is not { } type) return ladder;
        (float walking, float running) = type.At(heading);

        return new SpeedLadder(ladder.Rungs.Select(r =>
        {
            if (!Near(r.Weight, walking) && !Near(r.Weight, running)) return r;
            if (r.Delivered <= 0f) return r;
            if (MathF.Abs(r.Delivered - r.Weight) <= 0.05f * r.Weight) return r;

            return r with { Travel = r.Travel * (r.Weight / r.Delivered) };
        }))
        { Name = ladder.Name, Synchronised = ladder.Synchronised };
    }

    private static bool Near(float a, float b) => b > 0f && MathF.Abs(a - b) <= 0.005f * b;
}
