namespace HKSK.Cache;

/// <summary>
/// A behaviour variable the set is conditional on, and the values it may hold.
/// </summary>
/// <remarks>
/// <paramref name="Min"/> and <paramref name="Max"/> are inclusive and usually
/// equal, naming one value; a range picks a family, as
/// <c>iRightHandType 1..4</c> does for the one-handed weapons.
/// </remarks>
public sealed record HandVariable(string Name, int Min, int Max);

/// <summary>
/// The variables that decide when a set applies.
/// </summary>
/// <remarks>
/// These are behaviour-graph variables: all 386 in the shipped game are declared
/// in the variable list of a behaviour the project loads, which is what settles
/// what they are.
///
/// Named for what it mostly holds -- <c>iRightHandType</c> and
/// <c>iLeftHandType</c> are 297 of the 386 -- but it is not limited to hands:
/// <c>iWantMountedWeaponAnims</c> and <c>bWantMountedWeaponAnims</c> also
/// appear, with their own ranges.
///
/// The hand types run 0 to 12 in the shipped data. Naming them here would be
/// guesswork past what the files show -- ck-cmd's enum stops at 11, and the game
/// uses 12 -- so the values are left as values.
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
    /// <summary>
    /// The behaviour event that triggers the attack, e.g. <c>attackStart</c>.
    /// </summary>
    /// <remarks>
    /// A behaviour-graph event: 735 of the 737 in the shipped game are declared
    /// by a behaviour the project lists.
    /// </remarks>
    public string EventName { get; set; } = "";

    /// <summary>
    /// Whether the attack also exists mirrored. Only 0 and 1 occur in the
    /// shipped game, so it is a flag written as an integer.
    /// </summary>
    public int Mirrored { get; set; }

    /// <summary>
    /// The clips the attack may play, chosen between at runtime.
    /// </summary>
    /// <remarks>
    /// Clip generator names, matching the animation data's clip list: 776 of the
    /// 793 named in the shipped game are clips that project caches.
    ///
    /// Almost always exactly one -- of 737 attacks, 685 name a single clip, 50
    /// name several and 2 name none.
    /// </remarks>
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
///
/// An empty block is normal rather than a fault: 77 of the 990 sets in the
/// shipped game name no animations.
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
