using HKSK.AnimationData;
using HKSK.Records;
using HKSK.SetData;
using HKSK.Speed;

namespace HKSK.Model;

/// <summary>How the generated files are shaped where the game leaves a choice.</summary>
public sealed record CacheGenerationOptions
{
    /// <summary>How far a set covering several weapons may outgrow one (<see cref="SetDataGenerator.DefaultSlack"/>).</summary>
    public double Slack { get; init; } = SetDataGenerator.DefaultSlack;

    /// <summary>How far a dropped speed point may sit from its line (<see cref="SpeedDataGenerator.DefaultTolerance"/>).</summary>
    public float Tolerance { get; init; } = SpeedDataGenerator.DefaultTolerance;
}

/// <summary>What amending one project did to each of the three merged files.</summary>
public sealed record CacheAmendment(string Project, Amendment AnimationData, Amendment SetData, Amendment SpeedData);

/// <summary>
/// The three merged caches brought up to date together: the animation data, the set data
/// read from it, and the speed table sampled from it.
/// </summary>
/// <remarks>
/// <para>
/// The order is the dependency: the set data names the animation data's clips and the speed
/// table reads its root motion, so the animation data is amended first. The game's records
/// come from the caller (<see cref="IGameRecords"/>); nothing here opens a plugin.
/// </para>
/// <para>
/// Nothing is written to disk. The cache is changed in memory and <see cref="SkyrimCache.Save()"/>
/// writes it.
/// </para>
/// </remarks>
public static class CacheGeneration
{
    /// <summary>
    /// Brings one project up to date in all three files, adding it where the cache does not list
    /// it yet -- as an actor when a race wears it -- and leaving every other project as it was.
    /// </summary>
    /// <exception cref="InvalidOperationException">The project's Havok files cannot be found.</exception>
    public static CacheAmendment Amend(
        SkyrimCache cache, string projectName, IGameRecords records, CacheGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(records);
        options ??= new CacheGenerationOptions();

        string stem = Path.GetFileNameWithoutExtension(projectName.Replace('\\', '/'));
        bool actor = GameRecordRules.ActorProjects(records).Contains(stem);

        Amendment data = AnimationDataGenerator.Add(cache, stem, actor);
        Amendment sets = SetDataGenerator.Amend(cache, stem, GameRecordRules.Events(records), options.Slack);
        Amendment speed = SpeedDataGenerator.Amend(cache, stem, GameRecordRules.MovementTypes(records), options.Tolerance);

        return new CacheAmendment(stem, data, sets, speed);
    }

    /// <summary>
    /// Brings every project up to date: the animation data amended entry by entry, since its
    /// root motion and event lists cannot be rebuilt, and the set data and speed table
    /// generated whole, reading nothing from the shipped ones.
    /// </summary>
    /// <returns>The animation data entries that changed.</returns>
    public static IReadOnlyList<string> Regenerate(SkyrimCache cache, IGameRecords records, CacheGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(records);
        options ??= new CacheGenerationOptions();

        IReadOnlyList<string> changed = AnimationDataGenerator.Amend(cache);

        // Built from the other assets alone: what the old files say is dropped before
        // anything can ask it.
        cache.SetData.Projects.Clear();
        cache.SpeedData = null;

        cache.SetData.Projects.AddRange(SetDataGenerator.Generate(cache, GameRecordRules.Events(records), options.Slack).Projects);
        cache.SpeedData = SpeedDataGenerator.Generate(cache, GameRecordRules.MovementTypes(records), options.Tolerance);
        return changed;
    }
}
