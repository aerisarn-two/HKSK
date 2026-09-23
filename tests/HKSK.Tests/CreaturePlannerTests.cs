using HKSK.Assembly;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What a set of animations says it can be made into, before anything is written.
/// </summary>
public sealed class CreaturePlannerTests
{
    private static RoledAnimation A(string name, params AnimationRole[] roles) => new($"{name}.hkx", roles);

    private static AnimationRole Walk(Heading heading) => new(RoleKind.Walk, heading);

    /// <summary>The floor is the witchlight's: an idle and something that walks forward.</summary>
    [Fact]
    public void AnIdleAndAForwardWalkAreTheLeastThereCanBe()
    {
        CreaturePlan plan = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("forward", Walk(Heading.Forward)),
        ]);

        Assert.True(plan.CanBuild);
        Assert.Equal(LocomotionPlan.ForwardOnly, plan.Locomotion);
        Assert.Contains(Module.Locomotion, plan.Modules);
    }

    [Fact]
    public void WithoutAnIdleOrAWalkItRefusesAndSaysWhich()
    {
        CreaturePlan noIdle = CreaturePlanner.Of([A("forward", Walk(Heading.Forward))]);
        Assert.False(noIdle.CanBuild);
        Assert.Contains(noIdle.Refusals, r => r.Contains("the idle", StringComparison.Ordinal));

        CreaturePlan noWalk = CreaturePlanner.Of([A("idle", new AnimationRole(RoleKind.Idle))]);
        Assert.False(noWalk.CanBuild);
        Assert.Contains(noWalk.Refusals, r => r.Contains("forward walk", StringComparison.Ordinal));

        // Walking sideways and never forward is not a creature that can be driven.
        CreaturePlan sideways = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("left", Walk(Heading.Left)),
            A("right", Walk(Heading.Right)),
        ]);
        Assert.False(sideways.CanBuild);
        Assert.Contains(sideways.Refusals, r => r.Contains("forward", StringComparison.Ordinal));
    }

    /// <summary>The plan is read off the headings, not asked for.</summary>
    [Theory]
    [InlineData(1, LocomotionPlan.ForwardOnly)]
    [InlineData(4, LocomotionPlan.FourArm)]
    [InlineData(8, LocomotionPlan.CompassBiped)]
    public void TheHeadingsChooseThePlan(int headings, LocomotionPlan expected)
    {
        Heading[] compass =
        [
            Heading.Forward, Heading.Right, Heading.Back, Heading.Left,
            Heading.ForwardRight, Heading.BackRight, Heading.BackLeft, Heading.ForwardLeft,
        ];

        CreaturePlan plan = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            .. compass.Take(headings).Select(h => A(h.ToString(), Walk(h))),
        ]);

        Assert.Equal(expected, plan.Locomotion);
    }

    [Fact]
    public void ATrotMakesItAQuadruped()
    {
        CreaturePlan plan = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("walk", Walk(Heading.Forward)),
            A("trot", new AnimationRole(RoleKind.Trot, Heading.Forward)),
        ]);

        Assert.Equal(LocomotionPlan.Quadruped, plan.Locomotion);
        Assert.Contains(plan.Notes, n => n.Contains("2 gaits", StringComparison.Ordinal));
    }

    [Fact]
    public void SwimmingWithNoWalkIsASwimmer()
    {
        CreaturePlan plan = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("swim", new AnimationRole(RoleKind.Swim, Heading.Forward)),
        ]);

        Assert.True(plan.CanBuild);
        Assert.Equal(LocomotionPlan.Swimmer, plan.Locomotion);
    }

    /// <summary>
    /// A module is in the plan exactly when its animations are, and out of it with a
    /// note saying so -- which is the answer to "why has my creature no combat stance".
    /// </summary>
    [Fact]
    public void AModuleIsPresentExactlyWhenItsAnimationsAre()
    {
        CreaturePlan bare = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("forward", Walk(Heading.Forward)),
        ]);

        Assert.DoesNotContain(Module.Attacking, bare.Modules);
        Assert.DoesNotContain(Module.Death, bare.Modules);
        Assert.Contains(bare.Notes, n => n.Contains("no attacks", StringComparison.Ordinal));
        Assert.Contains(bare.Notes, n => n.Contains("straight to ragdoll", StringComparison.Ordinal));

        CreaturePlan armed = CreaturePlanner.Of([
            A("idle", new AnimationRole(RoleKind.Idle)),
            A("forward", Walk(Heading.Forward)),
            A("swing", new AnimationRole(RoleKind.Attack, Name: "attackStart_Attack1")),
            A("death", new AnimationRole(RoleKind.Death)),
            A("standup", new AnimationRole(RoleKind.GetUp)),
        ]);

        Assert.Contains(Module.Attacking, armed.Modules);
        Assert.Contains(Module.Death, armed.Modules);
        Assert.Contains(Module.GetUp, armed.Modules);
        Assert.Equal(["attackStart_Attack1"], armed.Attacks);
    }

    /// <summary>
    /// The 24 clips a skeleton bought from CGTrader arrives with, read as roles: a
    /// four-arm biped that fights, falls over and gets back up.
    /// </summary>
    [Fact]
    public void AMarketplaceSkeletonReadsAsAFourArmBiped()
    {
        CreaturePlan plan = CreaturePlanner.Of([
            A("Idle", new AnimationRole(RoleKind.Idle)),
            A("Idle2", new AnimationRole(RoleKind.IdleVariant)),
            A("IdleHoldSword", new AnimationRole(RoleKind.CombatIdle, Stance: Stance.Combat)),
            A("Walk01", Walk(Heading.Forward)),
            A("WalkBack", Walk(Heading.Back)),
            A("WalkLeft", Walk(Heading.Left)),
            A("WalkRight", Walk(Heading.Right)),
            A("Run", new AnimationRole(RoleKind.Run, Heading.Forward)),
            A("SwingNormal", new AnimationRole(RoleKind.Attack, Name: "attackStart_Attack1")),
            A("SwingQuick", new AnimationRole(RoleKind.Attack, Name: "attackStart_Attack2")),
            A("SwingHeavy", new AnimationRole(RoleKind.PowerAttack, Name: "attackStart_ForwardPower")),
            A("Hit", new AnimationRole(RoleKind.Recoil)),
            A("Hit2", new AnimationRole(RoleKind.Stagger, Magnitude: Magnitude.Large)),
            A("Death", new AnimationRole(RoleKind.Death)),
            A("StandUp01", new AnimationRole(RoleKind.GetUp)),
            A("StandUp02", new AnimationRole(RoleKind.GetUp)),
            A("StandUp03", new AnimationRole(RoleKind.GetUp)),
            A("Resurrection", new AnimationRole(RoleKind.Reanimate)),
            A("CastSpell", new AnimationRole(RoleKind.UpperBodyCast)),
        ]);

        Assert.True(plan.CanBuild);
        Assert.Equal(LocomotionPlan.FourArm, plan.Locomotion);
        Assert.Equal(3, plan.Attacks.Count);
        Assert.Contains(Module.Death, plan.Modules);
        Assert.Contains(Module.GetUp, plan.Modules);
        Assert.Contains(Module.Reanimate, plan.Modules);
        Assert.Contains(Module.CombatStance, plan.Modules);
        Assert.Contains(Module.Casting, plan.Modules);
        Assert.Contains(plan.Notes, n => n.Contains("2 gaits", StringComparison.Ordinal));
        Assert.Contains(plan.Notes, n => n.Contains("getting up, from 3", StringComparison.Ordinal));

        // And what it has not got, said rather than silently absent.
        Assert.DoesNotContain(Module.CannedTurn, plan.Modules);
        Assert.Contains(plan.Notes, n => n.Contains("no canned turns", StringComparison.Ordinal));

        // The turn it has not got is made rather than left out, and the plan says so.
        Assert.Contains(Module.TurnInPlace, plan.Modules);
        Assert.Contains(plan.Synthesised, s => s.Contains("turn in place", StringComparison.Ordinal));
    }

    /// <summary>
    /// A turn in place is the idle pose with the root turning, which is what the game's
    /// own are: the sabre cat's TurnLoopingL goes nowhere and turns 87 degrees.
    /// </summary>
    [Fact]
    public void ASynthesisedTurnGoesNowhereAndTurns()
    {
        HKSK.Cache.ClipMovement left = SyntheticMotion.TurnInPlace(0.8f, 90f, cacheIndex: 7);

        Assert.Equal(7, left.CacheIndex);
        Assert.Equal(0.8f, left.Duration, 4);
        Assert.Empty(left.Translations);
        Assert.Equal(0f, left.Travel);
        Assert.True(left.HasMovement);
        Assert.Equal(90f, left.Turn * 180f / MathF.PI, 2);

        // Signed: the other way turns the other way, and by as much.
        HKSK.Cache.ClipMovement right = SyntheticMotion.TurnInPlace(0.8f, -90f);
        Assert.Equal(left.Turn, right.Turn, 4);
        Assert.Equal(-left.Rotations[^1].Value.Z, right.Rotations[^1].Value.Z, 4);
    }

    /// <summary>
    /// The rate is one a creature of that size is asked for. Nothing in the game turns
    /// faster than the canines' 112.5, and the mammoth turns at 45.
    /// </summary>
    [Theory]
    [InlineData(70f, 90f)]
    [InlineData(140f, 90f)]
    [InlineData(200f, 60f)]
    [InlineData(400f, 45f)]
    public void TheTurnRateFollowsTheCreaturesSize(float height, float expected) =>
        Assert.Equal(expected, SyntheticMotion.ReasonableTurnRate(height));
}
