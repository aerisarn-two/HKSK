namespace HKSK.Assembly;

/// <summary>What an animation is for, in the terms a behaviour is assembled from.</summary>
/// <remarks>
/// The shipped names are inconsistent -- <c>MTForward</c>, <c>WalkForward</c>,
/// <c>Forward_Walk</c>, <c>RunF</c>, <c>forwardWalk</c> -- and the graph binds an
/// animation by path while the cache binds it by list position, so nothing can be
/// inferred from a name. A caller says what each animation *is* instead.
/// </remarks>
public enum RoleKind
{
    Idle, IdleVariant, CombatIdle,
    Walk, Run, Trot, Sprint, Swim,
    TurnInPlace, CannedTurn, SwimTurn,
    Equip, Unequip,
    Attack, PowerAttack, Bash, Block, BlockHit,
    Recoil, Stagger,
    Death, GetUp, Reanimate,
    AggroWarning, BleedOutEnter, BleedOutLoop, BleedOutExit,
    Feed, LayDown, LayLoop, LayUp,
    KillMoveVictim, UpperBodyCast,
    Custom,
}

/// <summary>Which way a locomotion clip carries the creature.</summary>
public enum Heading { None, Forward, ForwardRight, Right, BackRight, Back, BackLeft, Left, ForwardLeft }

/// <summary>Which hand a turn, a side attack or a side recoil is for.</summary>
public enum Side { None, Left, Right }

/// <summary>Whether a clip is for a creature at ease or one with its weapon out.</summary>
public enum Stance { Default, Combat }

/// <summary>How hard a stagger or a recoil is.</summary>
public enum Magnitude { None, Small, Medium, Large }

/// <summary>One thing an animation is. An animation may be given several.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Heading">Which way it carries the creature, for the locomotion kinds.</param>
/// <param name="Side">Which hand, for turns and the sided attacks and recoils.</param>
/// <param name="Angle">Degrees, for a canned turn: 90 or 180.</param>
/// <param name="Magnitude">How hard, for staggers and recoils.</param>
/// <param name="Stance">At ease or with the weapon out.</param>
/// <param name="Name">
/// The attack's event name, the kill-move's name, or the event a custom idle answers.
/// </param>
/// <param name="Mirror">
/// True when the animation is to be used mirrored for the other side as well, which
/// is how one turn clip serves both and what the <c>[Mirrored]</c> clips are.
/// </param>
public sealed record AnimationRole(
    RoleKind Kind,
    Heading Heading = Heading.None,
    Side Side = Side.None,
    int Angle = 0,
    Magnitude Magnitude = Magnitude.None,
    Stance Stance = Stance.Default,
    string? Name = null,
    bool Mirror = false)
{
    public override string ToString()
    {
        var parts = new List<string> { Kind.ToString() };
        if (Heading != Heading.None) parts.Add(Heading.ToString());
        if (Side != Side.None) parts.Add(Side.ToString());
        if (Angle != 0) parts.Add($"{Angle} degrees");
        if (Magnitude != Magnitude.None) parts.Add(Magnitude.ToString());
        if (Stance != Stance.Default) parts.Add(Stance.ToString());
        if (Name is { Length: > 0 }) parts.Add($"'{Name}'");
        if (Mirror) parts.Add("mirrored");
        return string.Join(" ", parts);
    }
}

/// <summary>An animation and what it is for.</summary>
/// <param name="Path">
/// A Havok animation on disk, or an FBX to import from. The order of the list these
/// come in is every clip's cache index, so it is appended to and never inserted into.
/// </param>
/// <param name="Roles">What it is. One animation may fill several roles.</param>
/// <param name="Stack">
/// The stack meant, for an FBX that holds more than one. Null takes the file's own.
/// </param>
/// <param name="Speed">
/// How fast this clip should carry the creature, in units a second, where the
/// animation does not say. Root motion lives in the cache rather than in the
/// animation, so a clip made for an engine that moves the actor itself -- which
/// carries no travel at all -- can still be given some here.
/// </param>
/// <param name="TurnDegrees">
/// How far this clip should turn the creature, signed, where the animation does not
/// say. Left is positive, on the same reading as the shipped clips.
/// </param>
public sealed record RoledAnimation(
    string Path,
    IReadOnlyList<AnimationRole> Roles,
    string? Stack = null,
    float? Speed = null,
    float? TurnDegrees = null)
{
    /// <summary>The name this animation is known by, which is its file's stem.</summary>
    public string Stem => Stack ?? Path.GetFileStem();

    public override string ToString() => $"{Stem}: {string.Join("; ", Roles)}";
}

internal static class PathText
{
    public static string GetFileStem(this string path) =>
        System.IO.Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
}
