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
public sealed partial class ActorProject : CacheProject
{
    private readonly List<AnimationSlot> _slots = [];
    private readonly List<Clip> _clips = [];

    private ActorProject(AnimationDataProject data) : base(data) { }

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
    /// Whether the animation list has been changed since it was read.
    /// </summary>
    /// <remarks>
    /// The list lives in the character packfile, not in the cache, so saving the
    /// cache alone does not persist it -- and a cache referring to slots the
    /// character file does not have is exactly the drift this library exists to
    /// prevent. <see cref="SaveCharacter"/> writes it back.
    /// </remarks>
    public bool CharacterModified { get; internal set; }

    /// <summary>
    /// Writes the character packfile back, persisting the animation list.
    /// </summary>
    /// <remarks>
    /// Separate from saving the cache because the two are different files and a
    /// caller may want to write them to different places. Both have to happen
    /// for an added or removed animation to be real.
    /// </remarks>
    /// <param name="path">Where to write. Defaults to where it was read from.</param>
    public void SaveCharacter(string? path = null)
    {
        if (Character?.File is null)
            throw new InvalidOperationException(
                $"'{Name}' has no character packfile to write: it was opened from the cache alone, " +
                "or built in memory.");

        Character.File.Save(path ?? Character.File.Path);
        CharacterModified = false;
    }

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
    public static ActorProject Open(
        AnimationDataProject data,
        AnimationSetDataProject? sets = null,
        string? projectHkx = null)
    {
        var project = new ActorProject(data) { Sets = sets };

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
    public static ActorProject Open(
        AnimationDataProject data,
        CharacterFile character,
        IEnumerable<BehaviorFile>? behaviors = null,
        AnimationSetDataProject? sets = null)
    {
        var project = new ActorProject(data)
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

    /// <summary>
    /// The folder the project's relative paths resolve against, when the Havok
    /// files were loaded.
    /// </summary>
    public string? Folder => ProjectFile?.Folder;

    /// <summary>
    /// The skeleton packfile the character is rigged to, resolved.
    /// </summary>
    /// <remarks>
    /// Taken from the character file's rig name, falling back to the cache's
    /// file list. Needed by anything that has to make sense of an animation's
    /// tracks, since an animation carries only transforms and the bones they
    /// drive live here.
    /// </remarks>
    public string? SkeletonPath
    {
        get
        {
            if (Folder is null) return null;

            if (Character is not null && Character.RigName.Length > 0 &&
                HavokPath.Resolve(Folder, Character.RigName) is { } rig)
                return rig;

            return Data.Block.Files
                .Where(f => Segment(f).StartsWith("character assets", StringComparison.OrdinalIgnoreCase))
                .Select(f => HavokPath.Resolve(Folder, f))
                .FirstOrDefault(p => p is not null);
        }
    }

    /// <summary>The animation packfile a slot names, resolved, or null if absent.</summary>
    public string? AnimationPath(AnimationSlot slot) =>
        Folder is null || slot.StoredName.Length == 0
            ? null
            : HavokPath.Resolve(Folder, slot.StoredName);

    /// <summary>
    /// Where a slot's animation packfile should be written, whether or not it
    /// exists yet.
    /// </summary>
    public string? AnimationTarget(AnimationSlot slot) =>
        Folder is null || slot.StoredName.Length == 0
            ? null
            : AnimationPath(slot) ?? Path.Combine(Folder, HavokPath.Normalise(slot.StoredName));

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
