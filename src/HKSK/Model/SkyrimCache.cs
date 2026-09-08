using HKSK.Cache;
using HKSK.Havok;

namespace HKSK.Model;

/// <summary>
/// The animation cache of a Skyrim data folder, and the way into its projects.
/// </summary>
/// <remarks>
/// This is the entry point. Point it at an extracted <c>meshes</c> folder and it
/// reads the two merged files that the game actually loads:
///
/// <code>
///   meshes/animationdatasinglefile.txt      clips and root motion, per project
///   meshes/animationsetdatasinglefile.txt   attack and idle sets, per creature
/// </code>
///
/// The split per-project files under <c>animationdata/</c> and
/// <c>animationsetdata/</c> are the same content in a form the Creation Kit
/// writes; they are not what the game reads, and this treats the merged files as
/// authoritative. <see cref="Split"/> regenerates them.
///
/// Saving rewrites both merged files in full. That is safe to do repeatedly
/// because reading and writing are byte-exact -- a load-and-save with no edits
/// reproduces Bethesda's own bytes, which the test suite checks against the
/// shipped files.
/// </remarks>
public sealed class SkyrimCache
{
    /// <summary>The merged animation data file's name.</summary>
    public const string AnimationDataFileName = "animationdatasinglefile.txt";

    /// <summary>The merged animation set data file's name.</summary>
    public const string AnimationSetDataFileName = "animationsetdatasinglefile.txt";

    private SkyrimCache(string? meshes, AnimationDataFile data, AnimationSetDataFile sets)
    {
        MeshesFolder = meshes;
        AnimationData = data;
        SetData = sets;
    }

    /// <summary>The meshes folder this was read from, when it was read from one.</summary>
    public string? MeshesFolder { get; }

    public AnimationDataFile AnimationData { get; }
    public AnimationSetDataFile SetData { get; }

    /// <summary>Reads the cache from an extracted meshes folder.</summary>
    public static SkyrimCache Load(string meshesFolder)
    {
        string data = Path.Combine(meshesFolder, AnimationDataFileName);
        string sets = Path.Combine(meshesFolder, AnimationSetDataFileName);

        if (!File.Exists(data))
            throw new FileNotFoundException($"no {AnimationDataFileName} in '{meshesFolder}'", data);
        if (!File.Exists(sets))
            throw new FileNotFoundException($"no {AnimationSetDataFileName} in '{meshesFolder}'", sets);

        return new SkyrimCache(meshesFolder, AnimationDataFile.Load(data), AnimationSetDataFile.Load(sets));
    }

    /// <summary>Reads the cache from the two files' contents.</summary>
    public static SkyrimCache Parse(string animationData, string animationSetData) =>
        new(null, AnimationDataFile.Parse(animationData), AnimationSetDataFile.Parse(animationSetData));

    /// <summary>Wraps two already-built files, as <see cref="SplitCache.ToMerged()"/> does.</summary>
    public static SkyrimCache FromParts(AnimationDataFile data, AnimationSetDataFile sets) =>
        new(null, data, sets);

    /// <summary>
    /// Rebuilds a merged cache from the split, per-project files.
    /// </summary>
    /// <remarks>
    /// Do not point this at Skyrim's own <c>animationdata</c> folder and save the
    /// result over the merged file: the shipped split copy is a pre-DLC snapshot
    /// and would roll the game back. See <see cref="SplitCache"/>. This is for
    /// split files that have been brought up to date or authored fresh.
    /// </remarks>
    public static SkyrimCache FromSplit(string folder, out IReadOnlyList<SplitIssue> issues) =>
        SplitCache.Load(folder).ToMerged(out issues);

    /// <summary>As <see cref="FromSplit(string, out IReadOnlyList{SplitIssue})"/>, ignoring the report.</summary>
    public static SkyrimCache FromSplit(string folder) => FromSplit(folder, out _);

    /// <summary>The project names, in the order the cache lists them.</summary>
    public IEnumerable<string> ProjectNames => AnimationData.Projects.Select(p => p.Stem);

    /// <summary>
    /// Opens one project, resolving its Havok files when they are to hand.
    /// </summary>
    /// <remarks>
    /// The project packfile is found by name under <c>actors/</c>: the cache
    /// lists <c>ChickenProject.txt</c> and the packfile is
    /// <c>actors/ambient/chicken/chickenproject.hkx</c>. When it is not there --
    /// a cache extracted without the meshes, say -- the project still opens from
    /// the cache alone, with <see cref="ActorProject.HasHavok"/> false.
    /// </remarks>
    public CacheProject? Open(string projectName)
    {
        AnimationDataProject? data = AnimationData.Project(projectName);
        return data is null ? null : OpenProject(data);
    }

    /// <summary>
    /// Opens one project, expecting an actor.
    /// </summary>
    /// <remarks>
    /// Returns null both when there is no such project and when it is a prop,
    /// which is usually what a caller wanting animations means. Use
    /// <see cref="Open"/> to tell those apart.
    /// </remarks>
    public ActorProject? OpenActor(string projectName) => Open(projectName) as ActorProject;

    /// <summary>Opens every project in the cache, of both kinds.</summary>
    public IEnumerable<CacheProject> OpenAll() => AnimationData.Projects.Select(OpenProject);

    /// <summary>Every project that has animations: 49 of the game's 429.</summary>
    public IEnumerable<ActorProject> Actors() => OpenAll().OfType<ActorProject>();

    /// <summary>Every project that has not: doors, windmills, pillars.</summary>
    public IEnumerable<PropProject> Props() => OpenAll().OfType<PropProject>();

    /// <summary>
    /// Decides which kind a project is, from the one field that separates them.
    /// </summary>
    /// <remarks>
    /// The animation cache flag, not the presence of set data: the two agree
    /// throughout the shipped game, but the flag is what the file itself
    /// declares, and a cached project missing its set entry should open as the
    /// actor it claims to be and then fail validation saying so.
    /// </remarks>
    private CacheProject OpenProject(AnimationDataProject data) =>
        data.Block.HasAnimationCache
            ? ActorProject.Open(data, SetData.Project(data.Stem), FindProjectFile(data.Stem))
            : new PropProject(data);

    /// <summary>Locates a project's packfile under the meshes folder.</summary>
    /// <remarks>
    /// Actors live under <c>actors/</c>, but props do not: a door is beside its
    /// door, a windmill beside its farmhouse, so they turn up in
    /// <c>animbehaviors/</c>, <c>architecture/</c> and <c>clutter/</c>. The whole
    /// tree is indexed, which costs little -- 7,699 packfiles in the shipped
    /// game -- and <c>actors/</c> is indexed first so it wins any collision on a
    /// name.
    /// </remarks>
    public string? FindProjectFile(string projectStem)
    {
        if (MeshesFolder is null) return null;

        _index ??= BuildIndex(MeshesFolder);
        return _index.GetValueOrDefault(projectStem.ToLowerInvariant());
    }

    private Dictionary<string, string>? _index;

    // There is no manifest, so the tree is indexed once by file stem.
    private static Dictionary<string, string> BuildIndex(string meshes)
    {
        var index = new Dictionary<string, string>();

        string actors = Path.Combine(meshes, "actors");
        if (Directory.Exists(actors)) Add(index, actors);

        Add(index, meshes);
        return index;

        static void Add(Dictionary<string, string> index, string folder)
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories))
                index.TryAdd(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), file);
        }
    }

    /// <summary>
    /// Gives a prop an animation cache, making it an actor.
    /// </summary>
    /// <remarks>
    /// The only way a project changes kind, and it is deliberately not something
    /// that happens as a side effect of adding a clip. A cached project has
    /// animation set data in every one of the 49 the game ships, so the set
    /// entry is created here too -- an actor without one is half-made, and the
    /// validator says so.
    /// </remarks>
    /// <param name="prop">The project to promote. It keeps its file list.</param>
    /// <param name="character">
    /// The character file whose animation list will number the new cache.
    /// </param>
    /// <param name="setFileName">The name of the first animation set.</param>
    public ActorProject PromoteToActor(
        PropProject prop, Havok.CharacterFile character, string setFileName = "FullBody.txt")
    {
        ArgumentNullException.ThrowIfNull(prop);
        ArgumentNullException.ThrowIfNull(character);

        if (!AnimationData.Projects.Contains(prop.Data))
            throw new ArgumentException($"'{prop.Name}' does not belong to this cache", nameof(prop));

        prop.Data.Block.HasAnimationCache = true;
        prop.Data.Movements ??= new ProjectDataBlock();

        AnimationSetDataProject? sets = SetData.Project(prop.Name);

        if (sets is null)
        {
            sets = new AnimationSetDataProject
            {
                Name = $@"{prop.Name}Data\{prop.Name}.txt",
                Sets = new ProjectAttackListBlock
                {
                    SetFiles = [setFileName],
                    Sets = [new ProjectAttackBlock()],
                },
            };

            SetData.Projects.Add(sets);
        }

        return ActorProject.Open(prop.Data, character, sets: sets);
    }

    /// <summary>Writes both merged files back to the meshes folder.</summary>
    public void Save() =>
        Save(MeshesFolder ?? throw new InvalidOperationException(
            "this cache was parsed from text and has no folder; pass one to Save"));

    /// <summary>Writes both merged files to a folder.</summary>
    public void Save(string meshesFolder)
    {
        Directory.CreateDirectory(meshesFolder);
        AnimationData.Save(Path.Combine(meshesFolder, AnimationDataFileName));
        SetData.Save(Path.Combine(meshesFolder, AnimationSetDataFileName));
    }

    /// <summary>
    /// Writes the cache out as the per-project files the Creation Kit uses.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="FromSplit(string)"/>: one file per project
    /// under <c>animationdata/</c>, its root motion under
    /// <c>animationdata/boundanims/</c>, and each creature's sets under
    /// <c>animationsetdata/</c>, with both <c>dirlist.txt</c> files recording
    /// the order a later rebuild reads back.
    /// </remarks>
    public void Split(string outputFolder) => ToSplit().Save(outputFolder);

    /// <summary>Takes the split form of this cache without writing it anywhere.</summary>
    public SplitCache ToSplit() => SplitCache.FromMerged(this);
}
