using HKSK.Records;
using MovementType = HKSK.Speed.MovementType;

namespace HKSK.Tests;

/// <summary>
/// Reads the movement types out of the game's masters.
/// </summary>
/// <remarks>
/// <c>HKSK</c> reads Havok files and the animation cache and has no business opening
/// plugins: <see cref="MasterRecords"/> reads them the way a caller would, and this is
/// the tests' way of finding the Data folder and asking the library what the records mean.
///
/// Set <c>HKSK_MASTERS</c> to the game's Data folder. Tests needing it skip when
/// it is unset, the way <see cref="Corpus"/> works for the meshes.
/// </remarks>
public static class Masters
{
    public const string EnvVar = "HKSK_MASTERS";

    public static string? DataFolder
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(value)) return null;

            string expanded = value.StartsWith("~/")
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
                : value;

            return Directory.Exists(expanded) ? expanded : null;
        }
    }

    public static bool Available => DataFolder is not null;

    internal static string[] Order => MasterRecords.Order;

    private static GameRecords? _records;

    /// <summary>The masters' records, read once per run.</summary>
    public static GameRecords Records => _records ??= MasterRecords.Read(DataFolder!);

    /// <summary>Every movement type the masters define, by name.</summary>
    public static IReadOnlyDictionary<string, MovementType> Read() => GameRecordRules.MovementTypes(Records);

    /// <summary>
    /// The roles each race gives a movement type, by the type's name.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<MovementRole>> RaceRoles() => GameRecordRules.MovementRoles(Records);
}

/// <summary>
/// A fact that needs both the extracted meshes and the game's masters.
/// </summary>
/// <remarks>
/// The movement types are the third input to a rebuild and the only one that is
/// not in the animation cache, so a test about them needs the plugins as well as
/// the meshes. It skips rather than fails when either is absent.
/// </remarks>
public sealed class MastersFactAttribute : Xunit.FactAttribute
{
    public MastersFactAttribute()
    {
        if (!Corpus.Available)
            Skip = $"set {Corpus.EnvVar} to an extracted meshes directory to run this";
        else if (!Masters.Available)
            Skip = $"set {Masters.EnvVar} to the game's Data folder to run this";
    }
}
