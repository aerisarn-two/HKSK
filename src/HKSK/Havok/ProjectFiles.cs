using HKX2;

namespace HKSK.Havok;

/// <summary>
/// The project packfile: <c>&lt;name&gt;project.hkx</c>.
/// </summary>
/// <remarks>
/// Holds almost nothing. In Skyrim it names the character files and leaves every
/// other field of <c>hkbProjectStringData</c> empty -- no animation path, no
/// behaviour list, no event names -- so it is a pointer to the character file
/// rather than a description of the project. The rest lives in the character
/// file and, restated, in the animation data.
/// </remarks>
public sealed class ProjectFile(HavokFile file, hkbProjectData data)
{
    public HavokFile File { get; } = file;
    public hkbProjectData Data { get; } = data;

    /// <summary>The folder the project's relative paths are resolved against.</summary>
    public string Folder { get; } = Path.GetDirectoryName(file.Path)!;

    /// <summary>The character files this project declares, as stored.</summary>
    public IList<string> CharacterFiles =>
        Data.m_stringData?.m_characterFilenames ?? Array.Empty<string>();

    public static ProjectFile Load(string path)
    {
        HavokFile file = HavokFile.Load(path);
        hkbProjectData? data = file.First<hkbProjectData>();

        return data is null
            ? throw new InvalidDataException($"'{path}' holds no hkbProjectData")
            : new ProjectFile(file, data);
    }
}

/// <summary>
/// The character packfile, which is where a project's animations are actually
/// listed.
/// </summary>
/// <remarks>
/// <see cref="AnimationNames"/> is the important part: its <em>order</em> is the
/// numbering the whole animation cache is built on. A clip's cache index is a
/// position in this list, and so is the key of every root motion block, so
/// inserting or removing an entry renumbers the cache. See
/// <c>HKSK.Model.HavokProject</c>.
/// </remarks>
public sealed class CharacterFile(HavokFile? file, hkbCharacterData data)
{
    /// <summary>The packfile this was read from, or null when it was built in memory.</summary>
    public HavokFile? File { get; } = file;

    public hkbCharacterData Data { get; } = data;

    private hkbCharacterStringData Strings =>
        Data.m_stringData ?? throw new InvalidDataException(
            $"{File?.Path ?? "the character"} holds no character string data");

    /// <summary>The character's name, e.g. <c>ChickenCharater</c>.</summary>
    public string Name => Strings.m_name;

    /// <summary>The skeleton, as stored, e.g. <c>Character Assets\skeleton.HKX</c>.</summary>
    public string RigName => Strings.m_rigName;

    public string RagdollName => Strings.m_ragdollName;

    /// <summary>The behaviour graph this character runs.</summary>
    public string BehaviorFilename => Strings.m_behaviorFilename;

    /// <summary>
    /// The animations available to the behaviour, in the order that defines
    /// every cache index in the project.
    /// </summary>
    public IList<string> AnimationNames => Strings.m_animationNames;

    public static CharacterFile Load(string path)
    {
        HavokFile file = HavokFile.Load(path);
        hkbCharacterData? data = file.First<hkbCharacterData>();

        return data is null
            ? throw new InvalidDataException($"'{path}' holds no hkbCharacterData")
            : new CharacterFile(file, data);
    }

    /// <summary>
    /// Wraps character data held in memory, for a project being built rather
    /// than read.
    /// </summary>
    public static CharacterFile FromData(hkbCharacterData data) => new(null, data);

    /// <summary>Builds character data from scratch, with the given animation list.</summary>
    public static CharacterFile Create(string name, IEnumerable<string> animationNames) =>
        FromData(new hkbCharacterData
        {
            m_stringData = new hkbCharacterStringData
            {
                m_name = name,
                m_animationNames = [.. animationNames],
            },
        });
}

/// <summary>
/// A behaviour packfile, read for the clip generators it defines.
/// </summary>
/// <remarks>
/// A project may list several, and clips are spread across them: the quadruped
/// creatures keep locomotion in a shared <c>QuadrupedBehavior.hkx</c> and only
/// their own idles in the named one. Any clip lookup therefore has to span every
/// behaviour the project lists, not just the character's own.
/// </remarks>
public sealed class BehaviorFile(HavokFile file)
{
    public HavokFile File { get; } = file;

    /// <summary>Every clip generator this graph defines.</summary>
    public IReadOnlyList<hkbClipGenerator> Clips { get; } = file.All<hkbClipGenerator>().ToList();

    public static BehaviorFile Load(string path) => new(HavokFile.Load(path));
}
