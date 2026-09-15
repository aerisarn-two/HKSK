using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The join between the behaviour graph and <c>speeddatasinglefile.txt</c>.
/// </summary>
/// <remarks>
/// <c>BSSpeedSamplerModifier</c> is the only thing in the game that reads a speed
/// table. It takes <c>iState</c>, <c>Direction</c> and <c>Speed</c> in through
/// bindings, looks the creature up, and writes the answer back out through the
/// binding on <c>speedOut</c> -- which is <c>SpeedSampled</c>, the parameter of
/// the blend ladder. With the table loaded the engine no longer has to be told
/// which rung to stand on.
/// </remarks>
public sealed class SpeedSamplerEngineTests
{
    private static readonly Events Moving = Events.Of("moveStart", "moveForward");

    [CorpusFact]
    public void TheTableDrivesTheLadder()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile speeds = SpeedDataFile.Load(Corpus.SpeedData);
        string project = cache.LocateSpeedProjects().First(p => p.Name == "DeerProject").ProjectFile!;
        SpeedProjectBlock block = Assert.IsType<SpeedProjectBlock>(speeds.Block("DeerProject"));

        // Walking. iState is 20, the table maps a goal of 50 down to 8.5, and that
        // lands between the two walk rungs so both carry weight.
        (string[] walking, float sampledWalking) = Run(project, block, 50f);
        Assert.Equal(["WalkSlowForwardL", "WalkForwardL"], walking);
        Assert.Equal(8.523f, sampledWalking, 0.01f);

        // At 100 iState becomes 21 and the block changes: a different ladder entirely.
        Assert.Equal(["SlowRunForwardL"], Run(project, block, 100f).Clips);

        // The deer's run ladder is knotted at 416.5 and 833. A goal of 450 samples
        // to 431.9, which is just past the first knot, so two rungs blend.
        (string[] crossing, float sampledCrossing) = Run(project, block, 450f);
        Assert.Equal(["SlowRunForwardL", "RunForwardL"], crossing);
        Assert.InRange(sampledCrossing, 416.5f, 833f);

        // Past the last point of the curve the query returns the goal unchanged --
        // the same as having no database -- and the top rung carries it alone.
        (string[] fast, float sampledFast) = Run(project, block, 900f);
        Assert.Equal(["RunForwardL"], fast);
        Assert.Equal(900f, sampledFast);
    }

    /// <summary>With no table the goal passes through, as it does with no database.</summary>
    [CorpusFact]
    public void WithNoTableTheGoalPassesThrough()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        string project = cache.LocateSpeedProjects().First(p => p.Name == "DeerProject").ProjectFile!;

        Assert.Equal(600f, Run(project, null, 600f).Sampled);
    }

    private static int Driven(SkyrimCache cache, SpeedDataFile speeds, float goal) =>
        cache.LocateSpeedProjects().Where(p => p.Found).Count(p =>
            speeds.Block(p.Name) is { } block &&
            Math.Abs(Run(p.ProjectFile!, block, goal).Sampled - goal) > 0.001f);

    private static (string[] Clips, float Sampled) Run(
        string project, SpeedProjectBlock? block, float speed)
    {
        Evaluation run = ActiveGenerators.Of(project, tables =>
        {
            foreach (Variables variables in tables.Values)
            {
                variables.Set("Speed", speed);
                variables.Set("SpeedSampled", 0f);
                variables.Set("Direction", 0f);
            }
        }, Moving, block);

        float sampled = 0f;
        foreach (Variables variables in run.Variables.Values)
        {
            int at = variables.IndexOf("SpeedSampled");
            if (at >= 0) { sampled = variables.AsReal(at); break; }
        }

        string[] clips = [.. run.Active
            .Where(a => a.Generator is hkbClipGenerator or BSSynchronizedClipGenerator)
            .Where(a => a.Weight > 0f)
            .Select(a => a.Name ?? "")];

        return (clips, sampled);
    }

    /// <summary>How far the join reaches across the shipped projects.</summary>
    /// <remarks>
    /// A project is driven only if three things line up: it has a sampler, the file
    /// has a block for it, and the <c>iState</c> the graph computes is a key that
    /// block carries. Where any of those fails the goal passes straight through,
    /// which is what the game does too.
    /// </remarks>
    [CorpusFact]
    public void HowManyProjectsTheTableReaches()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile speeds = SpeedDataFile.Load(Corpus.SpeedData);

        int reached = 0, passedThrough = 0;
        List<string> report = [];

        foreach (float goal in new[] { 50f, 100f, 200f, 400f, 600f })
            report.Add($"goal {goal,5}: {Driven(cache, speeds, goal)} of 49 driven by the table");

        report.Add("");

        foreach (ProjectLocation at in cache.LocateSpeedProjects().Where(p => p.Found))
        {
            SpeedProjectBlock? block = speeds.Block(at.Name);
            (string[] clips, float sampled) = Run(at.ProjectFile!, block, 600f);

            bool drove = block is not null && Math.Abs(sampled - 600f) > 0.001f;
            if (drove) reached++; else passedThrough++;

            HKSK.Behavior.ProjectWalk walk = HKSK.Behavior.ProjectWalk.Of(at.ProjectFile!);
            bool hasSampler = walk.Steps.Any(x => x.Node is BSSpeedSamplerModifier);
            int iState = 0;
            Evaluation run = ActiveGenerators.Of(at.ProjectFile!,
                t => { foreach (Variables v in t.Values) { v.Set("Speed", 600f); v.Set("Direction", 0f); } },
                Moving, block);
            foreach (Variables v in run.Variables.Values)
            { int i = v.IndexOf("iState"); if (i >= 0 && v.AsInt(i) != 0) { iState = v.AsInt(i); break; } }

            string why = drove ? "" :
                !hasSampler ? "no sampler" :
                block is null ? "no block" :
                block.Entry((uint)iState) is null ? $"iState {iState} not a key" : "goal past the curve";

            report.Add($"{at.Name,-28} {(drove ? "sampled" : "passed  ")} {sampled,9:0.###}  {why,-24}" +
                       $"{string.Join(", ", clips)}");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "sampler-corpus.txt"),
            $"{reached} driven by the table, {passedThrough} passed through\n" +
            string.Join("\n", report));

        Assert.Equal(49, reached + passedThrough);

        // At a speed the creatures actually reach, the join lands for all but two.
        // The two are a project whose graph computes an iState the block has no
        // entry for, and one with no sampler at all.
        Assert.Equal(47, Driven(cache, speeds, 200f));

        // At 600 most are above the top of their own curve, so the query passes the
        // goal through -- not a gap in the engine but the documented behaviour.
        Assert.Equal(20, Driven(cache, speeds, 600f));
    }
}
