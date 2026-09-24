using HKFBX.Model;
using HKSK.Assembly;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What a clip announces: footsteps where its feet actually landed, and attack events where
/// the game puts its own.
/// </summary>
public sealed class ClipAnnotationsTests
{
    private static readonly string[] Left = ["NPC L Toe0 [LToe]"];

    private static InferredMotion Walking(params (float Time, string Bone)[] falls) =>
        new(70f, 0f, falls.Length, 1f) { Falls = [.. falls.Select(f => new FootFall(f.Time, f.Bone))] };

    /// <summary>
    /// A footstep goes where the foot went down, and is told which foot it was by the name
    /// the bone carries. This needs no convention: the clip was asked.
    /// </summary>
    [Fact]
    public void FootstepsGoWhereTheFeetLanded()
    {
        var says = ClipAnnotations.Of(
            [new AnimationRole(RoleKind.Walk, Heading.Forward)],
            1f,
            Walking((0.2f, "NPC L Toe0 [LToe]"), (0.7f, "NPC R Toe0 [RToe]")),
            Left);

        var events = Assert.Single(says).Events;

        Assert.Contains(events, e => e.Text == "FootLeft" && Math.Abs(e.Time - 0.2f) < 0.001f);
        Assert.Contains(events, e => e.Text == "FootRight" && Math.Abs(e.Time - 0.7f) < 0.001f);

        // A gait announces the synchronising events beside them, which is what holds two
        // clips in step when the creature changes gait without breaking stride.
        Assert.Contains(events, e => e.Text == "syncLeft" && Math.Abs(e.Time - 0.2f) < 0.001f);
        Assert.Contains(events, e => e.Text == "syncRight");

        // In order, as the game's own are.
        Assert.Equal(events.OrderBy(e => e.Time).Select(e => e.Text), events.Select(e => e.Text));
    }

    /// <summary>A one-shot puts its feet down without announcing a gait to keep step with.</summary>
    [Fact]
    public void AOneShotAnnouncesItsFeetAndNoSynchronising()
    {
        var says = ClipAnnotations.Of(
            [new AnimationRole(RoleKind.Death)],
            2f,
            Walking((0.5f, "NPC R Toe0 [RToe]")),
            Left);

        var events = Assert.Single(says).Events;

        Assert.Contains(events, e => e.Text == "FootRight");
        Assert.DoesNotContain(events, e => e.Text.StartsWith("sync", StringComparison.Ordinal));
    }

    /// <summary>
    /// An attack announces its swing, a warning, and the blow landing, in that order and at
    /// the fractions the game's own 49 attacks put them at. Without the last of those the
    /// creature swings through its enemy without touching it.
    /// </summary>
    [Fact]
    public void AnAttackAnnouncesItsSwingAndItsHit()
    {
        var says = ClipAnnotations.Of([new AnimationRole(RoleKind.Attack, Name: "SwingHeavy")], 2f);
        var events = Assert.Single(says).Events;

        Assert.Equal(["weaponSwing", "preHitFrame", "HitFrame"], events.Select(e => e.Text));

        Assert.Equal(2f * ClipAnnotations.Swing, events[0].Time, 3);
        Assert.Equal(2f * ClipAnnotations.Hit, events[2].Time, 3);

        // Within the third to half of the clip that half of the game's own hits fall in.
        Assert.InRange(events[2].Time / 2f, 0.33f, 0.5f);
    }

    /// <summary>
    /// A clip with nothing to announce announces nothing, rather than inheriting whatever the
    /// file it was written into happened to say.
    /// </summary>
    [Fact]
    public void AClipWithNothingToSaySaysNothing()
    {
        Assert.Empty(ClipAnnotations.Of([new AnimationRole(RoleKind.Idle)], 3f));
        Assert.Empty(ClipAnnotations.Of([], 3f));
    }

    /// <summary>Both at once, for an attack that steps as it swings, all in order.</summary>
    [Fact]
    public void AnAttackThatStepsAnnouncesBoth()
    {
        var says = ClipAnnotations.Of(
            [new AnimationRole(RoleKind.Attack)],
            2f,
            Walking((0.2f, "NPC L Toe0 [LToe]"), (1.6f, "NPC R Toe0 [RToe]")),
            Left);

        var events = Assert.Single(says).Events;

        Assert.Equal(
            ["FootLeft", "weaponSwing", "preHitFrame", "HitFrame", "FootRight"],
            events.Select(e => e.Text));
    }

    /// <summary>
    /// A creature that goes nowhere takes no steps, whatever its feet do while it stands.
    /// How high a foot lifts cannot decide this: a bought skeleton's idle lifts a foot 4.2
    /// per cent of its height and its left strafe 4.3, and one of them is standing still.
    /// What separates them is that one goes somewhere.
    /// </summary>
    [Fact]
    public void AClipThatGoesNowhereAnnouncesNoFootsteps()
    {
        var shifting = new InferredMotion(0f, 0f, 30, 1f)
        {
            Falls = [new FootFall(0.2f, "NPC L Toe0 [LToe]"), new FootFall(0.9f, "NPC R Toe0 [RToe]")],
        };

        Assert.Empty(ClipAnnotations.Of([new AnimationRole(RoleKind.Idle)], 2f, shifting, Left));

        // The same feet on a creature that is going somewhere do announce.
        var going = new InferredMotion(12.1f, -90f, 30, 1f) { Falls = shifting.Falls };

        var events = Assert.Single(
            ClipAnnotations.Of([new AnimationRole(RoleKind.Walk, Heading.Left)], 2f, going, Left)).Events;

        Assert.Contains(events, e => e.Text == "FootLeft");
        Assert.Contains(events, e => e.Text == "FootRight");
    }
}
