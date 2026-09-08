namespace HKSK.Cache;

/// <summary>
/// One animation set: which weapons it applies to, what it swaps, what it attacks
/// with, and which animation files it covers.
/// </summary>
/// <remarks>
/// This is the whole of one <c>animationsetdata/&lt;project&gt;data/&lt;set&gt;.txt</c>,
/// e.g. the chicken's <c>fullbody.txt</c>.
/// </remarks>
public sealed class ProjectAttackBlock
{
    /// <summary>
    /// The format version. Every one of the 990 sets in the shipped game says
    /// <c>V3</c>, so nothing else has been seen and nothing else is written.
    /// </summary>
    public string Version { get; set; } = "V3";

    /// <summary>
    /// The events that bring this set in, e.g. <c>WeapEquip</c>,
    /// <c>MagicForceEquip</c>, <c>swimForceEquip</c>.
    /// </summary>
    /// <remarks>
    /// Behaviour-graph event names: 1,910 of the 1,921 in the shipped game are
    /// declared by a behaviour the project lists, and the eleven that are not
    /// are Dawnguard and Dragonborn additions the base graphs never gained --
    /// the same drift the cache indices show.
    ///
    /// The commonest thing in the set data by far, and the only part most sets
    /// have: 791 of 990 carry swap events and nothing else.
    /// </remarks>
    public List<string> SwapEvents { get; set; } = [];

    public HandVariableData HandVariables { get; set; } = new();
    public ClipAttackBlock Attacks { get; set; } = new();
    public ClipFilesCrcBlock Checksums { get; set; } = new();

    /// <summary>
    /// Whether the set applies only for particular equipment.
    /// </summary>
    /// <remarks>
    /// This is <em>not</em> a way to tell an attack set from an idle one, which
    /// is what the presence of hand variables looks like it should mean and what
    /// an earlier version of this claimed. The shipped data says otherwise: 40
    /// sets carry attacks with no hand variables at all, and 68 carry hand
    /// variables with no attacks. The two are independent -- the variables say
    /// when the set applies, <see cref="Attacks"/> says what it can attack with.
    /// Ask <c>Attacks.Attacks.Count</c> for the other question.
    /// </remarks>
    public bool IsConditional => HandVariables.Variables.Count > 0;

    /// <summary>The old name for <see cref="IsConditional"/>, which described it wrongly.</summary>
    /// <remarks>
    /// Same value -- it always meant "has hand variables" -- but the name said
    /// it distinguished attack sets from idle ones, which the shipped data
    /// contradicts. Kept so 1.0.x callers still compile.
    /// </remarks>
    [Obsolete("Renamed to IsConditional: hand variables say when a set applies, not whether it attacks. " +
              "For attacks, ask Attacks.Attacks.Count.")]
    public bool IsAttackSet => IsConditional;

    public static ProjectAttackBlock Read(LineCursor c)
    {
        var block = new ProjectAttackBlock { Version = c.Line("the set data version") };

        int count = c.Int("the swap event count");
        for (int i = 0; i < count; i++) block.SwapEvents.Add(c.Line("a swap event"));

        block.HandVariables = HandVariableData.Read(c);
        block.Attacks = ClipAttackBlock.Read(c);
        block.Checksums = ClipFilesCrcBlock.Read(c);
        return block;
    }

    public void Write(LineWriter w)
    {
        w.Line(Version);
        w.Counted(SwapEvents, static (lw, e) => lw.Line(e));
        HandVariables.Write(w);
        Attacks.Write(w);
        Checksums.Write(w);
    }

    public ProjectAttackBlock Clone() => new()
    {
        Version = Version,
        SwapEvents = [.. SwapEvents],
        HandVariables = HandVariables.Clone(),
        Attacks = Attacks.Clone(),
        Checksums = Checksums.Clone(),
    };
}

/// <summary>
/// Every animation set of one project.
/// </summary>
/// <remarks>
/// The set names come first, then one block per name in the same order -- which
/// is what makes the list self-delimiting, since nothing else marks where a
/// block ends.
/// </remarks>
public sealed class ProjectAttackListBlock
{
    /// <summary>
    /// The set file names, e.g. <c>FullBody.txt</c>.
    /// </summary>
    /// <remarks>
    /// Always <c>.txt</c>, and unique within a creature -- they are file names
    /// in the split form, so two sets sharing one would share a file. A creature
    /// carries between 1 and 374 of them.
    /// </remarks>
    public List<string> SetFiles { get; set; } = [];

    public List<ProjectAttackBlock> Sets { get; set; } = [];

    public static ProjectAttackListBlock Read(LineCursor c)
    {
        var block = new ProjectAttackListBlock();

        int count = c.Int("the animation set count");
        for (int i = 0; i < count; i++) block.SetFiles.Add(c.Line("an animation set name"));

        for (int i = 0; i < block.SetFiles.Count; i++)
            block.Sets.Add(ProjectAttackBlock.Read(c));

        return block;
    }

    public void Write(LineWriter w)
    {
        w.Counted(SetFiles, static (lw, f) => lw.Line(f));
        foreach (ProjectAttackBlock set in Sets) set.Write(w);
    }

    /// <summary>The set with the given file name, case-insensitively.</summary>
    public ProjectAttackBlock? Set(string fileName)
    {
        int i = SetFiles.FindIndex(f => string.Equals(f, fileName, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : Sets[i];
    }

    public ProjectAttackListBlock Clone() => new()
    {
        SetFiles = [.. SetFiles],
        Sets = [.. Sets.Select(s => s.Clone())],
    };
}

/// <summary>One project's animation sets, under the key the set data lists it by.</summary>
public sealed class AnimationSetDataProject
{
    /// <summary>The key as listed, e.g. <c>ChickenProjectData\ChickenProject.txt</c>.</summary>
    /// <remarks>
    /// Always <c>&lt;Project&gt;Data\&lt;Project&gt;.txt</c> -- true of all 49
    /// in the shipped game -- which is also the path the split form writes it to
    /// under <c>animationsetdata/</c>.
    /// </remarks>
    public required string Name { get; set; }

    public required ProjectAttackListBlock Sets { get; set; }

    /// <summary>The project stem, e.g. <c>ChickenProject</c>.</summary>
    public string Stem => Path.GetFileNameWithoutExtension(Name.Replace('\\', '/'));
}

/// <summary>
/// <c>animationsetdatasinglefile.txt</c>: the attack and idle sets of every
/// creature project.
/// </summary>
/// <remarks>
/// Only actors appear here, and exactly the ones that carry an animation cache:
/// all 49 projects in this file are among the 49 the animation data caches, and
/// there are no others on either side. A project has animation set data if and
/// only if it has a clip cache. Everything else in the animation data -- props,
/// furniture, traps -- appears in neither.
///
/// Unlike the animation data, the blocks carry no line counts -- each is
/// self-delimiting, so the file is read until it runs out.
/// </remarks>
public sealed class AnimationSetDataFile
{
    public List<AnimationSetDataProject> Projects { get; set; } = [];

    public static AnimationSetDataFile Load(string path) => Parse(File.ReadAllLines(path));

    public static AnimationSetDataFile Parse(string text) => Parse(Split(text));

    public static AnimationSetDataFile Parse(IReadOnlyList<string> lines)
    {
        var c = new LineCursor(lines);
        var file = new AnimationSetDataFile();

        LineCursor names = c.Counted("the set project count");
        var order = new List<string>();
        while (names.More) order.Add(names.Line("a set project name"));

        foreach (string name in order)
            file.Projects.Add(new AnimationSetDataProject
            {
                Name = name,
                Sets = ProjectAttackListBlock.Read(c),
            });

        return file;
    }

    public string Write()
    {
        var w = new LineWriter();
        w.Counted(Projects, static (lw, p) => lw.Line(p.Name));
        foreach (AnimationSetDataProject project in Projects) project.Sets.Write(w);
        return w.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, Write());

    /// <summary>Finds a project by name or stem, case-insensitively.</summary>
    public AnimationSetDataProject? Project(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
        return Projects.FirstOrDefault(p =>
            string.Equals(p.Stem, stem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Splits file text into lines the way <see cref="File.ReadAllLines(string)"/> does.
    /// </summary>
    /// <remarks>
    /// Every line is CRLF-terminated including the last, so splitting on the
    /// newline leaves one empty element past the end that is not a line. Only
    /// that one is dropped: a cached project's movement block genuinely ends
    /// with a blank line, and trimming blank lines in general would eat it and
    /// leave the block a line short of the count in front of it.
    /// </remarks>
    private static string[] Split(string text)
    {
        string[] parts = text.Split('\n');
        int count = parts.Length > 0 && parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;

        var lines = new string[count];
        for (int i = 0; i < count; i++) lines[i] = parts[i].TrimEnd('\r');

        return lines;
    }
}
