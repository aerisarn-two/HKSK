namespace HKSK.Records;

/// <summary>
/// What the caches need from the game's plugins, and nothing about how plugins are read.
/// </summary>
/// <remarks>
/// <para>
/// Three of the inputs to the caches are plugin records rather than Havok files: the
/// movement types the speed table is swept for, the races that name each project's
/// behaviour and attacks, and the idle tree that decides which events the set data is
/// keyed on. This library reads Havok files and the animation cache and does not open
/// plugins, so a caller reads the records and hands them in -- <c>SKAssets</c> does it
/// with Mutagen, a test builds them by hand.
/// </para>
/// <para>
/// <strong>The records are the load order's answer, not a plugin's.</strong> One record
/// per id, already the winning override; which plugin said it is the caller's business.
/// </para>
/// <para>
/// Ids are opaque: anything that is equal for the same record and different for two.
/// They are only ever compared, to follow an idle's links and a race's movement types.
/// </para>
/// <para>
/// The records are stated raw -- a movement type's name and editor id both, an idle's
/// conditions as the plugin states them -- and what they mean is decided here
/// (<see cref="GameRecordRules"/>), because those are the engine's rules and not the
/// plugin format's.
/// </para>
/// </remarks>
public interface IGameRecords
{
    /// <summary>Every movement type (<c>MOVT</c>).</summary>
    IReadOnlyCollection<MovementTypeRecord> MovementTypes { get; }

    /// <summary>Every race (<c>RACE</c>).</summary>
    IReadOnlyCollection<RaceRecord> Races { get; }

    /// <summary>Every idle animation (<c>IDLE</c>).</summary>
    IReadOnlyCollection<IdleRecord> Idles { get; }

    /// <summary>Every action (<c>AACT</c>), which an idle tree can hang off.</summary>
    IReadOnlyCollection<ActionRecord> Actions { get; }
}

/// <summary>A plain list of each, for a caller that has read the records already.</summary>
public sealed class GameRecords : IGameRecords
{
    public List<MovementTypeRecord> MovementTypes { get; init; } = [];
    public List<RaceRecord> Races { get; init; } = [];
    public List<IdleRecord> Idles { get; init; } = [];
    public List<ActionRecord> Actions { get; init; } = [];

    IReadOnlyCollection<MovementTypeRecord> IGameRecords.MovementTypes => MovementTypes;
    IReadOnlyCollection<RaceRecord> IGameRecords.Races => Races;
    IReadOnlyCollection<IdleRecord> IGameRecords.Idles => Idles;
    IReadOnlyCollection<ActionRecord> IGameRecords.Actions => Actions;
}

/// <summary>A movement type: its names, and its eight speeds in game units per second.</summary>
/// <remarks>
/// Both names are carried because the graph uses one and the editor the other:
/// <c>iState_&lt;MOVT&gt;</c> carries the <see cref="Name"/>, and the falmer's
/// <c>Falmer_1HM_Walk</c> record is named <c>Falmer1HMWalk</c>.
/// </remarks>
public sealed record MovementTypeRecord
{
    public required string Id { get; init; }
    public string? EditorID { get; init; }

    /// <summary>The record's <c>MNAM</c>, which the graph's constants carry.</summary>
    public string? Name { get; init; }

    public float ForwardWalk { get; init; }
    public float ForwardRun { get; init; }
    public float BackWalk { get; init; }
    public float BackRun { get; init; }
    public float LeftWalk { get; init; }
    public float LeftRun { get; init; }
    public float RightWalk { get; init; }
    public float RightRun { get; init; }
}

/// <summary>The part a movement type plays for a race.</summary>
public enum MovementRole { Walk, Run, Swim, Fly, Sneak, Sprint }

/// <summary>A race: the behaviour its actors run, the attacks it sends, and how it moves.</summary>
public sealed record RaceRecord
{
    public required string Id { get; init; }
    public string? EditorID { get; init; }

    /// <summary>The male behaviour graph, as stored: <c>Actors\Character\DefaultMale.hkx</c>.</summary>
    public string? MaleBehavior { get; init; }

    /// <summary>The female behaviour graph, as stored.</summary>
    public string? FemaleBehavior { get; init; }

    /// <summary>The attack data's events, in the race's order.</summary>
    public IReadOnlyList<string> AttackEvents { get; init; } = [];

    /// <summary>The race's base movement defaults: a movement type's id for each role it fills.</summary>
    public IReadOnlyDictionary<MovementRole, string> DefaultMovements { get; init; } =
        new Dictionary<MovementRole, string>();
}

/// <summary>An action, which roots idle trees: <c>ActionMoveStart</c> is the graph's <c>moveStart</c>.</summary>
public sealed record ActionRecord
{
    public required string Id { get; init; }
    public string? EditorID { get; init; }
}

/// <summary>One idle animation, with its two links into the idle tree.</summary>
/// <remarks>
/// An <c>IDLE</c> record carries exactly two links, which the editor calls "related
/// idles": the first is a <strong>parent</strong> -- another idle or an action -- and the
/// second the <strong>previous sibling</strong>, never an action. Together they describe a
/// forest with ordered children, and the order is the engine's: it takes the first child
/// whose conditions pass.
/// </remarks>
public sealed record IdleRecord
{
    public required string Id { get; init; }
    public string? EditorID { get; init; }

    /// <summary>The behaviour event it raises, or null.</summary>
    public string? AnimationEvent { get; init; }

    /// <summary>
    /// The behaviour file its tree belongs to, as stored, when it names one:
    /// <c>Actors\WerewolfBeast\Behaviors\WerewolfBehavior.hkx</c>. Children inherit it.
    /// </summary>
    public string? BehaviorFile { get; init; }

    /// <summary>The idle or action it hangs off, or null.</summary>
    public string? Parent { get; init; }

    /// <summary>The idle checked immediately before it among its parent's children, or null.</summary>
    public string? PreviousSibling { get; init; }

    /// <summary>Its conditions, in the record's order.</summary>
    public IReadOnlyList<IdleCondition> Conditions { get; init; } = [];
}

/// <summary>How a condition compares its function's result.</summary>
public enum ConditionOperator { EqualTo, NotEqualTo, GreaterThan, GreaterThanOrEqualTo, LessThan, LessThanOrEqualTo }

/// <summary>One condition on an idle, as the plugin states it.</summary>
/// <param name="Function">
/// The condition function by the editor's name for it: <c>IsSprinting</c>,
/// <c>GetMovementSpeed</c>.
/// </param>
/// <param name="Operator">The comparison.</param>
/// <param name="Value">
/// The value compared against, or <see cref="float.NaN"/> when the condition compares
/// against a global rather than a number.
/// </param>
/// <param name="Or">Whether it is OR-ed with the next condition rather than AND-ed.</param>
public sealed record IdleCondition(string Function, ConditionOperator Operator, float Value, bool Or = false);
