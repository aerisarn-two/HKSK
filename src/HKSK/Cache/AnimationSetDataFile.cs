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
    /// <summary>The format version, always <c>V3</c> in Skyrim.</summary>
    public string Version { get; set; } = "V3";

    /// <summary>Events that swap the animation set in.</summary>
    public List<string> SwapEvents { get; set; } = [];

    public HandVariableData HandVariables { get; set; } = new();
    public ClipAttackBlock Attacks { get; set; } = new();
    public ClipFilesCrcBlock Checksums { get; set; } = new();

    /// <summary>An idle set carries no hand variables; an attack set does.</summary>
    public bool IsAttackSet => HandVariables.Variables.Count > 0;

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
    /// <summary>The set file names, e.g. <c>FullBody.txt</c>.</summary>
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
/// Only creatures appear here. A project in the animation data with no entry in
/// the set data is a prop, a piece of furniture or a non-combat actor.
///
/// Unlike the animation data, the blocks carry no line counts -- each is
/// self-delimiting, so the file is read until it runs out.
/// </remarks>
public sealed class AnimationSetDataFile
{
    public List<AnimationSetDataProject> Projects { get; set; } = [];

    public static AnimationSetDataFile Load(string path) => Parse(File.ReadAllLines(path));

    public static AnimationSetDataFile Parse(string text) =>
        Parse(text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray());

    public static AnimationSetDataFile Parse(IReadOnlyList<string> lines)
    {
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines = lines.Take(lines.Count - 1).ToList();

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
}
