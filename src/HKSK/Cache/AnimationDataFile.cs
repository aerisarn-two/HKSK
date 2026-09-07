namespace HKSK.Cache;

/// <summary>One project's animation data: its block, and its root motion.</summary>
public sealed class AnimationDataProject
{
    /// <summary>The project's name as listed, e.g. <c>ChickenProject.txt</c>.</summary>
    public required string Name { get; set; }

    public required ProjectBlock Block { get; set; }

    /// <summary>Present exactly when <see cref="ProjectBlock.HasAnimationCache"/> is set.</summary>
    public ProjectDataBlock? Movements { get; set; }

    /// <summary>The project name without its extension, e.g. <c>ChickenProject</c>.</summary>
    public string Stem => Path.GetFileNameWithoutExtension(Name.Replace('\\', '/'));
}

/// <summary>
/// <c>animationdatasinglefile.txt</c>: every project's clips and root motion.
/// </summary>
/// <remarks>
/// The format is a counted-block grammar. A project list comes first, then, for
/// each project in that order, a line count followed by that many lines of
/// project block, and -- only if the project block says it has an animation
/// cache -- a second line count and that many lines of movement block.
///
/// Nothing in the file is self-describing, so the line counts are the only thing
/// keeping a reader in step. They are recomputed on write from the content
/// actually produced rather than from a formula, which is what stops the count
/// and the block drifting apart.
/// </remarks>
public sealed class AnimationDataFile
{
    public List<AnimationDataProject> Projects { get; set; } = [];

    public static AnimationDataFile Load(string path) => Parse(File.ReadAllLines(path));

    public static AnimationDataFile Parse(string text) => Parse(Split(text));

    public static AnimationDataFile Parse(IReadOnlyList<string> lines)
    {
        // A trailing CRLF leaves a final empty element that is not a line.
        var c = new LineCursor(lines);
        var file = new AnimationDataFile();

        LineCursor names = c.Counted("the project count");
        var order = new List<string>();
        while (names.More) order.Add(names.Line("a project name"));

        foreach (string name in order)
        {
            ProjectBlock block = ProjectBlock.Read(c.Counted($"the block length of '{name}'"));

            ProjectDataBlock? movements = null;
            if (block.HasAnimationCache)
                movements = ProjectDataBlock.Read(c.Counted($"the movement length of '{name}'"));

            file.Projects.Add(new AnimationDataProject
            {
                Name = name,
                Block = block,
                Movements = movements,
            });
        }

        return file;
    }

    public string Write()
    {
        var w = new LineWriter();
        w.Counted(Projects, static (lw, p) => lw.Line(p.Name));

        foreach (AnimationDataProject project in Projects)
        {
            var block = new LineWriter();
            project.Block.Write(block);
            w.Int(block.Count);
            w.Append(block);

            if (!project.Block.HasAnimationCache) continue;

            if (project.Movements is null)
                throw new InvalidOperationException(
                    $"project '{project.Name}' says it has an animation cache but carries no movement block");

            var movements = new LineWriter();
            project.Movements.Write(movements);
            w.Int(movements.Count);
            w.Append(movements);
        }

        return w.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, Write());

    /// <summary>Finds a project by name or stem, case-insensitively.</summary>
    public AnimationDataProject? Project(string name)
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
