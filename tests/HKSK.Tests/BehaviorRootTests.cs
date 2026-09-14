using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Following a project to the one behaviour graph it enters at.
/// </summary>
/// <remarks>
/// The root behaviour is the only file addressed from the character file, which
/// is the only file addressed from the project file. These confirm that against
/// all 49 shipped projects, and confirm the negative that makes it the <em>only</em>
/// route.
/// </remarks>
public sealed class BehaviorRootTests
{
    /// <summary>The chain is exactly one file wide at each link, for all 49.</summary>
    /// <remarks>
    /// And no project names a behaviour itself:
    /// <c>hkbProjectStringData.m_behaviorFilenames</c> exists and is empty in every
    /// one of them, so the character file is the only way to the entry graph.
    /// </remarks>
    [CorpusFact]
    public void EveryProjectNamesOneCharacterWhichNamesOneBehaviour()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int found = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ProjectFile project = ProjectFile.Load(at.ProjectFile!);

            Assert.Single(project.CharacterFiles);
            Assert.Empty(project.Data.m_stringData?.m_behaviorFilenames ?? []);

            BehaviorRoot root = Assert.IsType<BehaviorRoot>(BehaviorRoot.Of(at.ProjectFile!));

            Assert.True(File.Exists(root.CharacterFile), $"{at.Name}: {root.CharacterFile}");
            Assert.True(File.Exists(root.BehaviorFile), $"{at.Name}: {root.BehaviorFile}");
            Assert.NotEmpty(root.StoredBehaviorName);

            found++;
        }

        Assert.Equal(49, found);
    }

    /// <summary>
    /// What the chain finds is the graph the walk roots at, and it holds one
    /// <c>hkbBehaviorGraph</c> with a root generator.
    /// </summary>
    [CorpusFact]
    public void TheFileTheChainFindsIsTheGraphRoot()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int agreed = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            BehaviorRoot root = BehaviorRoot.Of(at.ProjectFile!)!.Value;
            ActorProject project = cache.OpenActor(at.Name)!;

            Assert.Equal(
                Path.GetFullPath(root.BehaviorFile),
                Path.GetFullPath(project.Graph.RootFile!.Path!),
                ignoreCase: true);

            // the entry graph, and only one of it
            BehaviorFile file = BehaviorFile.Load(root.BehaviorFile);
            Assert.Single(file.File.All<hkbBehaviorGraph>());
            Assert.NotNull(file.File.First<hkbBehaviorGraph>()!.m_rootGenerator);

            agreed++;
        }

        Assert.Equal(49, agreed);
    }

    /// <summary>
    /// Neither shortcut to the root is sound, though both would pass on the
    /// vanilla files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The animation cache lists behaviour files too, and 13 projects list more
    /// than one -- those are the graphs the entry point <em>references</em>, not
    /// the entry point. The root happens to be the first of that list in all 49,
    /// which is exactly the kind of coincidence that survives until a modded
    /// project reorders it.
    /// </para>
    /// <para>
    /// Naming is no better: the root is named after its project in only 3 of 49.
    /// Both player sexes and the first-person rig enter at <c>0_Master</c>, and
    /// the chicken's character file is <c>chickencharater.hkx</c>, misspelt in the
    /// shipped game.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void TheShortcutsToTheRootAreCoincidences()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int first = 0, namedAfterProject = 0, cacheListsMore = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            ActorProject project = cache.OpenActor(at.Name)!;
            BehaviorRoot root = BehaviorRoot.Of(at.ProjectFile!)!.Value;

            if (string.Equals(project.Behaviors[0].Name, root.Name, StringComparison.OrdinalIgnoreCase)) first++;
            if (string.Equals(root.Name, at.Name, StringComparison.OrdinalIgnoreCase)) namedAfterProject++;
            if (project.Behaviors.Count > 1) cacheListsMore++;
        }

        Assert.Equal(49, first);              // true, and not guaranteed
        Assert.Equal(3, namedAfterProject);   // demonstrably not a rule
        Assert.Equal(13, cacheListsMore);

        // the three that do match are the ones where it means nothing
        Assert.Equal("0_master", BehaviorRoot.Of(cache.FindProjectFile("DefaultMale")!)!.Value.Name);
        Assert.Equal("0_master", BehaviorRoot.Of(cache.FindProjectFile("DefaultFemale")!)!.Value.Name);
        Assert.Equal("0_master", BehaviorRoot.Of(cache.FindProjectFile("FirstPerson")!)!.Value.Name);
    }

    /// <summary>The stored paths are followed as written, not reconstructed.</summary>
    /// <remarks>
    /// Their spelling is not uniform. The wolf keeps its graph in
    /// <c>Behaviors Wolf\</c> rather than <c>Behaviors\</c>, and the chicken's
    /// character file is misspelt. Anything that rebuilds these from a convention
    /// loses those two.
    /// </remarks>
    [CorpusFact]
    public void TheStoredPathsAreNotUniform()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        BehaviorRoot wolf = BehaviorRoot.Of(cache.FindProjectFile("WolfProject")!)!.Value;
        Assert.Equal(@"Behaviors Wolf\WolfBehavior.hkx", wolf.StoredBehaviorName);

        BehaviorRoot chicken = BehaviorRoot.Of(cache.FindProjectFile("ChickenProject")!)!.Value;
        Assert.Contains("chickencharater", chicken.StoredCharacterName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"Behaviors\ChickenBehavior.hkx", chicken.StoredBehaviorName);
    }
}
