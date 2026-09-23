using System.Text.RegularExpressions;

namespace HKSK.Assembly;

/// <summary>What a clip's name suggests it is, and how sure that is.</summary>
/// <param name="Roles">What it looks like. Empty where the name says nothing.</param>
/// <param name="Why">The part of the name that was read, for a person to check.</param>
public sealed record ReadRole(IReadOnlyList<AnimationRole> Roles, string Why)
{
    public override string ToString() => Roles.Count == 0
        ? "nothing recognised"
        : $"{string.Join("; ", Roles)} (from '{Why}')";
}

/// <summary>
/// Reads a guess at what an animation is out of the name it was given.
/// </summary>
/// <remarks>
/// <para>
/// The assembly route takes roles rather than names, deliberately, because the names
/// cannot be trusted: the game's own say <c>MTForward</c>, <c>WalkForward</c>,
/// <c>Forward_Walk</c>, <c>RunF</c> and <c>forwardWalk</c> for the same thing. But a
/// person handed a folder of 200 clips should not have to type 200 roles either, so
/// this guesses and a front end shows the guess to be corrected.
/// </para>
/// <para>
/// It is deliberately shy. A name it does not recognise gets no roles rather than a
/// wrong one, because an animation in the wrong role is worse than an animation in
/// none: a walk filed as an attack is a creature that lunges when it is asked to move,
/// and a missing walk is a refusal with a reason.
/// </para>
/// </remarks>
public static class RoleReader
{
    /// <summary>
    /// What a name may be, most particular first.
    /// </summary>
    /// <remarks>
    /// The order is the whole of the rule and it is deliberate, not alphabetical and not
    /// by length: <c>TurnLeft90</c> is a canned turn and not a turn in place, and
    /// <c>UnequipSword</c> is not an equip. Whichever matches first wins, so a reading
    /// that is a special case of another goes above it.
    /// </remarks>
    private static readonly (string Pattern, RoleKind Kind)[] Kinds =
    [
        // the particular readings, which are special cases of the ones below them
        (@"paired|kill\s*move", RoleKind.KillMoveVictim),
        (@"canned.*turn|turn[\w\s]*(90|180)", RoleKind.CannedTurn),
        (@"turn.*place|turn\w*loop|loop\w*turn|\bturn\b", RoleKind.TurnInPlace),
        (@"unequip|sheath", RoleKind.Unequip),
        (@"equip|\bdraw\b", RoleKind.Equip),
        (@"stand\s*up|get\s*up", RoleKind.GetUp),
        (@"resurrect|reanimate|revive|\braise\b", RoleKind.Reanimate),
        (@"bleed", RoleKind.BleedOutEnter),

        // and the general ones
        (@"idle", RoleKind.Idle),
        (@"walk|^mt|\bmt\b", RoleKind.Walk),
        (@"\btrot", RoleKind.Trot),
        (@"\brun|sprint", RoleKind.Run),
        (@"\bswim|paddle", RoleKind.Swim),
        (@"death|\bdie\b|dying", RoleKind.Death),
        (@"attack|swing|strike|slash|chop|bite|claw", RoleKind.Attack),
        (@"stagger|stumble", RoleKind.Stagger),
        (@"recoil|\bhit\b|hit\d|flinch", RoleKind.Recoil),
        (@"\bblock", RoleKind.Block),
        (@"bash", RoleKind.Bash),
        (@"\bcast|\bspell", RoleKind.UpperBodyCast),
        (@"\bfeed", RoleKind.Feed),
        (@"aggro|warn|roar|growl|taunt", RoleKind.AggroWarning),
    ];
    /// <summary>What the name suggests, or nothing where it suggests nothing.</summary>
    public static ReadRole Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string plain = Regex.Replace(name, @"[_\-.]+", " ").Trim();
        string lower = plain.ToLowerInvariant();

        RoleKind? kind = null;
        string why = "";
        foreach ((string pattern, RoleKind candidate) in Kinds)
        {
            Match match = Regex.Match(lower, pattern);
            if (!match.Success) continue;
            kind = candidate;
            why = match.Value;
            break;
        }

        if (kind is not { } found) return new ReadRole([], "");

        Heading heading = Bearing(lower);
        Side side = Which(lower);
        int angle = found == RoleKind.CannedTurn ? Degrees(lower) : 0;
        Stance stance = lower.Contains("combat") || lower.Contains("sword") || lower.Contains("hold")
            ? Stance.Combat : Stance.Default;

        // A locomotion clip with no heading in its name is the forward one, which is
        // what a creature with exactly one walk means by it.
        if (found is RoleKind.Walk or RoleKind.Run or RoleKind.Trot or RoleKind.Sprint or RoleKind.Swim
            && heading == Heading.None)
            heading = Heading.Forward;

        // An idle whose name says it is holding something is the combat idle.
        if (found == RoleKind.Idle && stance == Stance.Combat) found = RoleKind.CombatIdle;

        return new ReadRole([new AnimationRole(found, heading, side, angle, Stance: stance)], why);
    }

    /// <summary>Reads every name at once, keeping the order, which is the numbering.</summary>
    public static IReadOnlyList<(string Name, ReadRole Read)> All(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return [.. names.Select(n => (n, Of(n)))];
    }

    private static Heading Bearing(string lower) =>
        Regex.IsMatch(lower, @"back.*left|\bbl\b") ? Heading.BackLeft
        : Regex.IsMatch(lower, @"back.*right|\bbr\b") ? Heading.BackRight
        : Regex.IsMatch(lower, @"forward.*left|\bfl\b") ? Heading.ForwardLeft
        : Regex.IsMatch(lower, @"forward.*right|\bfr\b") ? Heading.ForwardRight
        : Regex.IsMatch(lower, @"back|\bb\b|reverse") ? Heading.Back
        : Regex.IsMatch(lower, @"left|\bl\b") ? Heading.Left
        : Regex.IsMatch(lower, @"right|\br\b") ? Heading.Right
        : Regex.IsMatch(lower, @"forward|\bf\b|fwd") ? Heading.Forward
        : Heading.None;

    private static Side Which(string lower) =>
        Regex.IsMatch(lower, @"left|\bl\b") ? Side.Left
        : Regex.IsMatch(lower, @"right|\br\b") ? Side.Right
        : Side.None;

    private static int Degrees(string lower) =>
        Regex.Match(lower, @"(180|90)") is { Success: true } m ? int.Parse(m.Value) : 90;
}
