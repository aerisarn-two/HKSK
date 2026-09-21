using HKSK.Cache;
using HKSK.Model;
using HKSK.SetData;
using HKSK.Speed;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Amending one project: the entry a whole regeneration would write, put where the old
/// one stood, with every other project's entry left byte for byte as it was.
/// </summary>
public class AmendTests
{
    // the dog shares its graph with the wolf, and both have a speed block and sets
    private const string Project = "DogProject";

    [MastersFact]
    public void AnAmendedSpeedBlockIsTheOneTheWholeTableWouldHold()
    {
        var movements = Masters.Read();

        SkyrimCache regenerated = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile whole = SpeedDataGenerator.Generate(regenerated, movements);

        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        SpeedDataFile shipped = cache.SpeedData!;
        List<string> before = [.. shipped.Projects];
        int at = before.FindIndex(p => SpeedDataFile.StemOf(p) == Project);
        byte[][] others = [.. shipped.Blocks.Select(Bytes)];

        Assert.Equal(Amendment.Replaced, SpeedDataGenerator.Amend(cache, Project, movements));

        Assert.Equal(before, cache.SpeedData!.Projects);
        Assert.Equal(Bytes(whole.Block(Project)!), Bytes(cache.SpeedData.Blocks[at]));
        for (int i = 0; i < others.Length; i++)
            if (i != at) Assert.Equal(others[i], Bytes(cache.SpeedData.Blocks[i]));
    }

    [MastersFact]
    public void AProjectTheTableLacksIsAddedAtTheEnd()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int at = cache.SpeedData!.Projects.FindIndex(p => SpeedDataFile.StemOf(p) == Project);
        cache.SpeedData.Projects.RemoveAt(at);
        cache.SpeedData.Blocks.RemoveAt(at);

        Assert.Equal(Amendment.Added, SpeedDataGenerator.Amend(cache, Project, Masters.Read()));
        Assert.Equal(SpeedDataFile.ListingFor(Project), cache.SpeedData.Projects[^1]);
        Assert.Equal(cache.SpeedData.Projects.Count, cache.SpeedData.Blocks.Count);
    }

    /// <summary>
    /// The shipped table has a block for all 49 projects, the eight flyers and hoverers
    /// included; they carry no sampler, nothing reads theirs, and the generator writes none.
    /// </summary>
    [MastersFact]
    public void ACreatureWithNoSamplerLosesItsSpeedBlock()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        const string dragon = "DragonProject";
        int count = cache.SpeedData!.Projects.Count;
        Assert.NotNull(cache.SpeedData.Block(dragon));

        Assert.Equal(Amendment.Removed, SpeedDataGenerator.Amend(cache, dragon, Masters.Read()));
        Assert.Null(cache.SpeedData.Block(dragon));
        Assert.Equal(count - 1, cache.SpeedData.Blocks.Count);
        Assert.Equal(Amendment.None, SpeedDataGenerator.Amend(cache, dragon, Masters.Read()));
    }

    [MastersFact]
    public void AnAmendedSetEntryIsTheOneTheWholeFileWouldHold()
    {
        GameEvents events = HKSK.Records.GameRecordRules.Events(Masters.Records);

        AnimationSetDataFile whole = SetDataGenerator.Generate(SkyrimCache.Load(Corpus.Root!), events);

        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<string> before = [.. cache.SetData.Projects.Select(p => p.Name)];
        int at = before.FindIndex(p => p.StartsWith(Project + "Data", StringComparison.Ordinal));
        string[] others = [.. cache.SetData.Projects.Select(Text)];

        Assert.Equal(Amendment.Replaced, SetDataGenerator.Amend(cache, Project, events));

        Assert.Equal(before, cache.SetData.Projects.Select(p => p.Name));
        Assert.Equal(Text(whole.Project(Project)!), Text(cache.SetData.Projects[at]));
        for (int i = 0; i < others.Length; i++)
            if (i != at) Assert.Equal(others[i], Text(cache.SetData.Projects[i]));
    }

    [CorpusFact]
    public void AnAmendmentHasToNameAProjectTheCacheHas()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        Assert.Throws<ArgumentException>(() => SetDataGenerator.Amend(cache, "NoSuchProject", new GameEvents(
            new HashSet<string>(), new HashSet<string>(), new Dictionary<string, IReadOnlySet<string>>())));
        Assert.Throws<ArgumentException>(() => SpeedDataGenerator.Amend(cache, "NoSuchProject", new Dictionary<string, MovementType>()));
    }

    private static byte[] Bytes(SpeedProjectBlock block) =>
        new SpeedDataFile { Projects = ["X"], Blocks = [block] }.Write();

    private static string Text(AnimationSetDataProject project) =>
        new AnimationSetDataFile { Projects = [project] }.Write();
}
