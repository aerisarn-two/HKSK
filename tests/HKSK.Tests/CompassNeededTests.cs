using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Whether a block's records really vary with the heading, or only the forward
/// arm is ever heard from.
/// </summary>
public sealed class CompassNeededTests
{
    /// <summary>
    /// The compass earns its place everywhere but one block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A heading picks an arm and the arms are blended as vectors, which is most of
    /// what <see cref="SpeedSampler"/> does. Answering every heading with the
    /// forward arm instead asks whether that machinery is needed, and over the 5851
    /// points of the declared keys that have a compass the answer is emphatic:
    /// <strong>the compass holds 5247 and the forward arm alone 1126.</strong>
    /// </para>
    /// <para>
    /// <strong>One block is the other way round, and completely so.</strong>
    /// <c>BenthicLurkerProject</c> key 0 holds 23 of its 253 points through the
    /// compass and <strong>all 253</strong> through its forward arm -- every record,
    /// at every heading, is the forward walk ladder to four figures. Its sideways
    /// record climbs to 232.09, which is the forward ladder's top rung and well past
    /// its own 99.52, so at speed the creature turns to face rather than strafing.
    /// </para>
    /// <para>
    /// Nothing found in the inputs asks for that. Its movement type gives it real
    /// lateral speeds, 99.52 either way, matching the strafe rungs exactly; its
    /// graph carries eight directional blends like any other creature's; and its
    /// own key 1 still prefers the compass, 78 points against 30, so it is not even
    /// a property of the creature. Applying it would be reading the answer.
    /// </para>
    /// <para>
    /// <c>FirstPerson</c> is the second and last instance, and it is not counted
    /// here only because its key is reached by running the graph rather than
    /// declared. Its block is the same shape and more extreme:
    /// <see cref="FirstPersonIgnoresItsHeadingToo"/>.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void OnlyTheLurkersFirstBlockIgnoresItsHeading()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int compass = 0, ahead = 0, all = 0;
        Dictionary<string, (int Compass, int Ahead, int All)> byBlock = [];

        foreach (CacheProject project in cache.OpenAll())
        {
            if (project is not ActorProject actor) continue;
            if (cache.SpeedData!.Block(project.Name) is not { } block) continue;
            if (cache.FindProjectFile(project.Name) is not { } path) continue;

            ProjectWalk walk = ProjectWalk.Of(path);
            var built = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
                .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
                .Where(st => st.Arms.Count > 0)
                .ToList();

            foreach (SpeedEntry entry in block.Entries.Where(e => e.Records.Count > 0))
            {
                if (built.FirstOrDefault(b => b.State.Key == (int)entry.Key).Arms is not { Count: > 1 } arms)
                    continue;

                SpeedLadder? forward =
                    arms.FirstOrDefault(a => MathF.Abs(a.Direction) < 1e-3f).Ladder ??
                    arms.FirstOrDefault(a => MathF.Abs(a.Direction - 1f) < 1e-3f).Ladder;

                if (forward is null) continue;
                List<(float, SpeedLadder)> only = [(0f, forward)];

                int c = 0, f = 0, n = 0;
                foreach (SpeedRecord record in entry.Records)
                    foreach (SpeedPoint point in record.Points)
                    {
                        if (point.Y <= 0f) continue;
                        n++;
                        if (Holds(SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset), point.Y)) c++;
                        if (Holds(SpeedSampler.Sample(only, record.Direction, point.X - SpeedLadder.SamplerOffset), point.Y)) f++;
                    }

                if (n == 0) continue;
                compass += c; ahead += f; all += n;
                byBlock[$"{project.Name}:{entry.Key}"] = (c, f, n);
            }
        }

        Assert.Equal(5851, all);
        Assert.Equal(5247, compass);
        Assert.Equal(1126, ahead);

        // Exactly one block does better without its compass, and it does perfectly.
        var better = byBlock.Where(b => b.Value.Ahead > b.Value.Compass).ToList();
        Assert.Equal(["BenthicLurkerProject:0"], better.Select(b => b.Key));
        Assert.Equal((23, 253, 253), byBlock["BenthicLurkerProject:0"]);

        // and it is not a property of the creature: its other block wants the compass
        Assert.Equal((78, 11, 278), byBlock["BenthicLurkerProject:1"]);
    }

    /// <summary>
    /// The first-person rig carries one curve at every heading, and the forward arm
    /// is all of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All nineteen of <c>FirstPerson</c>'s records are the same curve, point for
    /// point, and <strong>the forward arm of <c>LocomotionDefault</c> alone answers
    /// every one of its 209 points</strong> where its eight-arm compass answers 42.
    /// For a camera that is what one would expect: the view travels at the
    /// character's speed whichever way the body is strafing.
    /// </para>
    /// <para>
    /// It is not applied, for the same reason the lurker's is not. Nothing in the
    /// inputs marks this project as a camera -- the name is not an input -- and the
    /// third-person player, whose block is built from the same
    /// <c>LocomotionDefault</c> state in the same shared graph, wants its compass
    /// and holds 226 of 226 points with it. So the two projects that must be told
    /// apart are told apart by nothing that has been found.
    /// </para>
    /// </remarks>
    [MastersFact]
    public void FirstPersonIgnoresItsHeadingToo()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var actor = (ActorProject)cache.OpenActor("FirstPerson")!;
        ProjectWalk walk = ProjectWalk.Of(cache.FindProjectFile("FirstPerson")!);
        SpeedEntry entry = cache.SpeedData!.Block("FirstPerson")!.Entries.First(e => e.Records.Count > 0);

        // every record is the same curve
        SpeedRecord first = entry.Records[0];
        Assert.Equal(19, entry.Records.Count);
        Assert.All(entry.Records, record =>
        {
            Assert.Equal(first.Points.Count, record.Points.Count);
            Assert.All(record.Points.Zip(first.Points), pair =>
                Assert.True(MathF.Abs(pair.First.Y - pair.Second.Y) <= 0.02f * MathF.Max(pair.Second.Y, 0.01f)));
        });

        var arms = LocomotionStates.In(walk, new ProjectVariables(walk.Steps))
            .Select(st => (State: st, Arms: Compass.ArmsOf(walk, st, actor)))
            .First(st => st.State.State.m_name == "LocomotionDefault" && st.Arms.Count == 8).Arms;

        SpeedLadder forward = arms.First(a => MathF.Abs(a.Direction) < 1e-3f).Ladder;
        List<(float, SpeedLadder)> only = [(0f, forward)];

        int compass = 0, ahead = 0, all = 0;
        foreach (SpeedRecord record in entry.Records)
            foreach (SpeedPoint point in record.Points)
            {
                if (point.Y <= 0f) continue;
                all++;
                if (Holds(SpeedSampler.Sample(arms, record.Direction, point.X - SpeedLadder.SamplerOffset), point.Y)) compass++;
                if (Holds(SpeedSampler.Sample(only, record.Direction, point.X - SpeedLadder.SamplerOffset), point.Y)) ahead++;
            }

        Assert.Equal(209, all);
        Assert.Equal(42, compass);
        Assert.Equal(209, ahead);
    }

    private static bool Holds(double got, double want) =>
        got > 0 && Math.Abs(got - want) / want <= 0.02;
}
