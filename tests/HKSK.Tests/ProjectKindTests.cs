using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// A project is an actor or a prop, and which one it is decides what can be
/// asked of it.
/// </summary>
/// <remarks>
/// The split is not a convenience. In the shipped game a project carries a clip
/// cache if and only if it has animation set data, and the 380 that do not carry
/// no clips, no root motion and no sets whatever -- so nearly everything an
/// actor can be asked is meaningless for them. Two types make that a matter of
/// which methods exist rather than which calls throw.
/// </remarks>
public class ProjectKindTests
{
    [MopperFact]
    public void ACachedProjectOpensAsAnActorAndAnUncachedOneAsAProp()
    {
        using var fixture = SyntheticProject.Build();
        SkyrimCache cache = fixture.Cache();

        Assert.IsType<ActorProject>(cache.Open(SyntheticProject.ProjectName));
        Assert.IsType<PropProject>(cache.Open(SyntheticProject.PropName));

        // OpenActor is the shortcut for "I want animations", and a prop is not
        // one -- it answers null rather than something half-usable.
        Assert.NotNull(cache.OpenActor(SyntheticProject.ProjectName));
        Assert.Null(cache.OpenActor(SyntheticProject.PropName));
        Assert.Null(cache.OpenActor("NoSuchProject"));
    }

    [MopperFact]
    public void ThePropCarriesItsFilesAndNothingElse()
    {
        using var fixture = SyntheticProject.Build();
        PropProject prop = fixture.OpenProp();

        Assert.Equal(3, prop.Files.Count);
        Assert.False(prop.Data.Block.HasAnimationCache);
        Assert.Empty(prop.Data.Block.Clips);
        Assert.Null(prop.Data.Movements);

        // Nothing wrong with it: a prop having nothing is a prop being correct.
        Assert.Empty(prop.Validate());
    }

    [MopperFact]
    public void ProjectsSplitIntoActorsAndProps()
    {
        using var fixture = SyntheticProject.Build();
        SkyrimCache cache = fixture.Cache();

        Assert.Equal(2, cache.OpenAll().Count());
        Assert.Equal(SyntheticProject.ProjectName, Assert.Single(cache.Actors()).Name);
        Assert.Equal(SyntheticProject.PropName, Assert.Single(cache.Props()).Name);
    }

    /// <summary>
    /// A prop with clips is malformed, and says so -- the state that adding a
    /// clip to a prop used to create silently.
    /// </summary>
    [MopperFact]
    public void APropCarryingClipsIsReported()
    {
        using var fixture = SyntheticProject.Build();
        SkyrimCache cache = fixture.Cache();
        var prop = (PropProject)cache.Open(SyntheticProject.PropName)!;

        prop.Data.Block.Clips.Add(new ClipGeneratorEntry { Name = "Sneaked", CacheIndex = 0 });

        Finding finding = Assert.Single(prop.Validate());
        Assert.Equal("clips-without-cache", finding.Kind);
        Assert.Equal(Severity.Error, finding.Severity);
    }

    /// <summary>
    /// Promoting is the only way a project changes kind, and it creates the set
    /// data that a cached project always has.
    /// </summary>
    [MopperFact]
    public void PromotingAPropMakesAnActorWithItsSetData()
    {
        using var fixture = SyntheticProject.Build();
        SkyrimCache cache = fixture.Cache();
        var prop = (PropProject)cache.Open(SyntheticProject.PropName)!;

        Assert.Null(cache.SetData.Project(prop.Name));

        CharacterFile character = CharacterFile.Create("TestDoor", [@"Animations\Open.hkx"]);
        ActorProject actor = cache.PromoteToActor(prop, character);

        Assert.Equal(SyntheticProject.PropName, actor.Name);
        Assert.True(actor.HasCache);
        Assert.NotNull(actor.Data.Movements);
        Assert.Single(actor.Animations);

        // The biconditional is kept: cache implies set data.
        AnimationSetDataProject sets = Assert.Single(
            cache.SetData.Projects, p => p.Stem.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));

        Assert.Equal($@"{prop.Name}Data\{prop.Name}.txt", sets.Name);
        Assert.DoesNotContain(actor.Validate(), f => f.Kind == "missing-set-data");

        // And it now opens as an actor.
        Assert.IsType<ActorProject>(cache.Open(SyntheticProject.PropName));
    }

    /// <summary>An actor with a cache but no set data is half-made, and reported.</summary>
    [MopperFact]
    public void ACachedProjectWithoutSetDataIsReported()
    {
        using var fixture = SyntheticProject.Build();
        SkyrimCache cache = fixture.Cache();

        cache.SetData.Projects.Clear();
        ActorProject actor = cache.OpenActor(SyntheticProject.ProjectName)!;

        Assert.Contains(actor.Validate(), f => f.Kind == "missing-set-data");
    }

    /// <summary>The two kinds in the shipped game, counted.</summary>
    [CorpusFact]
    public void TheGameHoldsFortyNineActorsAndThreeHundredAndEightyProps()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        Assert.Equal(49, cache.Actors().Count());
        Assert.Equal(380, cache.Props().Count());

        // Nothing in the game is malformed on the kind test itself.
        Assert.All(cache.Props(), p => Assert.Empty(p.Validate()));
    }

    /// <summary>
    /// A prop's packfile is found too, though it does not live under actors/.
    /// </summary>
    /// <remarks>
    /// Doors are beside doors and windmills beside farmhouses, so an index of
    /// <c>actors/</c> alone -- which is what this used to be -- never resolved
    /// one of the 380.
    /// </remarks>
    [CorpusFact]
    public void APropsPackfileIsFoundOutsideTheActorsFolder()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        string? windmill = cache.FindProjectFile("FarmhouseWindMill");

        Assert.NotNull(windmill);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}actors{Path.DirectorySeparatorChar}",
            windmill, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(windmill));

        // Actors still resolve, and still from actors/.
        string? chicken = cache.FindProjectFile("ChickenProject");
        Assert.NotNull(chicken);
        Assert.Contains($"{Path.DirectorySeparatorChar}actors{Path.DirectorySeparatorChar}",
            chicken, StringComparison.OrdinalIgnoreCase);
    }
}
