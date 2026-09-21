using HKSK.Records;
using HKSK.SetData;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// The engine's rules over the game's records, on records built by hand.
/// </summary>
/// <remarks>
/// The same rules are checked against the masters by <see cref="SetDataRebuildTests"/>
/// and <see cref="BlockSelectionTests"/>; these say what each rule is on the smallest
/// tree that shows it, and need no game.
/// </remarks>
public class GameRecordRulesTests
{
    [Fact]
    public void AMovementTypeIsNamedAsTheGraphNamesIt()
    {
        var records = new GameRecords
        {
            MovementTypes =
            [
                // the falmer's: the graph's constant carries the name, not the editor id
                new() { Id = "1", EditorID = "Falmer_1HM_Walk", Name = "Falmer1HMWalk", ForwardWalk = 100.44f, ForwardRun = 175.77f },
                new() { Id = "2", EditorID = "NoNameDefault" },
                new() { Id = "3" },
            ],
        };

        var types = GameRecordRules.MovementTypes(records);

        Assert.Equal(["Falmer1HMWalk", "NoNameDefault"], types.Keys.Order());
        Assert.Equal(175.77f, types["falmer1hmwalk"].ForwardRun);
    }

    [Fact]
    public void ARaceGivesItsMovementTypesTheirRoles()
    {
        var records = new GameRecords
        {
            MovementTypes = [new() { Id = "w", Name = "BearDefault" }, new() { Id = "s", Name = "BearSwimDefault" }],
            Races =
            [
                new() { Id = "r1", DefaultMovements = new Dictionary<MovementRole, string> { [MovementRole.Walk] = "w", [MovementRole.Run] = "w", [MovementRole.Swim] = "s" } },
                new() { Id = "r2", DefaultMovements = new Dictionary<MovementRole, string> { [MovementRole.Sneak] = "w", [MovementRole.Fly] = "missing" } },
            ],
        };

        var roles = GameRecordRules.MovementRoles(records);

        Assert.Equal([MovementRole.Walk, MovementRole.Run, MovementRole.Sneak], roles["BearDefault"]);
        Assert.Equal([MovementRole.Swim], roles["BearSwimDefault"]);
    }

    /// <summary>
    /// Every idle's event can be sent; those under a drawing or equipping root are what
    /// the equip path lands on; a race's attacks belong to the graph it wears.
    /// </summary>
    [Fact]
    public void TheEventsComeFromTheIdleTreeAndTheRaces()
    {
        var records = new GameRecords
        {
            Actions = [new() { Id = "a", EditorID = "ActionDraw" }],
            Idles =
            [
                new() { Id = "root", EditorID = "DrawSheathRoot", Parent = "a" },
                new() { Id = "draw", EditorID = "1HMDraw", AnimationEvent = "WeapEquip", Parent = "root" },
                new() { Id = "idle", EditorID = "IdleChair", AnimationEvent = "IdleChairSitting" },
            ],
            Races =
            [
                new() { Id = "r", MaleBehavior = @"Actors\Bear\BearProject.hkx", AttackEvents = ["attackStart", "", "attackPowerStart"] },
            ],
        };

        GameEvents events = GameRecordRules.Events(records);

        Assert.Equal(["IdleChairSitting", "WeapEquip"], events.Idle.Order());
        Assert.Equal(["WeapEquip"], events.Equip);
        Assert.Equal(["attackPowerStart", "attackStart"], events.Attacks["BearProject"].Order());
        Assert.Empty(events.MovingAttacks!);
    }

    /// <summary>
    /// The werewolf's running power attack has no condition of its own: it is tried after
    /// the standing one, which asks for a movement speed of at most one, and is the moving
    /// attack by elimination. A sprint condition on an ancestor makes the whole branch one.
    /// </summary>
    [Fact]
    public void AnAttackIsChosenOnTheMoveByItsTreeNotItsOwnConditions()
    {
        var records = new GameRecords
        {
            Idles =
            [
                new() { Id = "root", EditorID = "WerewolfAttackRoot", BehaviorFile = @"Actors\WerewolfBeast\Behaviors\WerewolfBehavior.hkx" },
                new()
                {
                    Id = "stand", EditorID = "WerewolfLeftPowerAttack", AnimationEvent = "attackPowerStartLeft", Parent = "root",
                    Conditions = [new IdleCondition("GetMovementSpeed", ConditionOperator.LessThanOrEqualTo, 1f)],
                },
                new() { Id = "run", EditorID = "WerewolfLeftRunningPowerAttack", AnimationEvent = "attackPowerStartLeftRunning", Parent = "root", PreviousSibling = "stand" },
                new()
                {
                    Id = "sprint", EditorID = "WerewolfSprintBranch", Parent = "root", PreviousSibling = "run",
                    Conditions = [new IdleCondition("IsSprinting", ConditionOperator.EqualTo, 1f)],
                },
                new() { Id = "leap", EditorID = "WerewolfSprintLeap", AnimationEvent = "attackSprintStart", Parent = "sprint" },
                new()
                {
                    // a direction is not a movement: the player's forward lunge travels on its own
                    Id = "lunge", EditorID = "WerewolfLunge", AnimationEvent = "attackLunge", Parent = "root",
                    Conditions = [new IdleCondition("GetMovementDirection", ConditionOperator.EqualTo, 1f)],
                },
            ],
            Races = [new() { Id = "r", MaleBehavior = @"Actors\WerewolfBeast\WerewolfBeastProject.hkx" }],
        };

        GameEvents events = GameRecordRules.Events(records);

        Assert.Equal(["attackPowerStartLeftRunning", "attackSprintStart"], events.MovingAttacks!["WerewolfBeastProject"].Order());
    }

    /// <summary>A tree whose links loop is followed once round, not forever.</summary>
    [Fact]
    public void ALoopInTheTreeEndsTheWalk()
    {
        var records = new GameRecords
        {
            Idles =
            [
                new() { Id = "a", EditorID = "A", AnimationEvent = "a", Parent = "b", PreviousSibling = "a" },
                new() { Id = "b", EditorID = "B", AnimationEvent = "b", Parent = "a" },
            ],
        };

        Assert.Equal(2, GameRecordRules.Events(records).Idle.Count);
    }

    /// <summary>
    /// The projects the races wear are the cache's actors, which is what the Havok files
    /// cannot say: 247 props have animations too. All but one: <c>FirstPerson</c>, the
    /// player's view, which the engine loads without a record naming it.
    /// </summary>
    [MastersFact]
    public void TheProjectsTheRacesWearAreTheActors()
    {
        IReadOnlySet<string> worn = GameRecordRules.ActorProjects(Masters.Records);
        string[] actors = [.. HKSK.Model.SkyrimCache.Load(Corpus.Root!).Actors().Select(a => a.Name)];

        Assert.Equal(48, worn.Count);
        Assert.Equal(["FirstPerson"], actors.Except(worn, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(worn.Except(actors, StringComparer.OrdinalIgnoreCase));
    }
}
