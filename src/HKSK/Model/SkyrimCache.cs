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
    /// the cache alone, with <see cref="HavokProject.HasHavok"/> false.
    /// </remarks>
    public HavokProject? Open(string projectName)
    {
        AnimationDataProject? data = AnimationData.Project(projectName);
        if (data is null) return null;

        return HavokProject.Open(data, SetData.Project(data.Stem), FindProjectFile(data.Stem));
    }

    /// <summary>Opens every project in the cache.</summary>
    public IEnumerable<HavokProject> OpenAll() =>
        AnimationData.Projects.Select(p =>
            HavokProject.Open(p, SetData.Project(p.Stem), FindProjectFile(p.Stem)));

    /// <summary>Locates a project's packfile under the meshes folder.</summary>
    public string? FindProjectFile(string projectStem)
    {
        if (MeshesFolder is null) return null;

        string actors = Path.Combine(MeshesFolder, "actors");
        if (!Directory.Exists(actors)) return null;

        _index ??= BuildIndex(actors);
        return _index.GetValueOrDefault(projectStem.ToLowerInvariant());
    }

    private Dictionary<string, string>? _index;

    // Project packfiles are scattered a couple of levels down under actors/ and
    // there is no manifest, so the folder is indexed once by file stem.
    private static Dictionary<string, string> BuildIndex(string actors)
    {
        var index = new Dictionary<string, string>();

        foreach (string file in Directory.EnumerateFiles(actors, "*.hkx", SearchOption.AllDirectories))
        {
            // Only the project files sit directly in an actor folder; characters,
            // behaviours and animations are all one level further down.
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            index.TryAdd(key, file);
        }

        return index;
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
