using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Rebuilding the shipped curves from the graph alone, with no names read and
/// nothing matched by guesswork.
/// </summary>
/// <remarks>
/// <para>
/// The whole chain in one place, every link established earlier in its own test:
/// locate the project, follow it to its root behaviour, visit across files, find
/// the generator carrying the speed sampler, follow the sampler's output variable
/// to the blends that read it, group those into the state that holds them, take
/// the <c>iState</c> the state is tagged with, look up the block that key names,
/// and compare.
/// </para>
/// <para>
/// Everything comes from one visit of one object graph. The earlier
/// <c>SpeedSampler</c> path reached more keys but had to guess which family served
/// which state from node names; this reaches fewer and guesses nothing.
/// </para>
/// </remarks>
public sealed class StructuralRebuildTests
{
    private const double Tolerance = 0.02;

    /// <summary>A key, the state serving it, and how its curves came out.</summary>
    private readonly record struct Rebuilt(string Project, int Key, bool ByTag, int Held, int Total);

    private static List<Rebuilt> Rebuild(SkyrimCache cache, out int keys, out int unmapped)
    {
        var done = new List<Rebuilt>();
        keys = 0;
        unmapped = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ActorProject project = cache.OpenActor(at.Name)!;
            ProjectWalk walk = ProjectWalk.Of(at.ProjectFile!);
            if (!Locomotion.RootsIn(walk.Steps).Any()) continue;

            var variables = new ProjectVariables(walk.Steps);
            List<LocomotionState> states = [.. LocomotionStates.In(walk, variables)];

            List<SpeedEntry> entries = [.. cache.SpeedData!.Block(at.Name)!.Entries.Where(e => e.Records.Count > 0)];

            foreach (SpeedEntry entry in entries)
            {
                keys++;
                int key = (int)entry.Key;

                // the state serving this key: its iState tag, or the only one there is
                LocomotionState? chosen = null;
                bool byTag = false;

                foreach (LocomotionState state in states)
                    if (state.Key == key) { chosen = state; byTag = true; break; }

                if (chosen is null && entries.Count == 1 && states.Count == 1) chosen = states[0];
                if (chosen is null || chosen.Value.Blends.Count == 0) { unmapped++; continue; }

                // the compass is the blender above that state's arms, in this same graph
                hkbBlenderGenerator? compass = walk.Nearest<hkbBlenderGenerator>(chosen.Value.Blends[0].Node);
                if (compass is null) { unmapped++; continue; }

                var arms = new List<(float Direction, SpeedLadder Ladder)>();
                foreach (hkbBlenderGeneratorChild? child in compass.m_children ?? [])
                    if (child?.m_generator is hkbBlenderGenerator arm)
                        arms.Add((child.m_weight, SpeedLadder.FromBlender(arm, project)));

                if (arms.Count == 0) { unmapped++; continue; }

                int held = 0;
                foreach (SpeedRecord record in entry.Records)
                {
                    bool ok = true;
                    foreach (SpeedPoint point in record.Points)
                    {
                        if (point.Y <= 0f) continue;

                        double value = SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset);
                        if (Math.Abs(value - point.Y) / point.Y > Tolerance) { ok = false; break; }
                    }

                    if (ok) held++;
                }

                done.Add(new Rebuilt(at.Name, key, byTag, held, entry.Records.Count));
            }
        }

        return done;
    }

    /// <summary>
    /// Where the graph says which state serves a key, the rebuilt curves match the
    /// shipped ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 18 of the 78 blocks can be reached without reading a single name -- 12
    /// because a <c>BSiStateTaggingGenerator</c> tags their state with the key, 6
    /// because the project has one block and one locomotion state and there is
    /// nothing to choose between. Rebuilt from the tagged state's compass and the
    /// root motion under it, <strong>298 of their 342 curves are within 2% at every
    /// point</strong>.
    /// </para>
    /// <para>
    /// Ten of the eighteen are exact end to end: the chaurus, the steam and
    /// ballista centurions, the sphere centurion, and the player's sneak, blocking
    /// and magic-casting keys.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheGraphAloneRebuildsTheCurvesItCanReach()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<Rebuilt> done = Rebuild(cache, out int keys, out _);

        Assert.Equal(78, keys);
        Assert.Equal(18, done.Count);
        Assert.Equal(12, done.Count(r => r.ByTag));

        Assert.Equal(342, done.Sum(r => r.Total));
        Assert.Equal(298, done.Sum(r => r.Held));

        // ten blocks come back whole
        Assert.Equal(10, done.Count(r => r.Held == r.Total));
    }

    /// <summary>
    /// Setting aside the one creature already known to be wrong, it is 92%.
    /// </summary>
    /// <remarks>
    /// <c>HMDaedra</c> fails all 19 of its curves and has done since section 9:
    /// every point of its table is exactly half what its graph produces, and
    /// duration, playback speed, the sync flag, the cyclic wrap, the compass
    /// geometry and the skeleton scale have each been eliminated. It is not a
    /// failure of this path. Without it, 298 of 323 hold.
    /// </remarks>
    [CorpusFact]
    public void WithoutTheKnownHalvingItIsNinetyTwoPercent()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<Rebuilt> done = Rebuild(cache, out _, out _);

        Rebuilt daedra = Assert.Single(done, r => r.Project == "HMDaedra");
        Assert.Equal(0, daedra.Held);
        Assert.Equal(19, daedra.Total);

        List<Rebuilt> rest = [.. done.Where(r => r.Project != "HMDaedra")];

        Assert.Equal(323, rest.Sum(r => r.Total));
        Assert.Equal(298, rest.Sum(r => r.Held));
    }

    /// <summary>
    /// Most blocks cannot be reached this way, and the reason is structural.
    /// </summary>
    /// <remarks>
    /// 60 of the 78 are left. A project with several blocks and several locomotion
    /// states needs something to pair them, and for the 30 projects whose graph
    /// never writes <c>iState</c> there is nothing in the behaviour that does --
    /// the game writes it from the movement type. That is the boundary of what the
    /// graph alone can say, and it is why the movement types are an input rather
    /// than a convenience.
    /// </remarks>
    [CorpusFact]
    public void SixtyBlocksCannotBeReachedFromTheGraphAlone()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Rebuild(cache, out int keys, out int unmapped);

        Assert.Equal(78, keys);
        Assert.Equal(60, unmapped);
    }
}
