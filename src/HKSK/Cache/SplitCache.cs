using HKSK.Havok;
using HKSK.Model;

namespace HKSK.Cache;

/// <summary>One project as the split form stores it.</summary>
public sealed class SplitProject
{
    /// <summary>The file name as listed, e.g. <c>ChickenProject.txt</c>.</summary>
    public required string Name { get; set; }

    public required ProjectBlock Block { get; set; }

    /// <summary>From <c>boundanims/anims_&lt;name&gt;</c>, when the project has a cache.</summary>
    public ProjectDataBlock? Movements { get; set; }
}

/// <summary>One creature's animation sets as the split form stores them.</summary>
public sealed class SplitSetProject
{
    /// <summary>The key as listed, e.g. <c>ChickenProjectData\ChickenProject.txt</c>.</summary>
    public required string Name { get; set; }

    public required ProjectAttackListBlock Sets { get; set; }
}

/// <summary>Something the split form could not account for.</summary>
public sealed record SplitIssue(string Kind, string Message)
{
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>
/// The cache in its split, per-project form -- what the Creation Kit writes and
/// what a project is edited as.
/// </summary>
/// <remarks>
/// <code>
///   animationdata/dirlist.txt                    the project order
///   animationdata/&lt;project&gt;.txt                 clips
///   animationdata/boundanims/anims_&lt;project&gt;.txt root motion
///   animationsetdata/dirlist.txt                 the creature order
///   animationsetdata/&lt;project&gt;data/&lt;project&gt;.txt the set names
///   animationsetdata/&lt;project&gt;data/&lt;set&gt;.txt     each set
/// </code>
///
/// <b>The copy shipped in Skyrim's BSAs is stale and must not be merged as it
/// stands.</b> It is a pre-DLC snapshot: its <c>dirlist.txt</c> names 328
/// projects where the merged file carries 429, the 96 Dawnguard and Dragonborn
/// creatures are missing entirely, and nine of the projects it does hold -- the
/// dragon, draugr, falmer, horse, werewolf, both player characters and the first
/// person rig -- have fewer clips than the merged file and cache indices
/// numbered against older animation lists. Merging it over
/// <c>animationdatasinglefile.txt</c> would roll the game back.
///
/// So the merged file is the source of truth for reading. This type exists for
/// the other direction: to write the split form out, edit it, and rebuild the
/// merged file from it once it has been brought up to date.
/// <see cref="ToMerged"/> reports what it could not account for rather than
/// quietly dropping it.
/// </remarks>
public sealed class SplitCache
{
    public const string AnimationDataFolder = "animationdata";
    public const string BoundAnimsFolder = "boundanims";
    public const string AnimationSetDataFolder = "animationsetdata";
    public const string DirListName = "dirlist.txt";

    /// <summary>The project order, from <c>animationdata/dirlist.txt</c>.</summary>
    public DirList ProjectOrder { get; set; } = new();

    /// <summary>The creature order, from <c>animationsetdata/dirlist.txt</c>.</summary>
    public DirList SetOrder { get; set; } = new();

    public List<SplitProject> Projects { get; set; } = [];
    public List<SplitSetProject> Sets { get; set; } = [];

    /// <summary>
    /// Reads the split form from a folder holding <c>animationdata</c> and
    /// <c>animationsetdata</c>.
    /// </summary>
    /// <remarks>
    /// The order comes from the two <c>dirlist.txt</c> files, because nothing
    /// else records it -- a directory listing is not an order. Project files
    /// present but unlisted are still read, and appended after the listed ones
    /// in name order, so a project added without updating the listing is not
    /// silently lost. <see cref="ToMerged"/> reports them.
    /// </remarks>
    public static SplitCache Load(string folder)
    {
        string dataFolder = Path.Combine(folder, AnimationDataFolder);
        string setFolder = Path.Combine(folder, AnimationSetDataFolder);

        if (!Directory.Exists(dataFolder))
            throw new DirectoryNotFoundException($"no '{AnimationDataFolder}' folder in '{folder}'");

        var cache = new SplitCache();

        string listPath = Find(dataFolder, DirListName);
        if (File.Exists(listPath)) cache.ProjectOrder = DirList.Load(listPath);

        foreach (string name in OrderedNames(dataFolder, cache.ProjectOrder))
        {
            string path = Find(dataFolder, name);
            if (!File.Exists(path)) continue;

            ProjectBlock block = ProjectBlock.Read(Cursor(path));

            ProjectDataBlock? movements = null;
            string motion = Find(Path.Combine(dataFolder, BoundAnimsFolder), $"anims_{name}");
            if (File.Exists(motion)) movements = ProjectDataBlock.Read(Cursor(motion));

            cache.Projects.Add(new SplitProject { Name = name, Block = block, Movements = movements });
        }

        if (!Directory.Exists(setFolder)) return cache;

        string setListPath = Find(setFolder, DirListName);
        if (File.Exists(setListPath)) cache.SetOrder = DirList.Load(setListPath);

        foreach (string name in cache.SetOrder.Entries)
        {
            string? path = HavokPath.Resolve(setFolder, name);
            if (path is null) continue;

            // The named file lists the set files; each of those is a set block.
            var list = new ProjectAttackListBlock
            {
                SetFiles = [.. File.ReadAllLines(path).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)],
            };

            string owner = Path.GetDirectoryName(path)!;
            foreach (string setFile in list.SetFiles)
            {
                string setPath = Find(owner, setFile);
                if (!File.Exists(setPath)) continue;

                list.Sets.Add(ProjectAttackBlock.Read(Cursor(setPath)));
            }

            cache.Sets.Add(new SplitSetProject { Name = name, Sets = list });
        }

        return cache;
    }

    private static IEnumerable<string> OrderedNames(string folder, DirList order)
    {
        var listed = new List<string>(order.Entries);
        var seen = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);

        var extra = Directory.EnumerateFiles(folder, "*.txt")
            .Select(Path.GetFileName)
            .Where(n => n is not null && !n.Equals(DirListName, StringComparison.OrdinalIgnoreCase))
            .Select(n => n!)
            .Where(n => !seen.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return listed.Concat(extra);
    }

    /// <summary>Resolves a name in a folder ignoring case, as the game does.</summary>
    private static string Find(string folder, string name)
    {
        string direct = Path.Combine(folder, name);
        if (File.Exists(direct) || !Directory.Exists(folder)) return direct;

        return Directory.EnumerateFiles(folder).FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) ?? direct;
    }

    private static LineCursor Cursor(string path)
    {
        var lines = File.ReadAllLines(path).Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new LineCursor(lines);
    }

    /// <summary>
    /// Rebuilds the merged files from the split ones.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="FromMerged"/>. Order follows the two listings,
    /// which is what makes the result reproducible rather than dependent on how
    /// the filesystem happens to enumerate.
    ///
    /// Read <paramref name="issues"/> before trusting the result: a listing
    /// naming a project with no file, or a project file nothing lists, is
    /// exactly the state the shipped split copy is in.
    /// </remarks>
    public SkyrimCache ToMerged(out IReadOnlyList<SplitIssue> issues)
    {
        var found = new List<SplitIssue>();

        var listed = new HashSet<string>(ProjectOrder.Entries, StringComparer.OrdinalIgnoreCase);
        foreach (string name in ProjectOrder.Entries)
            if (!Projects.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                found.Add(new SplitIssue("missing-project",
                    $"'{name}' is listed in {AnimationDataFolder}/{DirListName} but has no file"));

        foreach (SplitProject project in Projects)
            if (!listed.Contains(project.Name))
                found.Add(new SplitIssue("unlisted-project",
                    $"'{project.Name}' has a file but is not listed in {AnimationDataFolder}/{DirListName}"));

        // A name is a file name in the split form, so two projects sharing one
        // share a file and cannot differ. Skyrim's merged file does list ten
        // names twice -- harmlessly, since each pair is identical and none has a
        // cache -- but an edit that made them differ could not survive a split.
        foreach (var duplicate in ProjectOrder.Entries
                     .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            found.Add(new SplitIssue("duplicate-project",
                $"'{duplicate.Key}' is listed {duplicate.Count()} times; the split form keeps one " +
                "file per name, so these are copies of it"));

        var data = new AnimationDataFile();

        foreach (SplitProject project in Projects)
        {
            if (project.Block.HasAnimationCache && project.Movements is null)
                found.Add(new SplitIssue("missing-movements",
                    $"'{project.Name}' declares an animation cache but has no " +
                    $"{BoundAnimsFolder}/anims_{project.Name}"));

            data.Projects.Add(new AnimationDataProject
            {
                Name = project.Name,
                Block = project.Block,
                Movements = project.Block.HasAnimationCache
                    ? project.Movements ?? new ProjectDataBlock()
                    : null,
            });
        }

        var sets = new AnimationSetDataFile();
        foreach (SplitSetProject set in Sets)
        {
            if (set.Sets.SetFiles.Count != set.Sets.Sets.Count)
                found.Add(new SplitIssue("missing-set",
                    $"'{set.Name}' lists {set.Sets.SetFiles.Count} sets but only " +
                    $"{set.Sets.Sets.Count} were found"));

            sets.Projects.Add(new AnimationSetDataProject { Name = set.Name, Sets = set.Sets });
        }

        issues = found;
        return SkyrimCache.FromParts(data, sets);
    }

    /// <summary>Rebuilds the merged files, ignoring what could not be accounted for.</summary>
    public SkyrimCache ToMerged() => ToMerged(out _);

    /// <summary>Takes the split form of a merged cache.</summary>
    public static SplitCache FromMerged(SkyrimCache cache)
    {
        var split = new SplitCache();

        foreach (AnimationDataProject project in cache.AnimationData.Projects)
        {
            split.ProjectOrder.Entries.Add(project.Name);
            split.Projects.Add(new SplitProject
            {
                Name = project.Name,
                Block = project.Block,
                Movements = project.Movements,
            });
        }

        foreach (AnimationSetDataProject set in cache.SetData.Projects)
        {
            split.SetOrder.Entries.Add(set.Name);
            split.Sets.Add(new SplitSetProject { Name = set.Name, Sets = set.Sets });
        }

        return split;
    }

    /// <summary>Writes the split form out.</summary>
    public void Save(string folder)
    {
        string dataFolder = Path.Combine(folder, AnimationDataFolder);
        string boundAnims = Path.Combine(dataFolder, BoundAnimsFolder);
        string setFolder = Path.Combine(folder, AnimationSetDataFolder);

        Directory.CreateDirectory(boundAnims);
        Directory.CreateDirectory(setFolder);

        foreach (SplitProject project in Projects)
        {
            var block = new LineWriter();
            project.Block.Write(block);
            File.WriteAllText(Path.Combine(dataFolder, project.Name), block.ToString());

            if (project.Movements is null) continue;

            var movements = new LineWriter();
            project.Movements.Write(movements);
            File.WriteAllText(Path.Combine(boundAnims, $"anims_{project.Name}"), movements.ToString());
        }

        ProjectOrder.Save(Path.Combine(dataFolder, DirListName));

        foreach (SplitSetProject set in Sets)
        {
            string target = Path.Combine(setFolder, HavokPath.Normalise(set.Name));
            string owner = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(owner);

            var names = new LineWriter();
            foreach (string name in set.Sets.SetFiles) names.Line(name);
            File.WriteAllText(target, names.ToString());

            for (int i = 0; i < set.Sets.SetFiles.Count && i < set.Sets.Sets.Count; i++)
            {
                var block = new LineWriter();
                set.Sets.Sets[i].Write(block);
                File.WriteAllText(Path.Combine(owner, set.Sets.SetFiles[i]), block.ToString());
            }
        }

        SetOrder.Save(Path.Combine(setFolder, DirListName));
    }
}
