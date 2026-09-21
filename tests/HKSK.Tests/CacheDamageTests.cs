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
    /// The cache is the right input, for every creature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rung carries two numbers that ought to agree: the weight the author gave
    /// it, and the speed the clip beneath it actually travels at. Replacing the
    /// second with the first everywhere is a way of asking which one the shipped
    /// file followed, and over the 6140 points of the declared keys the answer is
    /// emphatic -- <strong>the cache holds 5536 and the weights 4045</strong>.
    /// <c>VampireLord</c> goes from 223 of its 223 points to 7 and
    /// <c>BallistaCenturion</c> from 224 to 14, which is section 0's whole claim
    /// made visible: the table is there because the two numbers differ.
    /// </para>
    /// <para>
    /// <strong>No creature prefers the weights.</strong> Two used to, the horse at 0
    /// against 213 and the werewolf at 28 against 67, and both were taken to have damaged
    /// root motion: clips delivering a constant multiple of their rungs per animation,
    /// arms recording no travel at all. Both caches are numbered against another
    /// character list -- the horse's past the end of its character's, the werewolf's at
    /// another animation's slot for every clip -- and both were read through the
    /// character. Read at the caches' own numbers (<see cref="ActorProject.MotionOf(HKX2.hkbClipGenerator)"/>)
    /// the horse holds all 289 of its points and the werewolf all 228.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void TheCacheDescribesTheShippedTableBetterThanTheWeights()
    {
        (int cacheHeld, int weightHeld, int all, var byProject) = Compare(mend: false);

        Assert.Equal(6140, all);
        Assert.Equal(5536, cacheHeld);
        Assert.Equal(4045, weightHeld);
        Assert.DoesNotContain(byProject, p => p.Value.Item2 > p.Value.Item1);

        Assert.Equal((289, 289), byProject["HorseProject"]);
        Assert.Equal((228, 172), byProject["WerewolfBeastProject"]);
        Assert.Equal((223, 7), byProject["VampireLord"]);
    }

    /// <summary>
    /// Damage cannot be told from the difference the table exists to record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A creature whose rung weights are its movement type's own speeds, to within a tenth
    /// of a percent, while the cache says the clip travels at something else entirely,
    /// looks identifiable from the inputs alone. Two inputs agreeing and the third
    /// contradicting both is a tempting rule. It was written for the horse and the
    /// werewolf, which turned out to be misread rather than damaged (above); read at their
    /// caches' own numbers there is nothing left in either to mend.
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

        Assert.Equal((289, 289), byProject["HorseProject"]);
        Assert.Equal((228, 228), byProject["WerewolfBeastProject"]);
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
