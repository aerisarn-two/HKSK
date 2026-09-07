using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The split form has to be a faithful carrier: what goes out must come back.
/// </summary>
public class SplitMergeTests
{
    /// <summary>
    /// Splitting the shipped merged cache and rebuilding it reproduces both
    /// merged files exactly.
    /// </summary>
    /// <remarks>
    /// This is what makes the split form usable as the editing format. It also
    /// covers the ordering: the rebuild follows the two <c>dirlist.txt</c> files,
    /// and 429 projects would not come back in the right order by accident.
    /// </remarks>
    [CorpusFact]
    public void SplittingAndRebuildingReproducesTheMergedFiles()
    {
        SkyrimCache original = SkyrimCache.Load(Corpus.Root!);
        string folder = Temp();

        try
        {
            original.Split(folder);
            SkyrimCache rebuilt = SkyrimCache.FromSplit(folder, out var issues);

            // The only thing it cannot represent cleanly is a name listed twice,
            // and Skyrim lists ten. They are identical copies, so the rebuild is
            // still exact -- but the report says so rather than staying quiet.
            Assert.All(issues, i => Assert.Equal("duplicate-project", i.Kind));

            Assert.Equal(original.AnimationData.Write(), rebuilt.AnimationData.Write());
            Assert.Equal(original.SetData.Write(), rebuilt.SetData.Write());

            // And it is the game's own bytes, not merely self-consistent.
            Assert.Equal(File.ReadAllText(Corpus.AnimationData), rebuilt.AnimationData.Write());
            Assert.Equal(File.ReadAllText(Corpus.AnimationSetData), rebuilt.SetData.Write());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [CorpusFact]
    public void TheSplitFormWritesAFilePerProject()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        string folder = Temp();

        try
        {
            cache.Split(folder);

            string data = Path.Combine(folder, SplitCache.AnimationDataFolder);
            int projects = cache.AnimationData.Projects.Count;
            int cached = cache.AnimationData.Projects.Count(p => p.Block.HasAnimationCache);

            // A file per distinct project name, plus dirlist.txt. Ten of the 429
            // entries are a second listing of a name already present, and share
            // its file.
            int distinct = cache.AnimationData.Projects
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            Assert.Equal(419, distinct);
            Assert.Equal(distinct + 1, Directory.GetFiles(data, "*.txt").Length);

            // And a root motion file for each project that has a cache.
            Assert.Equal(cached,
                Directory.GetFiles(Path.Combine(data, SplitCache.BoundAnimsFolder), "*.txt").Length);

            Assert.Equal(429, projects);
            Assert.Equal(49, cached);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The split copy Skyrim ships is stale, and rebuilding from it would roll
    /// the game back. The library reports that rather than doing it quietly.
    /// </summary>
    /// <remarks>
    /// The shipped listing names 328 projects where the merged file carries 429,
    /// so the 96 Dawnguard and Dragonborn creatures are simply absent, and
    /// <c>ShoutImod.txt</c> is listed with no file behind it. Nine more projects
    /// carry fewer clips than the merged file.
    /// </remarks>
    [CorpusFact]
    public void RebuildingFromTheShippedSplitCopyIsReportedAsIncomplete()
    {
        SkyrimCache merged = SkyrimCache.Load(Corpus.Root!);
        SkyrimCache fromSplit = SkyrimCache.FromSplit(Corpus.Root!, out var issues);

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Kind == "missing-project" && i.Message.Contains("ShoutImod.txt"));

        // Far fewer projects than the game actually loads.
        Assert.True(fromSplit.AnimationData.Projects.Count < merged.AnimationData.Projects.Count,
            "the shipped split copy should be missing projects the merged file has");

        var mergedNames = merged.AnimationData.Projects.Select(p => p.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var splitNames = fromSplit.AnimationData.Projects.Select(p => p.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The DLC creatures are the ones that went missing.
        Assert.Contains("ChaurusFlyer", mergedNames);
        Assert.DoesNotContain("ChaurusFlyer", splitNames);
    }

    /// <summary>
    /// A project written out and read back keeps its clips and its numbering.
    /// </summary>
    [Fact]
    public void ASplitProjectKeepsItsNumberingThroughARoundTrip()
    {
        var cache = SkyrimCache.FromParts(new AnimationDataFile(), new AnimationSetDataFile());
        cache.AnimationData.Projects.Add(Fake.Data());

        string folder = Temp();

        try
        {
            cache.Split(folder);
            SkyrimCache rebuilt = SkyrimCache.FromSplit(folder, out var issues);

            Assert.Empty(issues);

            AnimationDataProject project = Assert.Single(rebuilt.AnimationData.Projects);
            Assert.Equal("FakeProject.txt", project.Name);
            Assert.Equal(3, project.Block.Clips.Count);
            Assert.Equal(2, project.Block.Clip("Run")!.CacheIndex);
            Assert.Equal(30f, project.Movements!.For(2)!.Travel, 3);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Temp()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"hksk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
