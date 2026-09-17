using HKSK.Cache;
using HKSK.SetData;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Copying the one value in the set data that cannot be derived
/// (<c>docs/animation-set-data.md</c> §6).
/// </summary>
public sealed class MovingAttackFlagTests
{
    private static AnimationSetDataFile File(string project, params (string Event, int Flag, string[] Clips)[] attacks)
    {
        var set = new ProjectAttackBlock();
        foreach ((string name, int flag, string[] clips) in attacks)
            set.Attacks.Attacks.Add(new AttackData { EventName = name, MovingAttack = flag, Clips = [.. clips] });

        return new AnimationSetDataFile
        {
            Projects =
            {
                new AnimationSetDataProject
                {
                    Name = $"{project}Data\\{project}.txt",
                    Sets = new ProjectAttackListBlock { SetFiles = ["FullCharacter.txt"], Sets = [set] },
                },
            },
        };
    }

    [Fact]
    public void AnAttackTakesTheFlagOfTheShippedAttackNamingTheSameClips()
    {
        AnimationSetDataFile rebuilt = File("Netch", ("attackStartLeft", 0, ["NetchAttackLeft"]));
        AnimationSetDataFile shipped = File("Netch",
            ("attackStartLeft", 1, ["NetchAttackLeft"]),
            ("attackStartPowerStanding", 0, ["NetchAttackPowerStanding"]));

        MovingAttackReport report = MovingAttackFlags.CopyOnto(rebuilt, shipped);

        Assert.Equal(1, rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks[0].MovingAttack);
        Assert.Equal(new MovingAttackReport(Flagged: 1, Answered: 1, Unanswered: 0, Lost: 0), report);
    }

    [Fact]
    public void ClipOrderAndCaseDoNotCount()
    {
        AnimationSetDataFile rebuilt = File("Werewolf", ("attackStartDual", 0, ["ww howlexplode.hkx", "WW Second.hkx"]));
        AnimationSetDataFile shipped = File("Werewolf", ("AttackStartDual", 1, ["WW Second.hkx", "WW HowlExplode.hkx"]));

        MovingAttackFlags.CopyOnto(rebuilt, shipped);

        Assert.Equal(1, rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks[0].MovingAttack);
    }

    [Fact]
    public void AnAttackNamingOtherClipsFallsBackToItsProjectsAnswerForTheEvent()
    {
        AnimationSetDataFile rebuilt = File("Netch", ("attackStartLeft", 0, ["SomeOtherClip"]));
        AnimationSetDataFile shipped = File("Netch", ("attackStartLeft", 1, ["NetchAttackLeft"]));

        MovingAttackFlags.CopyOnto(rebuilt, shipped);

        Assert.Equal(1, rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks[0].MovingAttack);
    }

    [Fact]
    public void WhereTheClipsSayOneThingForOneWeaponAndAnotherElsewhereEachKeepsItsOwn()
    {
        // the player's bashStart: flagged where it plays the shield bash, clear where it
        // plays the one-handed one
        AnimationSetDataFile rebuilt = File("DefaultMale",
            ("bashStart", 0, ["Shd_Bash"]),
            ("bashStart", 0, ["1HM_BlockBash"]));
        AnimationSetDataFile shipped = File("DefaultMale",
            ("bashStart", 1, ["Shd_Bash"]),
            ("bashStart", 0, ["Shd_Bash"]),
            ("bashStart", 0, ["1HM_BlockBash"]));

        MovingAttackFlags.CopyOnto(rebuilt, shipped);

        List<AttackData> attacks = rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks;
        Assert.Equal(1, attacks[0].MovingAttack);
        Assert.Equal(0, attacks[1].MovingAttack);
    }

    [Fact]
    public void AnAttackNobodyShippedIsLeftAsItWas()
    {
        AnimationSetDataFile rebuilt = File("Netch", ("attackStartNew", 1, ["NewClip"]));
        AnimationSetDataFile shipped = File("Netch", ("attackStartLeft", 1, ["NetchAttackLeft"]));

        MovingAttackReport report = MovingAttackFlags.CopyOnto(rebuilt, shipped);

        Assert.Equal(1, rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks[0].MovingAttack);
        Assert.Equal(0, report.Answered);
        Assert.Equal(1, report.Unanswered);
        Assert.Equal(1, report.Lost);
    }

    [Fact]
    public void AProjectTheShippedFileDoesNotHaveIsLeftAlone()
    {
        AnimationSetDataFile rebuilt = File("NewCreature", ("attackStart", 0, ["Attack1"]));
        AnimationSetDataFile shipped = File("Netch", ("attackStartLeft", 0, ["NetchAttackLeft"]));

        MovingAttackReport report = MovingAttackFlags.CopyOnto(rebuilt, shipped);

        Assert.Equal(0, rebuilt.Projects[0].Sets.Sets[0].Attacks.Attacks[0].MovingAttack);
        Assert.Equal(new MovingAttackReport(Flagged: 0, Answered: 0, Unanswered: 1, Lost: 0), report);
    }
}
