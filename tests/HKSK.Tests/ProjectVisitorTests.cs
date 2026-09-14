using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Visiting a project's whole behaviour across files, from the project packfile
/// alone.
/// </summary>
/// <remarks>
/// Nothing here is taken from the animation cache. The visit starts at a project
/// packfile, follows it to its character file and on to the one behaviour that
/// names, and opens every file reached by <c>hkbBehaviorReferenceGenerator</c>
/// along the way. Which files that turns out to be is a result, not an input,
/// which is what makes comparing it against the cache's list worth anything.
/// </remarks>
public sealed class ProjectVisitorTests
{
    /// <summary>
    /// The visit reaches every object of every behaviour file of all 49 projects,
    /// and reaches each one once.
    /// </summary>
    /// <remarks>
    /// 218,330 objects over 125 files. The step count equals the object count
    /// exactly, so nothing is visited twice -- which is not free, because a
    /// behaviour graph has cycles and the quadrupeds reach one shared file from
    /// several places.
    /// </remarks>
    [CorpusFact]
    public void TheVisitReachesEveryObjectOfEveryProjectExactlyOnce()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int steps = 0, held = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            List<ProjectStep> walk = [.. ProjectVisitor.Visit(at.ProjectFile!)];

            var reached = new HashSet<IHavokObject>(
                walk.Select(s => s.Node), ReferenceEqualityComparer.Instance);

            Assert.Equal(walk.Count, reached.Count);          // once each

            ActorProject project = cache.OpenActor(at.Name)!;
            int inFiles = project.Behaviors.Sum(b => b.File.Objects.Count);

            Assert.Equal(inFiles, walk.Count);                // and all of them

            steps += walk.Count;
            held += inFiles;
        }

        Assert.Equal(218330, steps);
        Assert.Equal(218330, held);
    }

    /// <summary>
    /// Following the references from a project packfile reconstructs exactly the
    /// behaviour file list the animation cache carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For all 49 projects the two sets are equal, file for file -- 125 in total.
    /// The cache's list is therefore derivable from the packfiles and is not
    /// independent information, which is worth knowing both ways round: a tool
    /// adding a referenced graph knows what the cache entry must become, and a
    /// cache entry that disagrees with the references is wrong rather than merely
    /// different.
    /// </para>
    /// <para>
    /// It also says the reference graph is connected. No project ships a behaviour
    /// file nothing reaches, and none reaches a file the cache does not list.
    /// </para>
    /// </remarks>
    [CorpusFact]
    public void FollowingReferencesFindsExactlyTheFilesTheCacheLists()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int files = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            var visited = ProjectVisitor.Visit(at.ProjectFile!)
                .Select(s => Path.GetFullPath(s.File))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var listed = cache.OpenActor(at.Name)!.Behaviors
                .Select(b => Path.GetFullPath(b.Path!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Equal(listed.Count, visited.Count);
            Assert.True(visited.SetEquals(listed),
                $"{at.Name}: only visited [{string.Join(",", visited.Except(listed).Select(Path.GetFileName))}], " +
                $"only listed [{string.Join(",", listed.Except(visited).Select(Path.GetFileName))}]");

            files += visited.Count;
        }

        Assert.Equal(125, files);
    }

    /// <summary>
    /// Starting at the container rather than the root generator is what closes the
    /// last gap.
    /// </summary>
    /// <remarks>
    /// <see cref="BehaviorGraph"/> walks from the behaviour graph's root generator
    /// and so reaches 28,351 of the 28,476 <c>hkbNode</c>s in the corpus. The 125
    /// it does not reach are the per-file <c>hkbBehaviorGraph</c> wrappers it
    /// starts below -- one for each of the 125 files. Visiting from the container
    /// reaches all 28,476.
    /// </remarks>
    [CorpusFact]
    public void VisitingFromTheContainerReachesTheGraphWrappersToo()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int fromContainer = 0, fromRootGenerator = 0, wrappers = 0;

        foreach (ProjectLocation at in cache.LocateSpeedProjects())
        {
            fromContainer += ProjectVisitor.Visit(at.ProjectFile!).Count(s => s.Node is hkbNode);

            ActorProject project = cache.OpenActor(at.Name)!;
            fromRootGenerator += project.Graph.Visit().Count(s => s.Node is hkbNode);
            wrappers += project.Behaviors.Sum(b => b.File.All<hkbBehaviorGraph>().Count());
        }

        Assert.Equal(28476, fromContainer);
        Assert.Equal(28351, fromRootGenerator);
        Assert.Equal(125, wrappers);
        Assert.Equal(fromContainer, fromRootGenerator + wrappers);
    }

    /// <summary>
    /// A reference is resolved against the project's folder, which is why two
    /// projects can reach two different files of the same name.
    /// </summary>
    /// <remarks>
    /// The dog and the wolf share <c>actors/canine</c> and both reach a file
    /// called <c>QuadrupedBehavior.hkx</c>, but the dog's reference reads
    /// <c>Behaviors\</c> and the wolf's <c>Behaviors Wolf\</c>. They are two
    /// files. Anything that keys a referenced graph on its name alone, or resolves
    /// it against the referencing file's folder instead of the project's, merges
    /// them.
    /// </remarks>
    [CorpusFact]
    public void TwoProjectsInOneFolderReachTwoFilesOfTheSameName()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        string Quadruped(string project) =>
            Path.GetFullPath(ProjectVisitor.Visit(cache.FindProjectFile(project)!)
                .Select(s => s.File)
                .First(f => Path.GetFileName(f).Equals("quadrupedbehavior.hkx", StringComparison.OrdinalIgnoreCase)));

        string dog = Quadruped("DogProject");
        string wolf = Quadruped("WolfProject");

        Assert.NotEqual(dog, wolf);
        Assert.Equal(Path.GetFileName(dog), Path.GetFileName(wolf));

        // and they really are in the same project folder
        Assert.Equal(cache.FindProjectFolder("DogProject"), cache.FindProjectFolder("WolfProject"));
    }

    /// <summary>A visited object knows which file holds it and what holds it there.</summary>
    [CorpusFact]
    public void AVisitedObjectKnowsWhichFileHoldsIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<ProjectStep> walk = [.. ProjectVisitor.Visit(cache.FindProjectFile("DefaultMale")!)];

        Assert.Equal("0_master", walk[0].FileName);
        Assert.Null(walk[0].Parent);
        Assert.IsType<hkRootLevelContainer>(walk[0].Node);

        // the step that enters mt_behavior is a container, reached from a reference
        ProjectStep crossing = walk.First(s => s.FileName == "mt_behavior");

        Assert.IsType<hkRootLevelContainer>(crossing.Node);
        Assert.IsType<hkbBehaviorReferenceGenerator>(crossing.Parent);
        Assert.Equal("m_behaviorName", crossing.Member);

        // and the reference that reached it lives in a different file
        ProjectStep referencing = walk.First(s => ReferenceEquals(s.Node, crossing.Parent));
        Assert.NotEqual(crossing.FileName, referencing.FileName);
    }
}
