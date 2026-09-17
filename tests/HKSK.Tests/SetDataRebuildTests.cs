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

        Assert.Equal(2470, _built.Made.Projects.Sum(p => p.Sets.Sets.Count));
        Assert.Equal(95532, _built.Made.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Checksums.Entries.Count / 3)));
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

    [MastersFact]
    public void AnAttackIsTheNestedStateItsTransitionNames()
    {
        // the chaurus's attacks all enter one state; the nested state id is what separates them
        ProjectAttackBlock set = Assert.Single(_built.Made.Project("ChaurusProject")!.Sets.Sets);

        Assert.Equal(["Attack_RBite"], set.Attacks.Attacks.Single(a => a.EventName == "attackStart_Bite1").Clips);
        Assert.Equal(["Attack_RBite[Mirror]"], set.Attacks.Attacks.Single(a => a.EventName == "attackStart_Bite2").Clips);
    }
}
