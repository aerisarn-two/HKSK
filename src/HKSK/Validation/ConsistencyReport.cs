using HKSK.Cache;
using HKSK.Model;

namespace HKSK.Validation;

/// <summary>How much a finding matters.</summary>
public enum Severity
{
    /// <summary>The cache disagrees with the Havok files, but the game will run.</summary>
    Drift,

    /// <summary>The cache is internally inconsistent and will misbehave.</summary>
    Error,
}

/// <summary>One thing wrong with a project.</summary>
public sealed record Finding(Severity Severity, string Kind, string Message)
{
    public override string ToString() => $"{Severity}/{Kind}: {Message}";
}

/// <summary>
/// Checks a project's cache against the Havok files it claims to describe.
/// </summary>
/// <remarks>
/// Worth knowing before reading the output: Skyrim's own data does not pass
/// cleanly. Of the projects whose Havok files resolve, four carry caches that
/// were generated before their behaviour graphs gained clips -- the dragon and
/// draugr gained Dawnguard's <c>DLC01\Special_*</c> animations, the falmer
/// gained a staff set -- and their cache indices are consistent with an older
/// animation list. That is why <see cref="Severity.Drift"/> exists as something
/// distinct from an error.
///
/// The useful consequence for an editor is that the cache index numbering cannot
/// be recomputed from the Havok files and then written back: doing so would
/// "fix" those four projects into disagreeing with the game. The numbering is
/// data, and it is preserved.
/// </remarks>
public static class ConsistencyReport
{
    /// <summary>Checks one project.</summary>
    public static IReadOnlyList<Finding> Check(HavokProject project)
    {
        var findings = new List<Finding>();

        CheckCacheInternals(project, findings);
        if (project.HasHavok) CheckAgainstHavok(project, findings);

        return findings;
    }

    private static void CheckCacheInternals(HavokProject project, List<Finding> findings)
    {
        if (project.HasCache && project.Data.Movements is null)
            findings.Add(new Finding(Severity.Error, "missing-movements",
                $"'{project.Name}' declares an animation cache but carries no root motion block"));

        // Saving the cache does not save the animation list: it lives in the
        // character packfile. A cache written without it refers to slots the
        // character does not have.
        if (project.CharacterModified)
            findings.Add(new Finding(Severity.Error, "unsaved-animation-list",
                $"'{project.Name}' has animations added or removed that are still only in memory; " +
                "call SaveCharacter() as well as saving the cache"));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ClipGeneratorEntry clip in project.Data.Block.Clips)
            if (!seen.Add(clip.Name))
                findings.Add(new Finding(Severity.Error, "duplicate-clip",
                    $"'{project.Name}' lists the clip '{clip.Name}' more than once"));

        var indices = new HashSet<int>();
        foreach (ClipMovement movement in project.Data.Movements?.Movements ?? [])
            if (!indices.Add(movement.CacheIndex))
                findings.Add(new Finding(Severity.Error, "duplicate-motion",
                    $"'{project.Name}' records root motion for slot {movement.CacheIndex} more than once"));

        foreach (ClipGeneratorEntry clip in project.Data.Block.Clips)
            if (clip.CacheIndex < 0)
                findings.Add(new Finding(Severity.Error, "negative-index",
                    $"clip '{clip.Name}' has cache index {clip.CacheIndex}"));

        // Checksum blocks are three lines per animation; anything else means the
        // block was edited without keeping the triples whole.
        foreach (ProjectAttackBlock set in project.Sets?.Sets.Sets ?? [])
            if (set.Checksums.Entries.Count % 3 != 0)
                findings.Add(new Finding(Severity.Error, "ragged-checksums",
                    $"'{project.Name}' has a checksum block of {set.Checksums.Entries.Count} lines, " +
                    "which is not three per animation"));
    }

    private static void CheckAgainstHavok(HavokProject project, List<Finding> findings)
    {
        int animations = project.Character!.AnimationNames.Count;

        foreach (ClipGeneratorEntry clip in project.Data.Block.Clips)
            if (clip.CacheIndex >= animations)
                findings.Add(new Finding(Severity.Drift, "index-out-of-range",
                    $"clip '{clip.Name}' points at slot {clip.CacheIndex}, but the character file " +
                    $"lists {animations} animations"));

        foreach (ClipMovement movement in project.Data.Movements?.Movements ?? [])
            if (movement.CacheIndex >= animations)
                findings.Add(new Finding(Severity.Drift, "motion-out-of-range",
                    $"root motion is recorded for slot {movement.CacheIndex}, but the character file " +
                    $"lists {animations} animations"));

        // The load-bearing check: the cache index must be where the character
        // file puts that clip's animation.
        foreach (Clip clip in project.Clips)
        {
            if (clip.Generator is null)
            {
                findings.Add(new Finding(Severity.Drift, "clip-not-in-behavior",
                    $"the cache lists '{clip.Name}', which no loaded behaviour defines"));
                continue;
            }

            int expected = IndexOf(project.Character.AnimationNames, clip.Generator.m_animationName);

            if (expected < 0)
                findings.Add(new Finding(Severity.Drift, "animation-not-listed",
                    $"clip '{clip.Name}' plays '{clip.Generator.m_animationName}', which the " +
                    "character file does not list"));
            else if (expected != clip.CacheIndex)
                findings.Add(new Finding(Severity.Drift, "index-mismatch",
                    $"clip '{clip.Name}' plays '{clip.Generator.m_animationName}' at slot " +
                    $"{expected}, but the cache says {clip.CacheIndex}"));
        }

        // The cache restates the generator's speed and crop times. Unlike the
        // event list, these are copied verbatim: across the shipped game all
        // 10,556 clips that have a generator agree on both, so any difference
        // here is a real edit that did not reach the cache.
        foreach (Clip clip in project.Clips)
        {
            if (clip.Generator is null) continue;

            if (!Near(clip.Generator.m_playbackSpeed, clip.Entry.PlaybackSpeed))
                findings.Add(new Finding(Severity.Drift, "speed-mismatch",
                    $"clip '{clip.Name}' plays at {clip.Generator.m_playbackSpeed} in the behaviour " +
                    $"but {clip.Entry.PlaybackSpeed} in the cache"));

            if (!Near(clip.Generator.m_cropStartAmountLocalTime, clip.Entry.CropStartTime) ||
                !Near(clip.Generator.m_cropEndAmountLocalTime, clip.Entry.CropEndTime))
                findings.Add(new Finding(Severity.Drift, "crop-mismatch",
                    $"clip '{clip.Name}' is cropped " +
                    $"{clip.Generator.m_cropStartAmountLocalTime}/{clip.Generator.m_cropEndAmountLocalTime} " +
                    $"in the behaviour but " +
                    $"{clip.Entry.CropStartTime}/{clip.Entry.CropEndTime} in the cache"));
        }

        // Every generator the behaviours define should be in the cache -- across
        // the shipped game, every one of the 10,550 is. A generator the cache
        // does not list is a clip the game cannot play.
        var cached = new HashSet<string>(
            project.Data.Block.Clips.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (Havok.BehaviorFile behavior in project.Behaviors)
            foreach (HKX2.hkbClipGenerator generator in behavior.Clips)
                if (!cached.Contains(generator.m_name))
                    findings.Add(new Finding(Severity.Drift, "generator-not-cached",
                        $"the behaviour defines '{generator.m_name}', which the cache does not list"));

        foreach (string missing in project.MissingBehaviors)
            findings.Add(new Finding(Severity.Drift, "behavior-not-found",
                $"'{project.Name}' lists the behaviour '{missing}', which was not found"));
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-6f;

    private static int IndexOf(IList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
