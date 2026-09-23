using HKSK.Assembly;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Guessing what an animation is from the name it was given, which is what saves a
/// person typing 200 roles for a folder of 200 clips.
/// </summary>
public sealed class RoleReaderTests
{
    private static AnimationRole One(string name) => Assert.Single(RoleReader.Of(name).Roles);

    /// <summary>The 24 clips a skeleton bought from a marketplace arrives with.</summary>
    [Theory]
    [InlineData("Idle", RoleKind.Idle)]
    [InlineData("IdleHoldSword", RoleKind.CombatIdle)]
    [InlineData("Walk01", RoleKind.Walk)]
    [InlineData("WalkBack", RoleKind.Walk)]
    [InlineData("Run", RoleKind.Run)]
    [InlineData("SwingHeavy", RoleKind.Attack)]
    [InlineData("Death", RoleKind.Death)]
    [InlineData("StandUp01", RoleKind.GetUp)]
    [InlineData("Resurrection", RoleKind.Reanimate)]
    [InlineData("CastSpell", RoleKind.UpperBodyCast)]
    [InlineData("Hit2", RoleKind.Recoil)]
    public void AMarketplaceSkeletonsNamesAreRead(string name, RoleKind expected) =>
        Assert.Equal(expected, One(name).Kind);

    /// <summary>
    /// The game's own names for the same thing, which is why roles are asked for rather
    /// than read: five spellings of one walk.
    /// </summary>
    [Theory]
    [InlineData("MTForward")]
    [InlineData("WalkForward")]
    [InlineData("Forward_Walk")]
    [InlineData("forwardWalk")]
    public void TheGamesOwnSpellingsOfAWalkAreRead(string name)
    {
        AnimationRole role = One(name);
        Assert.Equal(RoleKind.Walk, role.Kind);
        Assert.Equal(Heading.Forward, role.Heading);
    }

    /// <summary>A heading in the name is read; a locomotion clip without one goes forward.</summary>
    [Theory]
    [InlineData("WalkBack", Heading.Back)]
    [InlineData("WalkLeft", Heading.Left)]
    [InlineData("WalkRight", Heading.Right)]
    [InlineData("MTBackwardLeft", Heading.BackLeft)]
    [InlineData("Walk01", Heading.Forward)]
    public void TheHeadingIsReadFromTheName(string name, Heading expected) =>
        Assert.Equal(expected, One(name).Heading);

    [Theory]
    [InlineData("TurnLeft90", 90)]
    [InlineData("CannedTurnRight180", 180)]
    public void ACannedTurnsAngleIsRead(string name, int expected)
    {
        AnimationRole role = One(name);
        Assert.Equal(RoleKind.CannedTurn, role.Kind);
        Assert.Equal(expected, role.Angle);
    }

    /// <summary>
    /// A name it does not know gets no role rather than a wrong one. An animation in the
    /// wrong role is worse than one in none: a walk filed as an attack is a creature
    /// that lunges when it is asked to move, and a missing walk is a refusal with a
    /// reason.
    /// </summary>
    [Theory]
    [InlineData("Plane.005")]
    [InlineData("Take 001")]
    [InlineData("")]
    public void ANameItDoesNotKnowGetsNoRole(string name) => Assert.Empty(RoleReader.Of(name).Roles);

    /// <summary>And it says which part of the name it read, for a person to check.</summary>
    [Fact]
    public void ItSaysWhatItRead()
    {
        Assert.Equal("death", RoleReader.Of("Death").Why);
        Assert.Contains("stand", RoleReader.Of("StandUp01").Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAListKeepsItsOrderBecauseThatIsTheNumbering()
    {
        var read = RoleReader.All(["Idle", "Walk01", "Run"]);

        Assert.Equal(["Idle", "Walk01", "Run"], read.Select(r => r.Name));
        Assert.Equal([RoleKind.Idle, RoleKind.Walk, RoleKind.Run], read.Select(r => r.Read.Roles[0].Kind));
    }
}
