using HKSK.Cache;
using HKSK.Havok;

namespace HKSK.Model;

/// <summary>
/// One Skyrim Havok animation project, gathered from the files that describe it.
/// </summary>
/// <remarks>
/// A project is spread over two kinds of file that restate each other.
///
/// The Havok side is authored: <c>&lt;name&gt;project.hkx</c> points at a
/// character file, which names the skeleton, the behaviour graph, and the
/// animations available to it; the behaviour graphs define clip generators over
/// those animations.
///
/// The cache side is generated, and is what the game actually reads at runtime:
/// <c>animationdata/&lt;name&gt;.txt</c> restates the clips with their speeds,
/// crop times and events; <c>animationdata/boundanims/anims_&lt;name&gt;.txt</c>
/// restates each animation's root motion so the game can plan movement without
/// loading the animation; and <c>animationsetdata/</c> holds the attack and idle
/// sets, naming animations by checksum.
///
/// The two are tied together by one thing: <b>a clip's cache index is the
/// position of its animation in the character file's animation list</b>. Root
/// motion is keyed by that same index, so it belongs to the animation and is
/// shared by every clip playing it. This was checked against the shipped game
/// data -- of the 27 projects whose Havok files resolve, 23 agree on every
/// clip, and the four that do not are stale caches rather than a different rule
/// (see <see cref="Validation.ConsistencyReport"/>).
///
/// That single fact is what makes editing dangerous by hand: inserting an
/// animation anywhere but the end renumbers every slot after it, and the clip
/// entries and root motion blocks that refer to them all have to move in step.
/// <see cref="AddAnimation"/> and <see cref="RemoveAnimation"/> do that.
/// </remarks>
public sealed partial class HavokProject
{
    private readonly List<AnimationSlot> _slots = [];
    private readonly List<Clip> _clips = [];

    private HavokProject(string name, AnimationDataProject data)
    {
        Name = name;
        Data = data;
    }

    /// <summary>The project stem, e.g. <c>ChickenProject</c>.</summary>
    public string Name { get; }

    /// <summary>The project's entry in the animation data.</summary>
    public AnimationDataProject Data { get; }

    /// <summary>The project's animation sets, when it is a creature.</summary>
    public AnimationSetDataProject? Sets { get; private set; }

    /// <summary>The project packfile, when the Havok files were loaded.</summary>
    public ProjectFile? ProjectFile { get; private set; }

    /// <summary>The character packfile, when the Havok files were loaded.</summary>
    public CharacterFile? Character { get; private set; }

    /// <summary>The behaviour packfiles that resolved, in the order listed.</summary>
    public IReadOnlyList<BehaviorFile> Behaviors { get; private set; } = [];

    /// <summary>
    /// Behaviour files the project lists but which could not be found. Their
    /// clips are simply absent, which is why a clip may have no generator.
    /// </summary>
    public IReadOnlyList<string> MissingBehaviors { get; private set; } = [];

    /// <summary>The animation slots, indexed by cache index.</summary>
    public IReadOnlyList<AnimationSlot> Animations => _slots;

    /// <summary>The clips, in the order the cache lists them.</summary>
    public IReadOnlyList<Clip> Clips => _clips;

    /// <summary>Whether the project carries a clip cache at all.</summary>
    public bool HasCache => Data.Block.HasAnimationCache;

    /// <summary>Whether the Havok files were resolved and loaded.</summary>
    public bool HasHavok => Character is not null;

    /// <summary>
    /// Builds the unified view of one project.
    /// </summary>
    /// <param name="data">The project's animation data entry.</param>
    /// <param name="sets">Its animation set entry, if it is a creature.</param>
    /// <param name="projectHkx">
    /// The project packfile. When null, or when it cannot be resolved, the
    /// project is still usable from the cache alone -- slots then come from the
    /// root motion and clip indices rather than from the character file.
    /// </param>
    public static HavokProject Open(
        AnimationDataProject data,
        AnimationSetDataProject? sets = null,
        string? projectHkx = null)
    {
        var project = new HavokProject(data.Stem, data) { Sets = sets };

        if (projectHkx is not null && File.Exists(projectHkx))
            project.LoadHavok(projectHkx);

        project.Rebuild();
        return project;
    }

    /// <summary>
    /// Builds the view over Havok data already in hand, rather than reading it
    /// from disk.
    /// </summary>
    /// <remarks>
    /// For a project being assembled in memory, and for callers that have
    /// already loaded the packfiles themselves.
    /// </remarks>
    public static HavokProject Open(
        AnimationDataProject data,
        CharacterFile character,
        IEnumerable<BehaviorFile>? behaviors = null,
        AnimationSetDataProject? sets = null)
    {
        var project = new HavokProject(data.Stem, data)
        {
            Sets = sets,
            Character = character,
            Behaviors = [.. behaviors ?? []],
        };

        project.Rebuild();
        return project;
    }

    private void LoadHavok(string projectHkx)
    {
        ProjectFile = Havok.ProjectFile.Load(projectHkx);
        string folder = ProjectFile.Folder;

        // The cache's file list is the reliable one: it names every behaviour,
        // where the character file names only the entry graph.
        var missing = new List<string>();

        string? characterPath = ProjectFile.CharacterFiles
            .Select(f => HavokPath.Resolve(folder, f))
            .FirstOrDefault(p => p is not null);

        characterPath ??= Data.Block.Files
            .Where(IsCharacter)
            .Select(f => HavokPath.Resolve(folder, f))
            .FirstOrDefault(p => p is not null);

        if (characterPath is not null) Character = CharacterFile.Load(characterPath);

        var behaviors = new List<BehaviorFile>();
        foreach (string stored in Data.Block.Files.Where(IsBehavior))
        {
            string? path = HavokPath.Resolve(folder, stored);
            if (path is null) { missing.Add(stored); continue; }

            behaviors.Add(BehaviorFile.Load(path));
        }

        Behaviors = behaviors;
        MissingBehaviors = missing;
    }

    // The file list has no field saying what each entry is, so its role comes
    // from the folder it sits in. The folder is not always plainly named --
    // "Characters Dog\", "Behaviors Wolf\", "CharacterSkeleton\" all occur --
    // so this matches on the prefix rather than the whole segment.
    private static bool IsBehavior(string stored) =>
        Segment(stored).StartsWith("behaviors", StringComparison.OrdinalIgnoreCase);

    private static bool IsCharacter(string stored) =>
        Segment(stored).StartsWith("character", StringComparison.OrdinalIgnoreCase) &&
        !Segment(stored).StartsWith("character assets", StringComparison.OrdinalIgnoreCase);

    private static string Segment(string stored)
    {
        string[] parts = stored.Replace('/', '\\').Split('\\');
        return parts.Length < 2 ? "" : parts[^2];
    }

    /// <summary>
    /// Recomputes the slot and clip views from the underlying files.
    /// </summary>
    /// <remarks>
    /// Called after every structural edit. The views hold references into the
    /// cache blocks rather than copies of them, so value changes need no rebuild
    /// -- only changes to which slots or clips exist.
    /// </remarks>
    public void Rebuild()
    {
        _slots.Clear();
        _clips.Clear();

        ProjectDataBlock? movements = Data.Movements;

        // The character file defines the numbering. Without it, the highest
        // index actually referenced is the best that can be said.
        int count = Character?.AnimationNames.Count ?? HighestReferencedIndex() + 1;

        for (int i = 0; i < count; i++)
            _slots.Add(new AnimationSlot
            {
                Index = i,
                StoredName = Character is not null && i < Character.AnimationNames.Count
                    ? Character.AnimationNames[i]
                    : "",
                Motion = movements?.For(i),
            });

        var generators = new Dictionary<string, HKX2.hkbClipGenerator>(StringComparer.OrdinalIgnoreCase);
        foreach (BehaviorFile behavior in Behaviors)
            foreach (HKX2.hkbClipGenerator generator in behavior.Clips)
                generators[generator.m_name] = generator;

        foreach (ClipGeneratorEntry entry in Data.Block.Clips)
            _clips.Add(new Clip
            {
                Entry = entry,
                Generator = generators.GetValueOrDefault(entry.Name),
                Slot = entry.CacheIndex >= 0 && entry.CacheIndex < _slots.Count
                    ? _slots[entry.CacheIndex]
                    : null,
            });
    }

    private int HighestReferencedIndex()
    {
        int highest = -1;
        foreach (ClipGeneratorEntry clip in Data.Block.Clips) highest = Math.Max(highest, clip.CacheIndex);
        foreach (ClipMovement m in Data.Movements?.Movements ?? []) highest = Math.Max(highest, m.CacheIndex);
        return highest;
    }

    /// <summary>Finds a clip by name, as the game matches them.</summary>
    public Clip? Clip(string name) =>
        _clips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the slot holding a given animation, by stored name or bare stem.</summary>
    public AnimationSlot? Animation(string name)
    {
        AnimationSlot? exact = _slots.FirstOrDefault(s =>
            string.Equals(s.StoredName, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        string stem = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
        return _slots.FirstOrDefault(s => string.Equals(s.FileStem, stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every clip that plays a given slot.</summary>
    public IEnumerable<Clip> ClipsOf(AnimationSlot slot) =>
        _clips.Where(c => c.CacheIndex == slot.Index);
}
