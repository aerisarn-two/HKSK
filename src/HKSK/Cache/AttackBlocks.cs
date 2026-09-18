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
    /// Whether the attack is made while moving: 1 when combat measures its reach by the
    /// attacker's own movement, 0 when by the attack animation's root motion. Only 0 and 1
    /// occur in the shipped game; the game reads any value above 0 as set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the melee combat context (<c>0x1408a3d30</c>, reached from
    /// <c>CombatBehaviorContextMelee</c>). For each attack the actor's race can make with
    /// its hand types, it finds this entry by event name and asks the animation data
    /// (<c>0x140442a80</c>) for the clips' translation at the end, their translation at the
    /// <c>HitFrame</c> annotation, and the hit frame's time. All six translation floats are
    /// multiplied by the actor's scale, and a <em>reach</em> is recorded:
    /// </para>
    /// <list type="bullet">
    /// <item><strong>set</strong>: -1, the constant at <c>0x141769578</c>;</item>
    /// <item><strong>clear</strong>: the length of the scaled end translation,
    /// <c>sqrt(x*x + y*y + z*z)</c> -- zero for a clip that does not travel.</item>
    /// </list>
    /// <para>
    /// The entry keeps that reach at <c>+0x20</c> and the hit frame's time at <c>+0x24</c>.
    /// The actor's longest reach, a running <c>maxss</c> at <c>+0x30</c> of the context,
    /// rises with a cleared flag and never with a set one, -1 losing every comparison.
    /// </para>
    /// <para>
    /// The attack check (<c>0x1408a2ee0</c>) uses that reach twice, and the flag is
    /// <strong>which of two estimates it trusts, not a bonus added to the other</strong>.
    /// </para>
    /// <para>
    /// First an early-out. A reach of zero or more skips the attacker's own speed altogether
    /// -- the call that fetches it sits inside the branch -- so the distance the attack is
    /// allowed is the clip's root motion, plus the base the context carries, plus one combat
    /// setting, plus the <em>target's</em> speed times the time to the hit frame. Squared,
    /// compared with the distance between the two actors, and false -- no attack -- when they
    /// are farther apart. With the flag set the first term is the attacker's own speed times
    /// that same time <em>instead of</em> the clip's travel.
    /// </para>
    /// <para>
    /// Then the finer test, on predicted positions. The hit frame's translation is rotated
    /// into the actor's frame and added to its position to say where the blow lands, whatever
    /// the flag; the reach is compared with zero a second time (<c>0x1408a31bd</c>) to predict
    /// <em>where the attacker will be</em> when it does. Set: <c>0x140853fb0</c> projects the
    /// actor forward along its own motion for that time. Clear: the end translation, rotated,
    /// is added to its position -- but only when the reach exceeds <strong>5.0</strong> units;
    /// at or below that the branch is skipped and the actor is modelled as not moving. The
    /// target is predicted the same way, and the distance between the two predictions carries
    /// the rest of the check.
    /// </para>
    /// <para>
    /// So for an attack whose clips do not travel, leaving this clear contributes 0 to the
    /// allowed distance and fails the 5.0 gate: the actor waits until it is already within
    /// the base reach, and is treated as standing still for the whole swing. The shipped game
    /// does exactly that on 21 attacks, so it is not plainly a mistake to copy.
    /// </para>
    /// <para>
    /// It is set on attacks whose travel is the character's locomotion rather than the
    /// clip's -- the player's regular, sprint and hand-to-hand attacks, the werewolf's
    /// running and side ones -- and clear on lunges and power attacks, whose root motion is
    /// where the blow lands. Only 6 of the 45 projects with attacks use it at all, and
    /// nothing in the races, the character files or the behaviours distinguishes those six.
    /// Most of it <em>is</em> derivable, and <see cref="HKSK.SetData.SetDataGenerator"/> derives
    /// it: a blender above the clips parametric on the actor's speed, so no single root motion
    /// exists; a state that raises <c>IsSprinting</c>, the same case with no blend because
    /// sprinting has one direction; or an idle the tree only chooses on the move. That covers 30
    /// of vanilla's 38; the rest are copied with <c>setgen --flags-from</c>.
    /// It was called "mirrored" until the executable was read; it has nothing to do with
    /// mirroring. <c>docs/animation-set-data.md</c> §4.6 and §6.
    /// </para>
    /// </remarks>
    public int MovingAttack { get; set; }

    /// <summary>The old name for <see cref="MovingAttack"/>, which described it wrongly.</summary>
    [Obsolete("Renamed to MovingAttack: the flag says whether combat measures the attack's reach by the attacker's movement, and has nothing to do with mirroring.")]
    public int Mirrored { get => MovingAttack; set => MovingAttack = value; }

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

    public bool IsMovingAttack => MovingAttack > 0;

    /// <summary>The old name for <see cref="IsMovingAttack"/>.</summary>
    [Obsolete("Renamed to IsMovingAttack.")]
    public bool IsMirrored => IsMovingAttack;

    public AttackData Clone() => new()
    {
        EventName = EventName,
        MovingAttack = MovingAttack,
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
                MovingAttack = c.Int("an attack moving-attack flag"),
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
            lw.Int(a.MovingAttack);
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
