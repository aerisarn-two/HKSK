namespace HKSK.Cache;

/// <summary>A variable the equipped weapon drives, and the range it may take.</summary>
public sealed record HandVariable(string Name, int Min, int Max);

/// <summary>
/// The variables that say what the character is holding.
/// </summary>
/// <remarks>
/// The ranges index Skyrim's equipped-type enum: 0 hand-to-hand, 1 sword,
/// 2 dagger, 3 axe, 4 mace, 5 two-handed sword, 6 two-handed axe, 7 bow,
/// 8 staff, 9 spell, 10 shield, 11 crossbow. A set with no hand variables is an
/// idle set rather than an attack set.
/// </remarks>
public sealed class HandVariableData
{
    public List<HandVariable> Variables { get; set; } = [];

    public static HandVariableData Read(LineCursor c)
    {
        var data = new HandVariableData();
        int count = c.Int("the hand variable count");
        for (int i = 0; i < count; i++)
            data.Variables.Add(new HandVariable(
                c.Line("a hand variable name"),
                c.Int("a hand variable minimum"),
                c.Int("a hand variable maximum")));
        return data;
    }

    public void Write(LineWriter w) =>
        w.Counted(Variables, static (lw, v) => { lw.Line(v.Name); lw.Int(v.Min); lw.Int(v.Max); });

    public HandVariableData Clone() => new() { Variables = [.. Variables] };
}

/// <summary>One attack: the event that starts it and the clips it may pick.</summary>
public sealed class AttackData
{
    public string EventName { get; set; } = "";

    /// <summary>Non-zero when the attack also exists mirrored.</summary>
    public int Mirrored { get; set; }

    public List<string> Clips { get; set; } = [];

    public bool IsMirrored => Mirrored > 0;

    public AttackData Clone() => new()
    {
        EventName = EventName,
        Mirrored = Mirrored,
        Clips = [.. Clips],
    };
}

/// <summary>Every attack in one set.</summary>
public sealed class ClipAttackBlock
{
    public List<AttackData> Attacks { get; set; } = [];

    public static ClipAttackBlock Read(LineCursor c)
    {
        var block = new ClipAttackBlock();
        int count = c.Int("the attack count");

        for (int i = 0; i < count; i++)
        {
            var attack = new AttackData
            {
                EventName = c.Line("an attack event name"),
                Mirrored = c.Int("an attack mirrored flag"),
            };

            int clips = c.Int("an attack clip count");
            for (int j = 0; j < clips; j++) attack.Clips.Add(c.Line("an attack clip"));

            block.Attacks.Add(attack);
        }

        return block;
    }

    public void Write(LineWriter w) =>
        w.Counted(Attacks, static (lw, a) =>
        {
            lw.Line(a.EventName);
            lw.Int(a.Mirrored);
            lw.Counted(a.Clips, static (x, clip) => x.Line(clip));
        });

    public ClipAttackBlock Clone() => new() { Attacks = [.. Attacks.Select(a => a.Clone())] };
}

/// <summary>
/// The animation files a set covers, named by checksum.
/// </summary>
/// <remarks>
/// Stored as a count of animations followed by three lines each -- folder
/// checksum, name checksum, and the constant extension code. The count is of
/// triples, not of lines. See <see cref="HavokCrc"/>.
/// </remarks>
public sealed class ClipFilesCrcBlock
{
    /// <summary>The raw lines, three per animation.</summary>
    public List<string> Entries { get; set; } = [];

    /// <summary>The triples, as read.</summary>
    public IEnumerable<(string Folder, string Name, string Extension)> Triples()
    {
        for (int i = 0; i + 2 < Entries.Count; i += 3)
            yield return (Entries[i], Entries[i + 1], Entries[i + 2]);
    }

    /// <summary>Adds the checksums for one animation path.</summary>
    public void Add(string relativePath)
    {
        (string folder, string name, string extension) = HavokCrc.Triple(relativePath);
        Entries.Add(folder);
        Entries.Add(name);
        Entries.Add(extension);
    }

    public static ClipFilesCrcBlock Read(LineCursor c)
    {
        var block = new ClipFilesCrcBlock();
        int count = c.Int("the animation checksum count") * 3;
        for (int i = 0; i < count; i++) block.Entries.Add(c.Line("an animation checksum"));
        return block;
    }

    public void Write(LineWriter w)
    {
        if (Entries.Count % 3 != 0)
            throw new InvalidOperationException(
                $"the checksum block holds {Entries.Count} lines, which is not three per animation");

        w.Int(Entries.Count / 3);
        foreach (string entry in Entries) w.Line(entry);
    }

    public ClipFilesCrcBlock Clone() => new() { Entries = [.. Entries] };
}
