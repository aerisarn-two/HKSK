using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>The three merged files amended together, from the game's records.</summary>
public class CacheGenerationTests
{
    /// <summary>
    /// A creature none of the three files lists joins all three, as the actor a race wears it
    /// as, and every other project's entries stay as they were.
    /// </summary>
    [MastersFact]
    public void ACreatureJoinsAllThreeFiles()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        cache.AnimationData.Projects.Remove(cache.AnimationData.Project("WolfProject")!);
        cache.SetData.Projects.Remove(cache.SetData.Project("WolfProject")!);
        int speed = cache.SpeedData!.Projects.FindIndex(p => SpeedDataFile.StemOf(p) == "WolfProject");
        cache.SpeedData.Projects.RemoveAt(speed);
        cache.SpeedData.Blocks.RemoveAt(speed);

        string others = cache.AnimationData.Write();

        CacheAmendment done = CacheGeneration.Amend(cache, "WolfProject", Masters.Records);

        Assert.Equal(new CacheAmendment("WolfProject", Amendment.Added, Amendment.Added, Amendment.Added), done);
        Assert.True(cache.AnimationData.Projects[^1].Block.HasAnimationCache);
        Assert.Equal("WolfProject", cache.SetData.Projects[^1].Stem);
        Assert.Equal(SpeedDataFile.ListingFor("WolfProject"), cache.SpeedData.Projects[^1]);

        cache.AnimationData.Projects.RemoveAt(cache.AnimationData.Projects.Count - 1);
        Assert.Equal(others, cache.AnimationData.Write());
    }

    /// <summary>Amending twice is amending once: the second pass finds nothing to change.</summary>
    [MastersFact]
    public void ASecondAmendmentChangesNothing()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        CacheGeneration.Amend(cache, "ChickenProject", Masters.Records);
        CacheAmendment again = CacheGeneration.Amend(cache, "ChickenProject", Masters.Records);

        Assert.Equal(new CacheAmendment("ChickenProject", Amendment.Unchanged, Amendment.Unchanged, Amendment.Unchanged), again);
    }
}
