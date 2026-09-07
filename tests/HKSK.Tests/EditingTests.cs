using System.Numerics;
using HKSK.Cache;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Editing has to keep the scattered files agreeing with each other.
/// </summary>
/// <remarks>
/// These run on a project built in memory rather than on the corpus, so the
/// numbering is fully known and a renumbering bug shows up as a specific wrong
/// index rather than as a diff against 170,000 lines.
/// </remarks>
public class EditingTests
{
    [Fact]
    public void AddingAnAnimationAppendsSoExistingIndicesStand()
    {
        HavokProject project = Fake.Project();

        int before = project.Animations.Count;
        Clip run = project.Clip("Run")!;
        int runIndex = run.CacheIndex;

        AnimationSlot added = project.AddAnimation("Animations\\AAA_Sorts_First.hkx");

        Assert.Equal(before, added.Index);
        Assert.Equal(before + 1, project.Animations.Count);

        // The new name sorts first alphabetically, but it went last -- because
        // inserting it in sorted position would have renumbered everything.
        Assert.Equal(runIndex, project.Clip("Run")!.CacheIndex);
    }

    [Fact]
    public void AddingTheSameAnimationTwiceReturnsTheSameSlot()
    {
        HavokProject project = Fake.Project();

        AnimationSlot first = project.AddAnimation("Animations\\New.hkx");
        AnimationSlot again = project.AddAnimation("Animations\\NEW.HKX");

        Assert.Equal(first.Index, again.Index);
    }

    /// <summary>
    /// The edit that hand-editing gets wrong: everything above the hole moves
    /// down by one, clips and root motion together.
    /// </summary>
    [Fact]
    public void RemovingAnAnimationRenumbersEverythingAboveIt()
    {
        HavokProject project = Fake.Project();

        // Walk is slot 1; Run is slot 2 and has root motion of its own.
        AnimationSlot walk = project.Animation("Walk")!;
        Assert.Equal(1, walk.Index);
        Assert.Equal(2, project.Animation("Run")!.Index);

        IReadOnlyList<string> orphaned = project.RemoveAnimation(walk);

        // The clip that played it went with it.
        Assert.Equal(["Walk"], orphaned);
        Assert.Null(project.Clip("Walk"));

        // And everything above moved down, in the character file, in the clip
        // entries, and in the root motion blocks alike.
        Assert.Equal(1, project.Animation("Run")!.Index);
        Assert.Equal(1, project.Clip("Run")!.CacheIndex);
        Assert.Equal(1, project.Animation("Run")!.Motion!.CacheIndex);

        // The motion that moved is still the one that belongs to Run: it travels
        // 30 units, where Idle's does not move at all.
        Assert.Equal(30f, project.Animation("Run")!.Motion!.Travel, 3);
    }

    [Fact]
    public void RemovingAnAnimationDropsItsRootMotion()
    {
        HavokProject project = Fake.Project();
        int motions = project.Data.Movements!.Movements.Count;

        project.RemoveAnimation(project.Animation("Walk")!);

        Assert.Equal(motions - 1, project.Data.Movements.Movements.Count);
        Assert.DoesNotContain(project.Data.Movements.Movements, m => m.CacheIndex == 1 &&
            m.Translations.Count == 2 && m.Translations[1].Value.X == 10f);
    }

    [Fact]
    public void AddingAClipOverAnExistingSlotSharesItsMotion()
    {
        HavokProject project = Fake.Project();
        AnimationSlot run = project.Animation("Run")!;

        Clip added = project.AddClip("RunFast", run, playbackSpeed: 1.6f);

        Assert.Equal(run.Index, added.CacheIndex);
        Assert.Same(project.Animation("Run")!.Motion, added.Slot!.Motion);
        Assert.Equal(1.6f, added.Entry.PlaybackSpeed);
    }

    [Fact]
    public void AddingAClipTwiceIsRejected()
    {
        HavokProject project = Fake.Project();
        AnimationSlot run = project.Animation("Run")!;

        Assert.Throws<InvalidOperationException>(() => project.AddClip("Run", run));
    }

    [Fact]
    public void SettingRootMotionKeepsTheBlocksInIndexOrder()
    {
        HavokProject project = Fake.Project();

        // Idle is slot 0 and starts without motion; give it some.
        AnimationSlot idle = project.Animation("Idle")!;
        Assert.Null(idle.Motion);

        project.SetRootMotion(idle, new ClipMovement
        {
            Duration = 1f,
            Translations = { new TranslationKey(0f, Vector3.Zero), new TranslationKey(1f, new Vector3(1, 0, 0)) },
            Rotations = { new RotationKey(0f, Quaternion.Identity) },
        });

        Assert.NotNull(project.Animation("Idle")!.Motion);
        Assert.Equal(0, project.Animation("Idle")!.Motion!.CacheIndex);

        var order = project.Data.Movements!.Movements.Select(m => m.CacheIndex).ToList();
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void SettingRootMotionReplacesWhatWasThere()
    {
        HavokProject project = Fake.Project();
        AnimationSlot run = project.Animation("Run")!;
        int before = project.Data.Movements!.Movements.Count;

        project.SetRootMotion(run, new ClipMovement { Duration = 9f });

        Assert.Equal(before, project.Data.Movements.Movements.Count);
        Assert.Equal(9f, project.Animation("Run")!.Motion!.Duration);
    }

    [Fact]
    public void RemovingAClipLeavesItsAnimationAlone()
    {
        HavokProject project = Fake.Project();

        Assert.True(project.RemoveClip("Run"));
        Assert.Null(project.Clip("Run"));

        // The slot and its root motion stay: another clip could still want them.
        Assert.NotNull(project.Animation("Run"));
        Assert.NotNull(project.Animation("Run")!.Motion);
    }

    [Fact]
    public void EditingWithoutTheCharacterFileIsRefusedRatherThanGuessed()
    {
        // Opened from the cache alone, as when the .hkx are not to hand.
        HavokProject project = HavokProject.Open(Fake.Data());

        Assert.False(project.HasHavok);
        Assert.Throws<InvalidOperationException>(() => project.AddAnimation("Animations\\New.hkx"));
    }

    /// <summary>An edited project still writes a well-formed cache.</summary>
    [Fact]
    public void AnEditedProjectStillRoundTrips()
    {
        HavokProject project = Fake.Project();
        project.RemoveAnimation(project.Animation("Walk")!);

        var file = new AnimationDataFile();
        file.Projects.Add(project.Data);

        string written = file.Write();
        AnimationDataFile reread = AnimationDataFile.Parse(written);

        Assert.Equal(written, reread.Write());
        Assert.Equal(1, reread.Projects[0].Block.Clip("Run")!.CacheIndex);
    }
}
