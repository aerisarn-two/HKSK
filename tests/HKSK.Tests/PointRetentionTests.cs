using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>Which of the swept goal speeds a shipped record keeps.</summary>
public sealed class PointRetentionTests
{
    /// <summary>The model's dense sweep over a shipped record's own range.</summary>
    private static List<SpeedPoint> Sweep(IReadOnlyList<(float Direction, SpeedLadder Ladder)> arms, SpeedRecord record)
    {
        List<SpeedPoint> sweep = [];
        float first = record.Points[0].X, last = record.Points[^1].X;
        int n = (int)MathF.Round((last - first) / 0.5f) + 1;

        for (int i = 0; i < n; i++)
        {
            float x = first + 0.5f * i;
            sweep.Add(new SpeedPoint(x, (float)SpeedSampler.Sample(arms, record.Direction, x - SpeedLadder.SamplerOffset)));
        }

        return sweep;
    }

    private static int Exact(SkyrimCache cache, string project, float tolerance)
    {
        SpeedSampler sampler = SpeedSampler.FromProject((ActorProject)cache.OpenActor(project)!)!;
        int exact = 0;

        foreach (SpeedEntry entry in cache.SpeedData!.Block(project)!.Entries)
        {
            var arms = sampler.CompassFor((int)entry.Key)!;
            foreach (SpeedRecord record in entry.Records)
            {
                var kept = SpeedRecord.Retain(Sweep(arms, record), tolerance).Select(p => p.X).ToHashSet();
                if (kept.SetEquals(record.Points.Select(p => p.X))) exact++;
            }
        }

        return exact;
    }

    /// <summary>
    /// Where the curve is reproduced closely, the retention is reproduced exactly.
    /// </summary>
    /// <remarks>
    /// The three centurions' curves are the rebuild's most exact, and every one of
    /// their 57 records keeps precisely the goal speeds a greedy pass at two units
    /// keeps. The tolerance is not a fit that happens to land near 2: a hundredth
    /// either side loses most of them.
    /// </remarks>
    [CorpusFact]
    public void TheCenturionsKeepExactlyWhatAGreedyPassAtTwoUnitsKeeps()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        string[] projects = ["BallistaCenturion", "SphereCenturion", "SteamProject"];

        Assert.Equal([19, 19, 19], projects.Select(p => Exact(cache, p, 2f)));

        Assert.Equal(57, projects.Sum(p => Exact(cache, p, 2f)));
        Assert.True(projects.Sum(p => Exact(cache, p, 1.99f)) < 40);
        Assert.True(projects.Sum(p => Exact(cache, p, 2.01f)) < 40);
    }

    [Fact]
    public void AStraightLineKeepsItsEnds()
    {
        List<SpeedPoint> line = [.. Enumerable.Range(0, 650).Select(i => new SpeedPoint(i * 0.5f, i * 0.37f))];
        Assert.Equal([line[0], line[^1]], SpeedRecord.Retain(line));
    }
}
