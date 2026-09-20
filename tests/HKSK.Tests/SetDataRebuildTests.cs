using HKSK.Cache;
using HKSK.Model;
using HKSK.SetData;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// <c>animationsetdatasinglefile.txt</c> built from the other assets, checked against the
/// shipped one where the two are meant to agree and counted where they are not.
/// </summary>
/// <remarks>
/// The generator does not try to reproduce the shipped file, which is a record of how it
/// was edited (<c>docs/animation-set-data.md</c> §3.3). The counts here are what it gives,
/// so a change to the rules shows up as a change to a number.
/// </remarks>
public sealed class SetDataRebuildTests : IClassFixture<SetDataRebuildTests.Built>
{
    public sealed class Built
    {
        public AnimationSetDataFile Made { get; }
        public AnimationSetDataFile Shipped { get; }

        public Built()
        {
            if (!Corpus.Available || !Masters.Available)
            {
                Made = Shipped = new AnimationSetDataFile();
                return;
            }

            Shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, SkyrimCache.AnimationSetDataFileName));

            SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
            cache.SetData.Projects.Clear();
            Made = SetDataGenerator.Generate(cache, HKSK.SetGen.MasterData.Events(Masters.DataFolder!));
        }
    }

    private readonly Built _built;

    public SetDataRebuildTests(Built built) => _built = built;

    private static HashSet<(string, string)> Files(ProjectAttackBlock set) =>
        [.. set.Checksums.Triples().Select(t => (t.Folder, t.Name))];

    [MastersFact]
    public void EveryShippedProjectGetsABlock()
    {
        Assert.Equal(49, _built.Made.Projects.Count);
        Assert.Equal(_built.Shipped.Projects.Select(p => p.Name).Order(), _built.Made.Projects.Select(p => p.Name).Order());

        Assert.Equal(2241, _built.Made.Projects.Sum(p => p.Sets.Sets.Count));
        Assert.Equal(57495, _built.Made.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Checksums.Entries.Count / 3)));
        Assert.Equal(3819, _built.Made.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Attacks.Attacks.Count)));
    }

    [MastersFact]
    public void ItReadsBackAsItWasWritten()
    {
        string text = _built.Made.Write();
        Assert.Equal(text, AnimationSetDataFile.Parse(text).Write());
    }

    [MastersFact]
    public void AProjectThatNeverChoosesByWeaponIsOneSetOfItsCharacterList()
    {
        var single = _built.Made.Projects.Where(p => p.Sets.Sets.Count == 1).ToList();
        Assert.Equal(38, single.Count);

        int identical = single.Count(p =>
            _built.Shipped.Project(p.Name) is { Sets.Sets.Count: 1 } shipped &&
            shipped.Sets.Sets[0].Checksums.Entries.SequenceEqual(p.Sets.Sets[0].Checksums.Entries));

        // the other twelve are the shipped file's history: first-person killmoves appended
        // later, a few idles dropped, the spider's killmoves listed twice (§3.2)
        Assert.Equal(26, identical);
    }

    [MastersFact]
    public void EveryFileTheShippedDataPreloadsIsInASetButKillmovesTheCharacterDoesNotList()
    {
        HashSet<string> Folders(string part) => Directory.EnumerateDirectories(Corpus.Root!, "*", SearchOption.AllDirectories)
            .Select(d => "meshes\\" + Path.GetRelativePath(Corpus.Root!, d).Replace('/', '\\'))
            .Where(d => d.Contains(part, StringComparison.OrdinalIgnoreCase))
            .Select(HavokCrc.Text)
            .ToHashSet();

        HashSet<string> firstPerson = Folders("\\1stperson");
        HashSet<string> werewolf = Folders("\\human&werewolf");

        int listed = 0, found = 0;
        var elsewhere = new List<(string Folder, string Name)>();

        foreach (AnimationSetDataProject shipped in _built.Shipped.Projects)
        {
            var made = _built.Made.Project(shipped.Name)!.Sets.Sets.SelectMany(Files).ToHashSet();
            foreach (var file in shipped.Sets.Sets.SelectMany(Files).ToHashSet())
            {
                listed++;
                if (made.Contains(file)) found++;
                else elsewhere.Add(file);
            }
        }

        Assert.Equal(6829, listed);
        Assert.Equal(6758, found);

        // the first-person counterparts a later update appended (§3.2, §3.3), and the
        // werewolf's human-side killmoves, which only the player's character file names
        Assert.Equal(66, elsewhere.Count(f => firstPerson.Contains(f.Folder)));
        Assert.Equal(5, elsewhere.Count(f => !firstPerson.Contains(f.Folder) && werewolf.Contains(f.Folder)));
    }

    // The set the lookup picks for a key, as §4.2 has it: the first whose swap events hold
    // the key and whose hand variables admit the graph's values, taken here as no weapon.
    private static ProjectAttackBlock? Lookup(AnimationSetDataProject project, string key) =>
        project.Sets.Sets.FirstOrDefault(s =>
            s.SwapEvents.Contains(key, StringComparer.OrdinalIgnoreCase) &&
            s.HandVariables.Variables.All(v => v.Min <= 0 && 0 <= v.Max));

    [MastersFact]
    public void TheKeysOfAShippedIdleSetLoadMostOfItsFiles()
    {
        int wanted = 0, loaded = 0;

        foreach (AnimationSetDataProject shipped in _built.Shipped.Projects)
        {
            AnimationSetDataProject made = _built.Made.Project(shipped.Name)!;
            var always = made.Sets.Sets.Count == 1
                ? Files(made.Sets.Sets[0])
                : made.Sets.Sets.Where(s => s.SwapEvents.Count == 0).SelectMany(Files).ToHashSet();

            foreach (ProjectAttackBlock set in shipped.Sets.Sets)
            {
                if (set.SwapEvents.Count == 0 || set.HandVariables.Variables.Count > 0) continue;

                var have = new HashSet<(string, string)>(always);
                foreach (string key in set.SwapEvents)
                    if (Lookup(made, key) is { } chosen) have.UnionWith(Files(chosen));

                var want = Files(set);
                wanted += want.Count;
                loaded += want.Count(have.Contains);
            }
        }

        // what is not loaded is mostly not derivable: keys no transition takes any more
        // (the dialogue idle_A_*Trans idles), and the first-person killmoves copied into
        // each of the draugr's sets (§5.7)
        Assert.Equal(2689, wanted);
        Assert.Equal(2145, loaded);
    }

    [CorpusFact]
    public void TheFirstPersonKillmovesMovedFromFirstPersonToTheVictim()
    {
        var snapshot = SplitCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, SkyrimCache.AnimationSetDataFileName));

        HashSet<(string, string)> All(ProjectAttackListBlock sets) => [.. sets.Sets.SelectMany(Files)];
        var before = All(snapshot.Sets.Single(p => p.Name.StartsWith("FirstPerson", StringComparison.OrdinalIgnoreCase)).Sets);
        var after = All(shipped.Project("FirstPerson")!.Sets);

        string killmoves = HavokCrc.Text("meshes\\actors\\sharedkillmoves\\1stperson\\human&bear");
        var bear = All(shipped.Project("BearProject")!.Sets).Where(f => f.Item1 == killmoves).ToList();

        // the bear's five first-person killmoves: first person's in the pre-DLC snapshot,
        // the bear's in the shipped file, and no longer first person's
        Assert.Equal(5, bear.Count);
        Assert.All(bear, f => Assert.Contains(f, before));
        Assert.All(bear, f => Assert.DoesNotContain(f, after));
    }

    // The race's question (§4.6): no key, both hand types; a set without hand variables
    // never answers it, and a project with a single set answers with that set.
    private static ProjectAttackBlock? AttacksFor(AnimationSetDataProject project, int right, int left) =>
        project.Sets.Sets.FirstOrDefault(s =>
            s.HandVariables.Variables.Count > 0 &&
            s.HandVariables.Variables.All(v => v.Name switch
            {
                "iRightHandType" => v.Min <= right && right <= v.Max,
                "iLeftHandType" => v.Min <= left && left <= v.Max,
                _ => v.Min <= 0 && 0 <= v.Max,
            }))
        ?? (project.Sets.Sets.Count == 1 ? project.Sets.Sets[0] : null);

    [MastersFact]
    public void EveryWeaponARaceFindsAttacksForInTheShippedFileItFindsAttacksFor()
    {
        int answered = 0, lost = 0;

        foreach (AnimationSetDataProject shipped in _built.Shipped.Projects)
        {
            AnimationSetDataProject made = _built.Made.Project(shipped.Name)!;
            foreach ((int right, int left) in HandCombinations.All)
            {
                if (AttacksFor(shipped, right, left) is not { Attacks.Attacks.Count: > 0 }) continue;

                answered++;
                if (AttacksFor(made, right, left) is not { Attacks.Attacks.Count: > 0 }) lost++;
            }
        }

        // a weapon set has to come before the sets split by weapon for an idle event, which
        // hold no attacks; sorted by name, those answered first for the empty hands
        Assert.Equal(4538, answered);
        Assert.Equal(0, lost);
    }

    [MastersFact]
    public void TheMovingAttackFlagIsDerivedWhereTheAttackTravelsAtTheActorsSpeed()
    {
        // What follows from the assets and the engine's own logic: a blender above the clips is
        // parametric on the actor's speed, so no single root motion exists; the graph says the
        // character is sprinting over that state -- the same case with no blend, because
        // sprinting has one direction; the idle tree only chooses the attack on the move; or the
        // controller carries the creature always, which is what a hovering creature's graph shows
        // (no speed blend, no root motion under its direction blends). Never inside a branch that
        // raises bAnimationDriven, where the clip's root motion moves the character
        // (docs/animation-set-data.md §4.6, §6).
        //
        // 33 of vanilla's 38 come out. The eight extra are vanilla's own omissions: the Vampire
        // Lord carries the player's speed-parametric blend in a project that flags nothing, the
        // werewolf flags AttackStartDualSprinting while leaving its left and right alone, and the
        // wisp hovers exactly as the witchlight does. The one deliberate difference is the storm
        // atronach's standing power attack, which vanilla flags although the graph drives it by
        // animation.
        var flagged = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AnimationSetDataProject project in _built.Made.Projects)
            foreach (AttackData attack in project.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
                if (attack.MovingAttack != 0)
                    flagged.Add($"{project.Stem}/{attack.EventName}");

        string[] expected =
            [
                "AtronachStormProject/attackPowerStart_ForwardAttack", "AtronachStormProject/attackStart_Attack_Swipe",
                "DefaultFemale/attackStart", "DefaultFemale/AttackStartH2HLeft",
                "DefaultFemale/AttackStartH2HRight", "DefaultFemale/attackStartLeftHand",
                "DefaultFemale/attackPowerStart_2HMSprint", "DefaultFemale/attackPowerStart_2HWSprint",
                "DefaultFemale/attackPowerStart_Sprint", "DefaultFemale/attackPowerStart_SprintLeftHand",
                "DefaultFemale/attackStartSprint", "DefaultFemale/attackStartSprintLeftHand",
                "DefaultMale/attackStart", "DefaultMale/AttackStartH2HLeft",
                "DefaultMale/AttackStartH2HRight", "DefaultMale/attackStartLeftHand",
                "DefaultMale/attackPowerStart_2HMSprint", "DefaultMale/attackPowerStart_2HWSprint",
                "DefaultMale/attackPowerStart_Sprint", "DefaultMale/attackPowerStart_SprintLeftHand",
                "DefaultMale/attackStartSprint", "DefaultMale/attackStartSprintLeftHand",
                "NetchProject/attackStartLeft", "NetchProject/attackStartRight",
                "VampireLord/attackStartLeft", "VampireLord/attackStartRight",
                "WerewolfBeastProject/AttackStartBackHand", "WerewolfBeastProject/AttackStartDual",
                "WerewolfBeastProject/AttackStartDualRunning",
                "WerewolfBeastProject/AttackStartDualSprinting",
                "WerewolfBeastProject/AttackStartLeftRunningPower",
                "WerewolfBeastProject/AttackStartRightRunningPower",
                "WerewolfBeastProject/AttackStartLeftSprinting",
                "WerewolfBeastProject/AttackStartRightSprinting",
                "WerewolfBeastProject/attackStartLeft", "WerewolfBeastProject/attackStartRight",
                "WispProject/attackStart_Attack1", "WispProject/attackStart_Attack2",
                "WispProject/attackStart_TouchPush", "WispProject/bashStart",
                "WitchlightProject/attackStart_Attack1",
            ];

        Assert.Equal(new SortedSet<string>(expected, StringComparer.OrdinalIgnoreCase), flagged);
    }

    [MastersFact]
    public void AnAttackIsTheNestedStateItsTransitionNames()
    {
        // the chaurus's attacks all enter one state; the nested state id is what separates them
        ProjectAttackBlock set = Assert.Single(_built.Made.Project("ChaurusProject")!.Sets.Sets);

        Assert.Equal(["Attack_RBite"], set.Attacks.Attacks.Single(a => a.EventName == "attackStart_Bite1").Clips);
        Assert.Equal(["Attack_RBite[Mirror]"], set.Attacks.Attacks.Single(a => a.EventName == "attackStart_Bite2").Clips);
    }
}
