namespace HKSK.Cache;

/// <summary>
/// A project's entry in the animation data: the Havok files it is made of, and
/// the clips its behaviour graphs define.
/// </summary>
/// <remarks>
/// This is the whole of <c>animationdata/&lt;project&gt;.txt</c>, and one block
/// inside <c>animationdatasinglefile.txt</c>.
///
/// A project without an animation cache carries only the file list; the clips
/// and the movement block that pairs with it are then both absent.
/// </remarks>
public sealed class ProjectBlock
{
    /// <summary>
    /// The Havok files the project is made of, as stored: Windows-separated and
    /// relative to the folder holding the project .hkx.
    /// </summary>
    /// <remarks>
    /// A restatement of what the project and character .hkx files already say --
    /// the behaviour graphs, the character, and the skeleton. Whether the list is
    /// present at all is itself recorded, because some projects ship without one.
    /// </remarks>
    public List<string> Files { get; set; } = [];

    /// <summary>Whether the file list is written at all.</summary>
    public bool HasFiles { get; set; }

    /// <summary>Whether this project carries clips and a movement block.</summary>
    public bool HasAnimationCache { get; set; }

    public List<ClipGeneratorEntry> Clips { get; set; } = [];

    public static ProjectBlock Read(LineCursor c)
    {
        var block = new ProjectBlock { HasFiles = c.Line("a project file list flag") == "1" };

        if (block.HasFiles)
        {
            LineCursor files = c.Counted("the project file count");
            while (files.More) block.Files.Add(files.Line("a project file"));
        }

        block.HasAnimationCache = c.Line("an animation cache flag") == "1";
        if (block.HasAnimationCache)
            while (c.More) block.Clips.Add(ClipGeneratorEntry.Read(c));

        return block;
    }

    public void Write(LineWriter w)
    {
        w.Bool(HasFiles);
        if (HasFiles) w.Counted(Files, static (lw, f) => lw.Line(f));

        w.Bool(HasAnimationCache);
        if (HasAnimationCache)
            foreach (ClipGeneratorEntry clip in Clips) clip.Write(w);
    }

    /// <summary>Finds a clip by name, case-insensitively as the game matches them.</summary>
    public ClipGeneratorEntry? Clip(string name) =>
        Clips.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public ProjectBlock Clone() => new()
    {
        HasFiles = HasFiles,
        HasAnimationCache = HasAnimationCache,
        Files = [.. Files],
        Clips = [.. Clips.Select(c => c.Clone())],
    };
}

/// <summary>
/// The root motion for every animation of one project.
/// </summary>
/// <remarks>
/// This is the whole of <c>animationdata/boundanims/anims_&lt;project&gt;.txt</c>,
/// and the block that follows a cached project inside the merged file.
/// </remarks>
public sealed class ProjectDataBlock
{
    public List<ClipMovement> Movements { get; set; } = [];

    public static ProjectDataBlock Read(LineCursor c)
    {
        var block = new ProjectDataBlock();
        while (c.More) block.Movements.Add(ClipMovement.Read(c));
        return block;
    }

    public void Write(LineWriter w)
    {
        foreach (ClipMovement m in Movements) m.Write(w);
    }

    /// <summary>The motion recorded for an animation slot, if there is one.</summary>
    public ClipMovement? For(int cacheIndex) =>
        Movements.FirstOrDefault(m => m.CacheIndex == cacheIndex);

    public ProjectDataBlock Clone() => new() { Movements = [.. Movements.Select(m => m.Clone())] };
}
