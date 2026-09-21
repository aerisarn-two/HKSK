using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using HkxAnimationFile = HKFBX.Hkx.HkxAnimationFile;

namespace HKSK.AnimationData;

/// <summary>
/// Brings a project's entry in <c>animationdatasinglefile.txt</c> up to date with its Havok files.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An entry is amended, never rebuilt from nothing</strong>, because two of its
/// parts are not in the Havok files at all and a third cannot be recovered from them:
/// </para>
/// <list type="bullet">
/// <item>the <strong>root motion</strong> -- a Skyrim animation's root bone does not move,
/// and the travel exists only here (<c>docs/animation-data.md</c> §0); it is set by
/// importing an animation, <see cref="ActorProject.SetRootMotion"/>, and carried;</item>
/// <item>the <strong>event lists</strong> -- derived from the annotations and the triggers
/// by a rule that reproduces the shipped lists only for most clips, so a cached list is
/// kept and only a new clip's is derived;</item>
/// <item>the <strong>order</strong> of the file list and the clips, which is the authoring
/// tool's: the humans' behaviours are listed neither depth nor breadth first.</item>
/// </list>
/// <para>
/// What the Havok files do state is made true: every behaviour the root graph reaches,
/// the character and the rig are listed, and a prop's animations after them; every clip
/// generator the graph reaches has a clip, with its playback speed and crop, and a new clip
/// is numbered by the slot its animation has in the character. Anything the files do not name
/// is kept: a clip with no generator, a file the walk does not reach, a motion no clip uses.
/// </para>
/// <para>
/// <strong>An existing clip keeps its number.</strong> The numbering is the character's list,
/// and <see cref="ActorProject.AddAnimation"/> and <see cref="ActorProject.RemoveAnimation"/>
/// keep the cache in step with it as it is edited. A number that disagrees with the list was
/// written that way: the horse's and the werewolf's clips are numbered past the end of their
/// characters' lists, 89 slots against 51 and 104 against 99. Renumbering them would move root
/// motion the amendment cannot check, so the disagreement is left for
/// <see cref="Validation.ConsistencyReport"/> to name.
/// </para>
/// <para>
/// So amending a project whose files have not changed changes nothing, which is what lets
/// a caller amend every project after adding one.
/// </para>
/// </remarks>
public static class AnimationDataGenerator
{
    /// <summary>Brings one project's entry up to date with its Havok files.</summary>
    /// <exception cref="ArgumentException">The cache does not list the project: <see cref="Add"/> it.</exception>
    /// <exception cref="InvalidOperationException">The project's Havok files cannot be found.</exception>
    public static Amendment Amend(SkyrimCache cache, string projectName)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        string stem = StemOf(projectName);
        AnimationDataProject existing = cache.AnimationData.Project(stem)
            ?? throw new ArgumentException($"the animation data lists no project '{stem}'; add it first", nameof(projectName));

        return Amend(existing, Inputs.Read(Locate(cache, stem, existing) ?? throw Missing(stem)));
    }

    /// <summary>
    /// Lists a project the cache does not have yet, at the end of the file, and fills its entry
    /// from its Havok files; a project already listed is amended instead.
    /// </summary>
    /// <param name="cache">The cache, read from the meshes folder the project's files are in.</param>
    /// <param name="projectName">The project's name: its packfile's, without the extension.</param>
    /// <param name="actor">
    /// Whether it is an actor, with an animation cache, rather than a prop. The Havok files do
    /// not say: 247 of the game's props have animations too, and all that separates the 49
    /// actors is that a race wears them -- a plugin's fact, which
    /// <see cref="Records.GameRecordRules.ActorProjects"/> reads. It is ignored for a project
    /// already listed, which keeps its kind; <see cref="SkyrimCache.PromoteToActor"/> changes it.
    /// </param>
    /// <exception cref="InvalidOperationException">The project's Havok files cannot be found.</exception>
    public static Amendment Add(SkyrimCache cache, string projectName, bool actor)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        string stem = StemOf(projectName);
        if (cache.AnimationData.Project(stem) is not null) return Amend(cache, stem);

        Inputs inputs = Inputs.Read(cache.FindProjectFile(stem) ?? throw Missing(stem));
        var added = new AnimationDataProject
        {
            Name = stem + ".txt",
            Block = new ProjectBlock { HasFiles = true, HasAnimationCache = actor },
            Movements = actor ? new ProjectDataBlock() : null,
        };

        Update(added, inputs);
        cache.AnimationData.Projects.Add(added);
        return Amendment.Added;
    }

    private static string StemOf(string projectName) =>
        Path.GetFileNameWithoutExtension(projectName.Replace('\\', '/'));

    private static InvalidOperationException Missing(string stem) =>
        new($"the Havok project file of '{stem}' cannot be found under the meshes folder");

    /// <summary>Amends every project whose Havok files are to hand.</summary>
    /// <remarks>
    /// Entry by entry rather than by name: the shipped cache lists <c>SmallBird01</c> twice,
    /// and a lookup by name only ever reaches the first.
    /// </remarks>
    /// <returns>The entries that changed, by name, in the file's order.</returns>
    public static IReadOnlyList<string> Amend(SkyrimCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);

        var changed = new List<string>();
        var read = new Dictionary<string, Inputs>(StringComparer.OrdinalIgnoreCase);

        foreach (AnimationDataProject project in cache.AnimationData.Projects)
        {
            if (Locate(cache, project.Stem, project) is not { } path) continue;
            if (!read.TryGetValue(path, out Inputs? inputs)) read[path] = inputs = Inputs.Read(path);

            if (Amend(project, inputs) != Amendment.Unchanged) changed.Add(project.Stem);
        }

        return changed;
    }

    /// <summary>
    /// The project's packfile: the one the cache finds by name, unless the files the entry
    /// already lists do not resolve beside it.
    /// </summary>
    /// <remarks>
    /// A name is not unique under the meshes folder. Five packfiles are called
    /// <c>moth.hkx</c>, and the one found first is an effect's, in <c>effects/</c>; the moth's
    /// project is <c>uniquebehaviors/moth/moth.hkx</c>, which is the one whose folder holds
    /// <c>Behaviors\Moth.hkx</c>.
    /// </remarks>
    private static string? Locate(SkyrimCache cache, string stem, AnimationDataProject? existing)
    {
        string? found = cache.FindProjectFile(stem);
        if (existing is not { Block.Files: [var first, ..] } || cache.MeshesFolder is null) return found;
        if (found is not null && HoldsProject(found, first)) return found;

        return Directory.EnumerateFiles(cache.MeshesFolder, stem + ".hkx", new EnumerationOptions
               {
                   RecurseSubdirectories = true,
                   MatchCasing = MatchCasing.CaseInsensitive,
               })
               .FirstOrDefault(candidate => HoldsProject(candidate, first)) ?? found;

        static bool HoldsProject(string candidate, string listed) =>
            HavokPath.Resolve(Path.GetDirectoryName(candidate)!, listed) is not null
            && HavokFile.Load(candidate).First<hkbProjectData>() is not null;
    }

    private static Amendment Amend(AnimationDataProject project, Inputs inputs)
    {
        string before = Text(project);
        Update(project, inputs);
        return Text(project) == before ? Amendment.Unchanged : Amendment.Replaced;
    }

    private static void Update(AnimationDataProject project, Inputs inputs)
    {
        if (project.Block.HasFiles)
            ListFiles(project.Block.Files, project.Block.HasAnimationCache
                ? inputs.Files
                : [.. inputs.Files, .. inputs.Character?.AnimationNames ?? []], inputs.Folder);
        if (!project.Block.HasAnimationCache || inputs.Character is null) return;

        project.Movements ??= new ProjectDataBlock();
        UpdateClips(project, inputs);
    }

    /// <summary>
    /// Adds what the files name and the list lacks, after what it has; nothing is removed or
    /// moved, since the order is the authoring tool's and a listed file the walk does not reach
    /// is not known to be unused.
    /// </summary>
    /// <remarks>
    /// A file is listed when an entry resolves to it, however it is spelled: the bow's character
    /// names its graph <c>..\Bow\Behaviors\BowBehavior.hkx</c> from the folder the list calls
    /// <c>Behaviors\BowBehavior.hkx</c>. A prop lists its animations after its rig -- all 247 of
    /// the shipped props that have animations -- and an actor never does.
    /// </remarks>
    private static void ListFiles(List<string> files, IEnumerable<string> named, string folder)
    {
        var listed = new HashSet<string>(files.Select(Key), StringComparer.OrdinalIgnoreCase);

        foreach (string file in named)
            if (listed.Add(Key(file))) files.Add(file);

        string Key(string stored) => HavokPath.Resolve(folder, stored) is { } path
            ? Path.GetFullPath(path)
            : stored.Replace('/', '\\');
    }

    private static void UpdateClips(AnimationDataProject project, Inputs inputs)
    {
        IList<string> slots = inputs.Character!.AnimationNames;

        foreach ((hkbClipGenerator generator, string file) in inputs.Generators)
        {
            int slot = SlotOf(slots, generator.m_animationName);
            ClipGeneratorEntry? clip = project.Block.Clips.FirstOrDefault(c => c.Name == generator.m_name);

            if (clip is null)
            {
                // A clip that plays nothing the character lists cannot be bound; the
                // consistency report names it.
                if (slot < 0) continue;

                clip = new ClipGeneratorEntry
                {
                    Name = generator.m_name,
                    CacheIndex = slot,
                    Events = [.. ClipEvents.Derive(generator, file, inputs, slot)],
                };
                project.Block.Clips.Add(clip);
            }

            // Only where the file would say something else: a value read back from the cache is
            // the generator's rounded to the text it is written as, and is not a change.
            clip.PlaybackSpeed = Written(clip.PlaybackSpeed, generator.m_playbackSpeed);
            clip.CropStartTime = Written(clip.CropStartTime, generator.m_cropStartAmountLocalTime);
            clip.CropEndTime = Written(clip.CropEndTime, generator.m_cropEndAmountLocalTime);
        }
    }

    private static float Written(float cached, float stated) =>
        CacheText.Float(cached) == CacheText.Float(stated) ? cached : stated;

    /// <summary>
    /// The slot of the animation a generator names: the same path, or failing that the same
    /// file name -- the female body's character lists <c>Animations\female\...</c> at the
    /// slots its graph's clips name as <c>Animations\male\...</c>.
    /// </summary>
    internal static int SlotOf(IList<string> slots, string animation)
    {
        for (int i = 0; i < slots.Count; i++)
            if (SamePath(slots[i], animation)) return i;

        for (int i = 0; i < slots.Count; i++)
            if (SameFile(slots[i], animation)) return i;

        return -1;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('/', '\\'), b.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFileName(a.Replace('\\', '/')), Path.GetFileName(b.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase);

    private static string Text(AnimationDataProject project) =>
        new AnimationDataFile { Projects = [project] }.Write();

    /// <summary>Everything an entry is made from, read once.</summary>
    internal sealed class Inputs
    {
        public required string ProjectPath { get; init; }
        public CharacterFile? Character { get; init; }

        /// <summary>The files an entry lists: the behaviours, the character, the rig.</summary>
        public required IReadOnlyList<string> Files { get; init; }

        /// <summary>Every clip generator the root graph reaches, once per name, with the file it is in.</summary>
        public required IReadOnlyList<(hkbClipGenerator Generator, string File)> Generators { get; init; }

        /// <summary>Each behaviour file's event names, by the file as the walk names it.</summary>
        public required IReadOnlyDictionary<string, Variables> Tables { get; init; }

        public string Folder => Path.GetDirectoryName(ProjectPath)!;

        public static Inputs Read(string projectPath)
        {
            ProjectFile project = ProjectFile.Load(projectPath);
            string folder = project.Folder;

            CharacterFile? character = project.CharacterFiles
                .Select(f => HavokPath.Resolve(folder, f))
                .Where(p => p is not null)
                .Select(p => CharacterFile.Load(p!))
                .FirstOrDefault();

            var files = new List<string>();
            var generators = new List<(hkbClipGenerator, string)>();
            var tables = new Dictionary<string, Variables>(StringComparer.OrdinalIgnoreCase);

            if (character is not null && !string.IsNullOrEmpty(character.BehaviorFilename))
            {
                // The root graph as the character names it, then every graph it reaches.
                files.Add(character.BehaviorFilename);
                var names = new HashSet<string>(StringComparer.Ordinal);

                foreach (ProjectStep step in ProjectVisitor.Visit(projectPath))
                    switch (step.Node)
                    {
                        case hkbBehaviorReferenceGenerator reference
                            when !files.Contains(reference.m_behaviorName, StringComparer.OrdinalIgnoreCase):
                            files.Add(reference.m_behaviorName);
                            break;
                        case hkbBehaviorGraph graph:
                            tables.TryAdd(step.File, Variables.Of(graph));
                            break;
                        // Clip names are matched exactly: Crossbow_IdleHeld and
                        // CrossBow_IdleHeld are two clips of the humans'.
                        case hkbClipGenerator clip when names.Add(clip.m_name):
                            generators.Add((clip, step.File));
                            break;
                    }
            }

            files.AddRange(project.CharacterFiles);
            if (character is not null && !string.IsNullOrEmpty(character.RigName)) files.Add(character.RigName);

            return new Inputs
            {
                ProjectPath = projectPath,
                Character = character,
                Files = files,
                Generators = generators,
                Tables = tables,
            };
        }

        /// <summary>The animation file in a slot, resolved.</summary>
        public string? AnimationPath(int slot) =>
            Character is not null && slot >= 0 && slot < Character.AnimationNames.Count
                ? HavokPath.Resolve(Folder, Character.AnimationNames[slot])
                : null;
    }

    /// <summary>A new clip's events, from its animation's annotations and its generator's triggers.</summary>
    /// <remarks>
    /// The rule <c>BehaviorAgreementTests</c> measured against the shipped lists: annotation
    /// times as they are, clamped to the clip's playing length; a trigger at its local time, or
    /// past the playing length by it when it is relative to the end; an annotation's text as the
    /// longest dotted prefix that names an event of the graph, and the whole text when none does.
    /// </remarks>
    internal static class ClipEvents
    {
        public static IEnumerable<ClipEvent> Derive(hkbClipGenerator generator, string file, Inputs inputs, int slot)
        {
            var annotated = new List<ClipEvent>();
            var triggered = new List<ClipEvent>();
            float speed = generator.m_playbackSpeed == 0f ? 1f : MathF.Abs(generator.m_playbackSpeed);

            float? length = null;
            if (inputs.AnimationPath(slot) is { } animation && File.Exists(animation))
            {
                var (spline, _, _, tracks) = HkxAnimationFile.ReadAnimationWithEvents(animation);
                length = (spline.Duration - generator.m_cropStartAmountLocalTime - generator.m_cropEndAmountLocalTime) / speed;

                HashSet<string> known = EventNames(inputs.Tables.GetValueOrDefault(file));
                foreach (var track in tracks)
                    foreach (var annotation in track.Events)
                        if (!string.IsNullOrEmpty(annotation.Text) && Named(annotation.Text, known) is { } name)
                            annotated.Add(new ClipEvent(name, MathF.Min(annotation.Time, length.Value)));
            }

            if (generator.m_triggers?.m_triggers is { } triggers)
            {
                Variables? table = inputs.Tables.GetValueOrDefault(file);
                foreach (hkbClipTrigger trigger in triggers)
                {
                    if (trigger.m_event?.m_id is not { } id || table?.EventNameOf(id) is not { } name) continue;

                    float time = trigger.m_relativeToEndOfClip ? (length ?? 0f) + trigger.m_localTime : trigger.m_localTime;
                    triggered.Add(new ClipEvent(name, time));
                }
            }

            // On a tie the trigger comes first, and an annotation restating a trigger is one event.
            foreach (ClipEvent trigger in triggered) annotated.Remove(trigger);
            return triggered.Concat(annotated).OrderBy(e => e.Time);
        }

        private static HashSet<string> EventNames(Variables? table)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (table is not null)
                for (int id = 0; table.EventNameOf(id) is { } name; id++) names.Add(name);
            return names;
        }

        private static string? Named(string text, HashSet<string> known)
        {
            for (string candidate = text; ; )
            {
                if (known.Contains(candidate)) return candidate;
                int dot = candidate.LastIndexOf('.');
                if (dot < 0) return null;
                candidate = candidate[..dot];
            }
        }
    }
}
