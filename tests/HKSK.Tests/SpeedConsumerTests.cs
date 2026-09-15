using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
using Xunit.Abstractions;

namespace HKSK.Tests;

/// <summary>
/// What reads the speed the sampler produces.
/// </summary>
/// <remarks>
/// The sampler writes a number into a variable and walks away. What that number
/// does is decided entirely by whoever binds the variable it wrote, so the
/// consumers are the speed table's actual effect on the animation.
/// </remarks>
public sealed class SpeedConsumerTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every consumer in the game is a blend parameter, with no second shape.
    /// </summary>
    /// <remarks>
    /// 1,037 of them across the corpus and all are
    /// <c>hkbBlenderGenerator.blendParameter</c>. Nothing reads the sampled speed
    /// into a clip's playback rate, a condition or a selector, so the table only
    /// ever chooses a position within a blend.
    /// </remarks>
    [CorpusFact]
    public void EveryConsumerOfTheSampledSpeedIsABlendParameter()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var kinds = new Dictionary<string, int>();

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
            foreach (SpeedConsumer consumer in Locomotion.ConsumersIn(ProjectVisitor.Visit(at.ProjectFile!)))
            {
                string key = $"{consumer.Kind}.{consumer.MemberPath}";
                kinds.TryGetValue(key, out int count);
                kinds[key] = count + 1;
            }

        Assert.Equal(new Dictionary<string, int> { ["hkbBlenderGenerator.blendParameter"] = 1037 }, kinds);
    }

    /// <summary>
    /// 38 of the 41 projects with a sampler consume its output, and three do not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three are worth naming because each is a different kind of exception,
    /// and none is a failure of the search:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// the <strong>netch</strong> and the <strong>dwarven spider centurion</strong>
    /// write <c>SpeedSampled</c> and nothing reads it. The sampler's output is
    /// dead wiring -- their blends use other variables entirely -- which for the
    /// netch fits its sampler never binding <c>state</c> either;
    /// </description></item>
    /// <item><description>
    /// the <strong>slaughterfish</strong> bypasses the sampler: six of its blends
    /// bind <c>Speed</c>, the raw goal speed the sampler was given, rather than the
    /// sampled result. So its animation does not follow the table's blend law at
    /// all, which is a much better explanation of its curves than anything the
    /// rebuild was doing wrong.
    /// </description></item>
    /// </list>
    /// <para>
    /// All three still ship a speed block with 19 records, so the table being
    /// present says nothing about the graph using it.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void ThreeProjectsNeverReadWhatTheirSamplerWrites()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        var without = new List<string>();
        int reading = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            if (!Locomotion.RootsIn(walk).Any()) continue;

            reading++;
            if (!Locomotion.ConsumersIn(walk).Any()) without.Add(at.Name);
        }

        Assert.Equal(41, reading);
        Assert.Equal(
            ["DwarvenSpiderCenturionProject", "NetchProject", "SlaughterfishProject"],
            without.Order());
    }

    /// <summary>The slaughterfish blends on the raw goal speed instead.</summary>
    /// <remarks>
    /// Six blends bind <c>Speed</c>, which is what the sampler <em>reads</em> as its
    /// <c>goalSpeed</c>, so the sampled value never reaches the animation. The
    /// netch and the spider centurion do not even do that: nothing binds either
    /// variable.
    /// </remarks>
    [CorpusFact]
    public void TheSlaughterfishBlendsOnTheUnsampledSpeed()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        int RawSpeedBlends(string project)
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(cache.FindProjectFile(project)!)];
            var variables = new ProjectVariables(walk);

            return walk.Select(s => (Step: s, Node: s.Node as hkbBlenderGenerator))
                .Where(x => x.Node is not null)
                .SelectMany(x => (x.Node!.m_variableBindingSet?.m_bindings ?? [])
                    .Where(b => b.m_memberPath == "blendParameter")
                    .Select(b => variables.NameOf(x.Step.File, b.m_variableIndex)))
                .Count(name => name == "Speed");
        }

        Assert.Equal(6, RawSpeedBlends("SlaughterfishProject"));
        Assert.Equal(0, RawSpeedBlends("NetchProject"));
        Assert.Equal(0, RawSpeedBlends("DwarvenSpiderCenturionProject"));
    }

    /// <summary>
    /// A consumer is often in a different file from the sampler that feeds it.
    /// </summary>
    /// <remarks>
    /// The dog's sampler is in <c>quadrupedbehavior.hkx</c> and every blend reading
    /// it is in <c>forwardlocomotion.hkx</c>. A reader confined to one packfile
    /// finds the sampler and none of its consumers, which is why this needs the
    /// multi-file visit and why the variable is matched by name -- the index means
    /// something different in each file's table.
    /// </remarks>
    [CorpusFact]
    public void ConsumersAreFoundAcrossFileBoundaries()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<ProjectStep> walk = [.. ProjectVisitor.Visit(cache.FindProjectFile("DogProject")!)];

        LocomotionRoot root = Assert.Single(Locomotion.RootsIn(walk));
        Assert.Equal("quadrupedbehavior", root.FileName);

        List<SpeedConsumer> consumers = [.. Locomotion.ConsumersIn(walk)];

        Assert.NotEmpty(consumers);
        Assert.All(consumers, c => Assert.Equal("forwardlocomotion", c.FileName));
        Assert.All(consumers, c => Assert.Equal("SpeedSampled", c.Variable));
    }

    /// <summary>Lists every consumer, per project, as a readable report.</summary>
    [CorpusFact]
    public void ReportEveryConsumer()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];
            List<SpeedConsumer> consumers = [.. Locomotion.ConsumersIn(walk)];
            if (!Locomotion.RootsIn(walk).Any()) continue;

            output.WriteLine($"{at.Name} -- {consumers.Count} consumer(s)");

            foreach (var group in consumers.GroupBy(c => (c.Kind, c.MemberPath, c.Variable, c.FileName)))
            {
                output.WriteLine($"    {group.Key.Kind}.{group.Key.MemberPath} <- {group.Key.Variable} " +
                                 $"[{group.Key.FileName}] x{group.Count()}");

                foreach (SpeedConsumer consumer in group.Take(6))
                    output.WriteLine($"        '{consumer.Name}'");

                if (group.Count() > 6) output.WriteLine($"        ... {group.Count() - 6} more");
            }
        }
    }
}
