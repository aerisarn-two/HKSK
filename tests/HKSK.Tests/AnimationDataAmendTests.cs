using HKSK.AnimationData;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Bringing the animation data up to date with the Havok files, against the shipped game.
/// </summary>
public class AnimationDataAmendTests
{
    /// <summary>
    /// The files have not changed, so nothing does: all 429 entries come back byte for byte,
    /// the horse's and the werewolf's stale numbering, the doubled <c>SmallBird01</c> and the
    /// moth, whose name finds an effect's packfile first, included.
    /// </summary>
    [CorpusFact]
    public void AmendingTheShippedGameChangesNothing()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        string before = cache.AnimationData.Write();

        Assert.Empty(AnimationDataGenerator.Amend(cache));
        Assert.Equal(429, cache.AnimationData.Projects.Count);
        Assert.Equal(before, cache.AnimationData.Write());
    }

    /// <summary>
    /// A new clip's events are derived, and the derivation agrees with the cache exactly for
    /// 9,973 of the game's 10,556 clips. The rest are mostly drift -- the dog's graph now
    /// triggers <c>NPCFoxBreatheRun</c>, which none of its cached lists carries -- which is why
    /// a cached list is kept rather than derived again.
    /// </summary>
    [CorpusFact]
    public void ANewClipsEventsAreDerivedAsTheCacheHasThem()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        int clips = 0, exact = 0;

        foreach (ActorProject project in cache.Actors())
        {
            if (!project.HasCache || cache.FindProjectFile(project.Name) is not { } path) continue;
            var inputs = AnimationDataGenerator.Inputs.Read(path);

            foreach ((HKX2.hkbClipGenerator generator, string file) in inputs.Generators)
            {
                if (project.Data.Block.Clips.FirstOrDefault(c => c.Name == generator.m_name) is not { } cached) continue;

                List<ClipEvent> derived = [.. AnimationDataGenerator.ClipEvents.Derive(generator, file, inputs, cached.CacheIndex)];
                clips++;
                if (derived.Count == cached.Events.Count && derived.Zip(cached.Events).All(p =>
                        p.First.Name == p.Second.Name && MathF.Abs(p.First.Time - p.Second.Time) < 0.0005f))
                    exact++;
            }
        }

        Assert.Equal(10556, clips);
        Assert.Equal(9973, exact);
    }

    /// <summary>
    /// A creature the cache does not list is added from its files: every file and every clip
    /// the shipped entry has, each clip bound to the same slot with the same speed and crop.
    /// Its root motion is not in any Havok file, so it starts with none.
    /// </summary>
    [CorpusFact]
    public void ACreatureTheCacheLacksIsAddedFromItsFiles()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        AnimationDataProject shipped = cache.AnimationData.Project("WolfProject")!;
        cache.AnimationData.Projects.Remove(shipped);

        Assert.Equal(Amendment.Added, AnimationDataGenerator.Add(cache, "WolfProject", actor: true));

        AnimationDataProject added = cache.AnimationData.Projects[^1];
        Assert.Equal("WolfProject.txt", added.Name);
        Assert.True(added.Block.HasAnimationCache);
        Assert.Equal(shipped.Block.Files.Order(), added.Block.Files.Order());

        Assert.Equal(shipped.Block.Clips.Count, added.Block.Clips.Count);
        foreach (ClipGeneratorEntry clip in shipped.Block.Clips)
        {
            ClipGeneratorEntry made = Assert.Single(added.Block.Clips, c => c.Name == clip.Name);
            Assert.Equal(Text(clip), Text(made));
        }

        Assert.Empty(added.Movements!.Movements);

        // what the file says about a clip, bar its events, which are derived
        static string Text(ClipGeneratorEntry c) =>
            string.Join(' ', c.CacheIndex, CacheText.Float(c.PlaybackSpeed), CacheText.Float(c.CropStartTime), CacheText.Float(c.CropEndTime));
    }

    /// <summary>A clip the graph gained is added after the others, and nothing else moves.</summary>
    [CorpusFact]
    public void AClipTheGraphGainedIsAddedAtTheEnd()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        AnimationDataProject chicken = cache.AnimationData.Project("ChickenProject")!;
        ClipGeneratorEntry shipped = chicken.Block.Clips[0];
        chicken.Block.Clips.RemoveAt(0);
        string[] rest = [.. chicken.Block.Clips.Select(c => c.Name)];

        Assert.Equal(Amendment.Replaced, AnimationDataGenerator.Amend(cache, "ChickenProject"));

        Assert.Equal([.. rest, shipped.Name], chicken.Block.Clips.Select(c => c.Name));
        ClipGeneratorEntry made = chicken.Block.Clips[^1];
        Assert.Equal((shipped.CacheIndex, shipped.PlaybackSpeed), (made.CacheIndex, made.PlaybackSpeed));
        Assert.Equal(shipped.Events, made.Events);
    }

    /// <summary>
    /// A prop is added with no animation cache and its animations listed after its rig; the
    /// bow's character names its graph <c>..\Bow\Behaviors\BowBehavior.hkx</c>, and that is
    /// the file the list calls <c>Behaviors\BowBehavior.hkx</c>, listed once.
    /// </summary>
    [CorpusFact]
    public void APropTheCacheLacksIsAddedWithItsAnimations()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        AnimationDataProject shipped = cache.AnimationData.Project("BowProject")!;
        cache.AnimationData.Projects.Remove(shipped);

        Assert.Equal(Amendment.Added, AnimationDataGenerator.Add(cache, "BowProject", actor: false));

        AnimationDataProject added = cache.AnimationData.Projects[^1];
        Assert.False(added.Block.HasAnimationCache);
        Assert.Null(added.Movements);
        Assert.Equal(shipped.Block.Files.Count, added.Block.Files.Count);

        string folder = Path.GetDirectoryName(cache.FindProjectFile("BowProject"))!;
        Assert.Equal(Resolved(shipped.Block.Files), Resolved(added.Block.Files));

        IEnumerable<string> Resolved(IEnumerable<string> files) =>
            files.Select(f => Path.GetFullPath(HavokPath.Resolve(folder, f)!)).Order(StringComparer.OrdinalIgnoreCase);
    }

    [CorpusFact]
    public void OnlyAListedProjectIsAmendedAndOnlyOneWithFilesIsAdded()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        Assert.Throws<ArgumentException>(() => AnimationDataGenerator.Amend(cache, "NoSuchProject"));
        Assert.Throws<InvalidOperationException>(() => AnimationDataGenerator.Add(cache, "NoSuchProject", actor: true));
        Assert.Equal(Amendment.Unchanged, AnimationDataGenerator.Add(cache, "WolfProject", actor: false));
    }
}
