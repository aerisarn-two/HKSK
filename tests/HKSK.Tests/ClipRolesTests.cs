using HKFBX.Model;
using HKSK.Assembly;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Reading what a clip is from its name, and checking that against what its feet did.
/// </summary>
public sealed class ClipRolesTests
{
    private static WrittenClip Clip(
        string name, float speed = 0f, float heading = 0f, float confidence = 1f, float duration = 1f) =>
        new(name, $@"Animations\{name}.hkx", 30, duration,
            speed <= 0f && confidence <= 0f ? null : new InferredMotion(speed, heading, 25, confidence));

    private static RoledClips Read(params WrittenClip[] clips) =>
        ClipRoles.Of(new WrittenClips(clips, 67, []));

    /// <summary>
    /// The name decides what a clip is for, because no measurement can supply that, and the
    /// feet decide how fast it goes, because no name can supply that.
    /// </summary>
    [Fact]
    public void TheNameGivesTheRoleAndTheFeetGiveTheSpeed()
    {
        RoledClips read = Read(
            Clip("Walk01", speed: 70.9f, heading: 2f),
            Clip("Run", speed: 160.7f, heading: 3f),
            Clip("WalkLeft", speed: 12.1f, heading: -87f),
            Clip("WalkRight", speed: 14.1f, heading: 93f));

        Assert.Empty(read.Disagreements);
        Assert.Empty(read.Unmeasured);

        var by = read.Clips.ToDictionary(c => c.Clip.Name);
        Assert.Equal(RoleKind.Walk, by["Walk01"].Animation.Roles[0].Kind);
        Assert.Equal(Heading.Forward, by["Walk01"].Animation.Roles[0].Heading);
        Assert.Equal(70.9f, by["Walk01"].Animation.Speed);

        Assert.Equal(RoleKind.Run, by["Run"].Animation.Roles[0].Kind);
        Assert.Equal(Heading.Left, by["WalkLeft"].Animation.Roles[0].Heading);
        Assert.Equal(Heading.Right, by["WalkRight"].Animation.Roles[0].Heading);

        // Every clip's duration comes across, since a speed without one is not a travel.
        Assert.Equal(read.Clips.Count, read.Durations.Count);
    }

    /// <summary>
    /// A clip whose feet carry it somewhere its name does not is reported, and its speed is
    /// not written: the two sources disagree and neither is believed over the other.
    /// </summary>
    [Fact]
    public void AClipWhoseFeetContradictItsNameIsReportedAndNotBelieved()
    {
        RoledClips read = Read(Clip("WalkLeft", speed: 60f, heading: 0f));

        string said = Assert.Single(read.Disagreements);
        Assert.Contains("WalkLeft", said, StringComparison.Ordinal);
        Assert.Contains("Left", said, StringComparison.Ordinal);

        RoledClip clip = Assert.Single(read.Clips);
        Assert.Null(clip.Animation.Speed);

        // But the role is still read, and the measurement is still there to be looked at.
        Assert.Equal(RoleKind.Walk, clip.Animation.Roles[0].Kind);
        Assert.Equal(60f, clip.Clip.Travel!.Speed);
    }

    /// <summary>
    /// A heading read off feet is not precise, so a strafe a few degrees off square is not a
    /// disagreement. Forty-five degrees out is another heading entirely and is.
    /// </summary>
    [Fact]
    public void AHeadingIsAllowedToBeApproximateButNotToBeAnotherHeading()
    {
        Assert.Empty(Read(Clip("WalkRight", speed: 14f, heading: 93f)).Disagreements);
        Assert.Empty(Read(Clip("WalkForward", speed: 70f, heading: -30f)).Disagreements);
        Assert.Single(Read(Clip("WalkForward", speed: 70f, heading: 75f)).Disagreements);
    }

    /// <summary>
    /// A clip with little on the ground is not read wrongly, it is left unread: what it
    /// measured is reported, and the row is left for somebody to fill in.
    /// </summary>
    [Fact]
    public void ALocomotionClipWithTooLittleOnTheGroundIsLeftForSomebodyToFillIn()
    {
        RoledClips read = Read(Clip("WalkBack", speed: 26f, heading: 155f, confidence: 0.37f));

        Assert.Empty(read.Disagreements);
        Assert.Null(Assert.Single(read.Clips).Animation.Speed);

        string said = Assert.Single(read.Unmeasured);
        Assert.Contains("WalkBack", said, StringComparison.Ordinal);
        Assert.Contains("37", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// An idle that travels, and a name that says nothing at all: both are worth saying out
    /// loud, and neither stops the rest being read.
    /// </summary>
    [Fact]
    public void AStillClipThatTravelsAndANamelessOneAreBothReported()
    {
        RoledClips read = Read(
            Clip("Idle", speed: 0.1f, heading: 40f),
            Clip("Idle2", speed: 120f, heading: 0f),
            Clip("Take 001"),
            Clip("Walk01", speed: 70f, heading: 0f));

        Assert.Equal(2, read.Disagreements.Count);
        Assert.Contains(read.Disagreements, d => d.StartsWith("Idle2", StringComparison.Ordinal));
        Assert.Contains(read.Disagreements, d => d.StartsWith("Take 001", StringComparison.Ordinal));

        // An idle that barely drifts is an idle, not a fault.
        Assert.DoesNotContain(read.Disagreements, d => d.StartsWith("Idle:", StringComparison.Ordinal));

        // And the clip that reads cleanly is unaffected by the company it keeps.
        Assert.Equal(70f, read.Clips.Single(c => c.Clip.Name == "Walk01").Animation.Speed);
    }

    /// <summary>The measured speeds can be read without being acted on.</summary>
    [Fact]
    public void TheSpeedsCanBeReportedWithoutBeingWritten()
    {
        RoledClips read = ClipRoles.Of(
            new WrittenClips([Clip("Walk01", speed: 70f, heading: 0f)], 67, []), speeds: false);

        RoledClip clip = Assert.Single(read.Clips);
        Assert.Null(clip.Animation.Speed);
        Assert.Equal(70f, clip.Clip.Travel!.Speed);
        Assert.Empty(read.Unmeasured);
    }
}
