using HKSK.Havok;
using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// What the game's own paired animations are, checked against the game.
/// </summary>
/// <remarks>
/// The pairing rules were measured here before they were written, and these hold
/// them to it. The counts are exact where the shipped data is exact, because a
/// change in them means the format has been read differently rather than that a
/// mod is unusual.
/// </remarks>
public class PairedCorpusTests
{
    /// <summary>
    /// Every animation a project lists, opened once and shared: the sweep parses a
    /// few hundred packfiles and is far too slow to repeat per test.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<(string Path, PairedAnimation Paired, List<string> Projects)>> Combined =
        new(() =>
        {
            SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
            var listedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (ActorProject project in cache.Actors())
                foreach (AnimationSlot slot in project.Animations)
                {
                    if (slot.StoredName.Length == 0 || project.AnimationPath(slot) is not { } path) continue;

                    string key = Path.GetFullPath(path);

                    if (!listedBy.TryGetValue(key, out var projects)) listedBy[key] = projects = [];
                    if (!projects.Contains(project.Name)) projects.Add(project.Name);
                }

            List<(string, PairedAnimation, List<string>)> combined = [];

            foreach ((string path, List<string> projects) in listedBy)
            {
                if (projects.Count < 2
                    && !Path.GetFileName(path).Contains("paired", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    PairedAnimation paired = PairedAnimation.Read(path);

                    if (paired.IsCombined) combined.Add((path, paired, projects));
                }
                catch
                {
                    // One of the game's own: a listed animation holding no
                    // animation. PairingReport is where that is reported.
                }
            }

            return combined;
        });

    [CorpusFact]
    public void TheGameHasTheParedAnimationsTheRulesWereMeasuredFrom() =>
        Assert.Equal(294, Combined.Value.Count);

    /// <summary>
    /// A pairing is two actors, never three: 23,824 prefixed tracks in the game
    /// and not one of them <c>3_</c>.
    /// </summary>
    [CorpusFact]
    public void OnlyOnePrefixIsEverUsed()
    {
        var prefixes = Combined.Value
            .SelectMany(entry => entry.Paired.Tracks)
            .Where(name => name.Length > 2 && char.IsDigit(name[0]) && name[1] == '_')
            .Select(name => name[..2])
            .Distinct()
            .ToList();

        Assert.Equal([PairedAnimation.PartnerPrefix], prefixes);
    }

    /// <summary>
    /// The binding usually says what it is rigged to, and it is nearly always
    /// <c>PairedRoot</c> -- which is why detection starts there and does not stop
    /// there.
    /// </summary>
    /// <remarks>
    /// Two of the game's own are rooted at the viewer instead and bind to
    /// <c>NPC</c>: first-person killmoves, which put <c>PairedRoot</c> inside the
    /// tree rather than at the top of it. An exporter that trusted the binding
    /// alone would hand those back as ordinary animations, which is the bug this
    /// whole thing exists to remove, so the track count is checked as well.
    /// </remarks>
    [CorpusFact]
    public void NearlyAllOfThemBindToThePairedRoot()
    {
        var otherwise = Combined.Value
            .Where(entry => !PairedAnimation.IsPairedSkeletonName(entry.Paired.BoundSkeletonName))
            .ToList();

        Assert.Equal(2, otherwise.Count);
        Assert.All(otherwise, entry =>
            Assert.Equal(PairedAnimation.DriverRootBone, entry.Paired.BoundSkeletonName));
    }

    /// <summary>
    /// Both actors hang from a common root, and each half usually has a subtree
    /// root of its own.
    /// </summary>
    /// <remarks>
    /// One first-person killmove places the partner's root bone with no subtree
    /// root above it, which is why the rig builder treats that bone as optional
    /// rather than indexing it blindly.
    /// </remarks>
    [CorpusFact]
    public void EveryOneOfThemHasAPairRoot()
    {
        Assert.All(Combined.Value, entry =>
            Assert.True(entry.Paired.RootTrack >= 0,
                $"{Path.GetFileName(entry.Path)} has no {PairedAnimation.RootBone}"));

        int withoutPartnerRoot = Combined.Value.Count(entry => entry.Paired.PartnerRootTrack < 0);

        Assert.Equal(1, withoutPartnerRoot);
    }

    /// <summary>
    /// Whatever the layout, the combined skeleton builds: every track gets a bone,
    /// exactly one bone is rootless, and nothing is its own ancestor.
    /// </summary>
    [CorpusFact]
    public void EveryOneOfThemBuildsARig() =>
        Assert.All(Combined.Value, entry =>
        {
            var rig = Fbx.PairedRig.Build(entry.Paired, null, null);

            Assert.Equal(entry.Paired.Tracks.Count, rig.Count);
            Assert.Single(rig.Bones.Where(bone => bone.ParentIndex < 0));

            for (int bone = 0; bone < rig.Count; bone++)
            {
                int walk = bone;

                for (int step = 0; walk >= 0; step++)
                {
                    Assert.True(step <= rig.Count, $"{Path.GetFileName(entry.Path)} has a parent cycle");
                    walk = rig.Bones[walk].ParentIndex;
                }
            }
        });

    /// <summary>
    /// The rule that looks obvious and is false. Half of them are listed by a
    /// single project -- the first-person killmoves, whose partner half is applied
    /// to an actor that never names the file -- which is why nothing treats an
    /// unshared pairing as broken.
    /// </summary>
    [CorpusFact]
    public void HalfOfThemAreListedByOneProjectAlone()
    {
        int alone = Combined.Value.Count(entry => entry.Projects.Count == 1);

        Assert.Equal(139, alone);
        Assert.Equal(155, Combined.Value.Count - alone);
    }

    /// <summary>
    /// A project that lists one always has a clip over it. This is the rule the
    /// unshared one was mistaken for.
    /// </summary>
    [CorpusFact]
    public void EveryProjectThatListsOneCanPlayIt()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);

        var findings = PairingReport.Check(cache)
            .Where(finding => finding.Kind == "paired-no-clip")
            .ToList();

        Assert.Empty(findings);
    }

    /// <summary>
    /// The whole report against the shipped game, which is one finding: a listed
    /// animation that holds no animation.
    /// </summary>
    [CorpusFact]
    public void TheGameItselfPassesThePairingRules()
    {
        var findings = PairingReport.Check(SkyrimCache.Load(Corpus.Root!));

        Assert.All(findings, finding => Assert.Equal(Severity.Drift, finding.Severity));
        Assert.Single(findings);
    }

    /// <summary>
    /// Who is in a pairing comes from the file lists and nowhere else. Three
    /// projects list the bear's killmove, two of them views of the same human.
    /// </summary>
    [CorpusFact]
    public void FindsTheProjectsThatShareAKillmove()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        ActorProject bear = cache.OpenActor("BearProject")!;

        AnimationSlot slot = bear.Animations.First(
            animation => animation.StoredName.Contains("Paired_1HMKillMoveBearA", StringComparison.OrdinalIgnoreCase));

        var participants = cache.ParticipantsOf(bear.AnimationPath(slot)!);

        Assert.Equal(
            ["BearProject", "DefaultFemale", "DefaultMale"],
            participants.Select(part => part.Project.Name).Order());

        // Each numbers it for itself: an index only means anything in one project.
        Assert.Contains(participants, part => part.Slot.Index == 17);
        Assert.Contains(participants, part => part.Slot.Index == 1165);
    }
}
