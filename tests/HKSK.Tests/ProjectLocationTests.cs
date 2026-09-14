using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Finding the 49 projects the speed table names in the shipped meshes tree.
/// </summary>
/// <remarks>
/// The first step of anything that reads a project: the speed table gives a name
/// and nothing else, and every file the project is made of -- character,
/// behaviours, animations -- is named relative to the folder its packfile sits
/// in. These check that step against the vanilla files rather than assuming it.
/// </remarks>
public sealed class ProjectLocationTests
{
    /// <summary>Every name in the speed table is a packfile under <c>actors/</c>.</summary>
    /// <remarks>
    /// The speed table names its blocks the way the animation cache names its
    /// projects, and the packfile is that name with <c>.hkx</c> on it. All 49 line
    /// up without translation, and all 49 are actors -- the props in the animation
    /// cache, 380 of them, have no speed block at all.
    /// </remarks>
    [CorpusFact]
    public void EveryProjectTheSpeedTableNamesIsFoundUnderActors()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        IReadOnlyList<ProjectLocation> located = cache.LocateSpeedProjects();

        Assert.Equal(49, located.Count);

        var lost = located.Where(p => !p.Found).Select(p => p.Name).ToList();
        Assert.Empty(lost);

        Assert.All(located, p =>
        {
            Assert.True(File.Exists(p.ProjectFile), $"{p.Name}: no file at '{p.ProjectFile}'");
            Assert.True(Directory.Exists(p.Folder), $"{p.Name}: no folder at '{p.Folder}'");

            // the packfile is the project's own name
            Assert.Equal(p.Name, Path.GetFileNameWithoutExtension(p.ProjectFile!),
                         ignoreCase: true);

            // and it is an actor, not a prop beside a farmhouse
            string relative = Path.GetRelativePath(Corpus.Root!, p.Folder!);
            Assert.StartsWith("actors", relative, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// A folder can hold more than one project, so it does not identify one.
    /// </summary>
    /// <remarks>
    /// Three folders hold two projects each: <c>actors/canine</c> the dog and the
    /// wolf, <c>actors/character</c> both player sexes, and <c>actors/draugr</c>
    /// the draugr and the draugr skeleton. So 49 projects live in 46 folders, and
    /// anything keyed on the folder rather than the project name will merge those
    /// pairs. The first-person rig is <em>not</em> one of them -- it has its own
    /// <c>actors/character/_1stperson</c>.
    /// </remarks>
    [CorpusFact]
    public void ThreeFoldersHoldTwoProjectsEach()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var shared = cache.LocateSpeedProjects()
            .GroupBy(p => p.Folder!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => Path.GetFileName(g.Key), g => g.Select(p => p.Name).Order().ToList());

        Assert.Equal(46, cache.LocateSpeedProjects()
            .Select(p => p.Folder!).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Equal(3, shared.Count);
        Assert.Equal(["DogProject", "WolfProject"], shared["canine"]);
        Assert.Equal(["DefaultFemale", "DefaultMale"], shared["character"]);
        Assert.Equal(["DraugrProject", "DraugrSkeletonProject"], shared["draugr"]);
    }

    /// <summary>
    /// The folder is enough to open the project: every one of the 49 yields a
    /// character file and at least one behaviour graph.
    /// </summary>
    /// <remarks>
    /// This is the part that says the mapping is not merely a path that exists.
    /// Opening a project resolves the character file named by the packfile and the
    /// behaviour files named by the animation cache, both relative to this folder,
    /// and every one of them lands. 125 behaviour files across the 49.
    /// </remarks>
    [CorpusFact]
    public void EveryLocatedProjectOpensWithItsCharacterAndBehaviours()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int opened = 0, behaviours = 0;
        var missing = new List<string>();

        foreach (ProjectLocation located in cache.LocateSpeedProjects())
        {
            ActorProject? project = cache.OpenActor(located.Name);
            Assert.NotNull(project);

            Assert.Equal(located.Folder, project.Folder);
            Assert.True(project.HasHavok, $"{located.Name}: no character file");
            Assert.NotEmpty(project.Behaviors);

            opened++;
            behaviours += project.Behaviors.Count;
            missing.AddRange(project.MissingBehaviors.Select(m => $"{located.Name}: {m}"));
        }

        Assert.Equal(49, opened);
        Assert.Equal(125, behaviours);
        Assert.Empty(missing);
    }
}
