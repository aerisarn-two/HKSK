using HKSK.Cache;
using HKSK.Fbx;
using HKSK.Model;
using HKSK.Validation;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Skips a test when the corpus or Havok's spline codec is not available.
/// </summary>
/// <remarks>
/// Converting an animation needs mopper.exe, because Havok's spline encoder is
/// proprietary and this is the only credible implementation of it. It is a Win32
/// binary, so off Windows it also needs Wine. Neither is on a stock runner, so
/// these skip in CI and run where the work is actually done.
/// </remarks>
public sealed class FbxFactAttribute : FactAttribute
{
    public FbxFactAttribute()
    {
        if (!Corpus.Available) Skip = $"set {Corpus.EnvVar} to an extracted meshes directory to run this";
        else if (!Mopper.Available) Skip = "mopper.exe cannot be run here (not found, or no Wine off Windows)";
    }
}

/// <summary>Whether Havok's spline codec can actually be run here.</summary>
/// <remarks>
/// Finding mopper.exe is not enough. The Mopper.Native package copies it to the
/// output directory on every platform, including ones that cannot execute it, so
/// the file being present says nothing. Off Windows it is run through Wine, and
/// a stock Linux runner has no Wine -- which showed up as a failing CI job
/// rather than a skipped test until this checked for it.
/// </remarks>
internal static class Mopper
{
    public static bool Available { get; } = Find() is not null && CanExecute();

    private static string? Find()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "mopper.exe");
        if (File.Exists(beside)) return beside;

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, "mopper.exe"))
            .FirstOrDefault(File.Exists);
    }

    // Windows runs the 32-bit binary through WOW64; everything else needs Wine.
    private static bool CanExecute() =>
        OperatingSystem.IsWindows() ||
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Any(d => File.Exists(Path.Combine(d, "wine")));
}

/// <summary>
/// Moving animations out to FBX and back, carrying their events and root motion.
/// </summary>
/// <remarks>
/// Every test works on a copy of the chicken, never the corpus: these write
/// packfiles and cache files, and the corpus is read-only game data.
/// </remarks>
public class FbxExchangeTests
{
    /// <summary>
    /// The root motion the cache records reaches the FBX and comes back
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the pairing. HKFBX asks the caller for root
    /// motion because Havok keeps it apart from the skeleton and it can come
    /// from anywhere; in Skyrim it comes from the cache, which is what HKSK
    /// knows. If it did not survive, the two libraries would not be joined at
    /// all.
    ///
    /// The chicken's left turn is a good subject: it turns 90 degrees and
    /// travels nowhere, so a mix-up between translation and rotation would show.
    /// </remarks>
    [FbxFact]
    public void RootMotionSurvivesTheTripOutToFbxAndBack()
    {
        using var work = new Workspace();
        HavokProject project = work.Chicken();

        AnimationSlot turn = project.Animation("TurnLoopingL")!;
        float travelBefore = turn.Motion!.Travel;
        float turnBefore = turn.Motion.Turn;

        Assert.Equal(0f, travelBefore, 3);
        Assert.Equal(MathF.PI / 2f, turnBefore, 3);   // a right angle

        var exchange = new AnimationExchange();
        string fbx = Path.Combine(work.Folder, "turn.fbx");

        ExchangeResult exported = exchange.Export(project, turn, fbx);
        Assert.True(exported.Succeeded, exported.Problem);
        Assert.True(new FileInfo(fbx).Length > 1000, "the FBX should carry a skeleton and curves");

        ExchangeResult imported = exchange.Import(
            project, fbx, new ImportOptions { StoredName = turn.StoredName });
        Assert.True(imported.Succeeded, imported.Problem);

        // Replacing keeps the slot, so nothing pointing at it had to move.
        Assert.Equal(turn.Index, imported.CacheIndex);
        Assert.Equal(20, project.Animations.Count);

        AnimationSlot after = project.Animation("TurnLoopingL")!;
        Assert.Equal(travelBefore, after.Motion!.Travel, 2);
        Assert.Equal(turnBefore, after.Motion.Turn, 2);
    }

    /// <summary>
    /// A batch of FBX files becomes new animations, appended, with the existing
    /// numbering untouched.
    /// </summary>
    [FbxFact]
    public void ABatchImportAppendsSlotsAndLeavesTheNumberingAlone()
    {
        using var work = new Workspace();
        HavokProject project = work.Chicken();

        var before = project.Clips.ToDictionary(c => c.Name, c => c.CacheIndex, StringComparer.OrdinalIgnoreCase);
        var exchange = new AnimationExchange();

        string folder = Path.Combine(work.Folder, "out");
        Directory.CreateDirectory(folder);

        foreach (string name in new[] { "WalkForward", "RunForward", "TurnLoopingL" })
            exchange.Export(project, project.Animation(name)!, Path.Combine(folder, $"{name}.fbx"));

        IReadOnlyList<ExchangeResult> results = exchange.ImportAll(
            project,
            Directory.GetFiles(folder, "*.fbx").OrderBy(f => f, StringComparer.Ordinal),
            path => new ImportOptions
            {
                StoredName = $"Animations\\New_{Path.GetFileNameWithoutExtension(path)}.hkx",
                ClipName = $"New_{Path.GetFileNameWithoutExtension(path)}",
            });

        Assert.All(results, r => Assert.True(r.Succeeded, r.Problem));
        Assert.Equal(3, results.Count);

        // Appended: 20 + 3, and the new ones took the three positions after.
        Assert.Equal(23, project.Animations.Count);
        Assert.Equal([20, 21, 22], results.Select(r => r.CacheIndex!.Value).OrderBy(i => i));

        // Nothing that already existed moved.
        foreach (Clip clip in project.Clips)
            if (before.TryGetValue(clip.Name, out int index))
                Assert.Equal(index, clip.CacheIndex);

        // And the motion came with them.
        AnimationSlot run = project.Animation("New_RunForward")!;
        Assert.Equal(project.Animation("RunForward")!.Motion!.Travel, run.Motion!.Travel, 1);
    }

    /// <summary>
    /// The animation list lives in the character packfile, so saving the cache
    /// alone is not enough -- and the library says so rather than leaving a
    /// cache pointing at slots that do not exist.
    /// </summary>
    [FbxFact]
    public void AddingAnAnimationNeedsTheCharacterFileSavedToo()
    {
        using var work = new Workspace();
        HavokProject project = work.Chicken();

        var exchange = new AnimationExchange();
        string fbx = Path.Combine(work.Folder, "walk.fbx");
        exchange.Export(project, project.Animation("WalkForward")!, fbx);

        exchange.Import(project, fbx, new ImportOptions { StoredName = "Animations\\Added.hkx" });

        Assert.True(project.CharacterModified);
        Assert.Contains(ConsistencyReport.Check(project), f => f.Kind == "unsaved-animation-list");

        project.SaveCharacter();
        work.Cache.Save();

        Assert.False(project.CharacterModified);

        // Both halves are on disk now, so a fresh read agrees with itself.
        HavokProject reread = SkyrimCache.Load(work.Meshes).Open("ChickenProject")!;

        Assert.Equal(21, reread.Animations.Count);
        Assert.NotNull(reread.Animation("Added"));
        Assert.DoesNotContain(ConsistencyReport.Check(reread), f => f.Kind == "unsaved-animation-list");
    }

    /// <summary>
    /// Options that name one animation are refused for a batch rather than
    /// applied to an arbitrary member of it.
    /// </summary>
    [Fact]
    public void ABatchRefusesOptionsThatNameASingleAnimation()
    {
        var exchange = new AnimationExchange();
        HavokProject project = HavokProject.Open(Fake.Data());
        string[] paths = ["a.fbx", "b.fbx"];

        Assert.Throws<ArgumentException>(() =>
            exchange.ImportAll(project, paths, new ImportOptions { StoredName = "Animations\\One.hkx" }));

        Assert.Throws<ArgumentException>(() =>
            exchange.ImportAll(project, paths, new ImportOptions { ClipName = "One" }));
    }

    /// <summary>A failure is reported, not thrown, so a batch keeps going.</summary>
    [Fact]
    public void AFailureIsReportedRatherThanThrown()
    {
        var exchange = new AnimationExchange();
        HavokProject project = HavokProject.Open(Fake.Data());

        IReadOnlyList<ExchangeResult> results =
            exchange.ImportAll(project, ["nowhere/a.fbx", "nowhere/b.fbx"]);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.All(results, r => Assert.NotNull(r.Problem));
    }

    /// <summary>A copy of the chicken to edit, deleted afterwards.</summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"hksk-fbx-{Guid.NewGuid():N}");
            Meshes = Path.Combine(Folder, "meshes");

            Copy(Corpus.Path_("actors", "ambient", "chicken"),
                 Path.Combine(Meshes, "actors", "ambient", "chicken"));

            Directory.CreateDirectory(Meshes);
            File.Copy(Corpus.AnimationData, Path.Combine(Meshes, SkyrimCache.AnimationDataFileName));
            File.Copy(Corpus.AnimationSetData, Path.Combine(Meshes, SkyrimCache.AnimationSetDataFileName));

            Cache = SkyrimCache.Load(Meshes);
        }

        public string Folder { get; }
        public string Meshes { get; }
        public SkyrimCache Cache { get; }

        public HavokProject Chicken() =>
            Cache.Open("ChickenProject") ?? throw new InvalidOperationException("no chicken in the cache");

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);

            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);

            foreach (string folder in Directory.GetDirectories(from))
                Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
        }
    }
}
