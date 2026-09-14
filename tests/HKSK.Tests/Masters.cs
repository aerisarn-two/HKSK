using Mutagen.Bethesda.Skyrim;

namespace HKSK.Tests;

/// <summary>
/// Reads the movement types out of the game's masters.
/// </summary>
/// <remarks>
/// It lives in the test project because
/// <c>HKSK</c> reads Havok files and the animation cache and has no business
/// opening plugins -- a caller that has them hands the speeds in as
/// <see cref="MovementType"/>.
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

    /// <summary>The masters, in load order.</summary>
    /// <remarks>
    /// Named rather than discovered. <c>GameEnvironment.Typical</c> wants the
    /// <c>plugins.txt</c> the launcher writes, which does not exist on a machine
    /// that has never run the game, so the five shipped masters are opened
    /// directly and in order -- later ones overriding earlier, which is all a
    /// load order does for records nothing else touches.
    /// </remarks>
    private static readonly string[] Order =
        ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    /// <summary>Every movement type the masters define, by name.</summary>
    public static IReadOnlyDictionary<string, MovementType> Read()
    {
        var types = new Dictionary<string, MovementType>(StringComparer.OrdinalIgnoreCase);

        foreach (string master in Order)
        {
            string path = Path.Combine(DataFolder!, master);
            if (!File.Exists(path)) continue;

            using var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);

            foreach (IMovementTypeGetter movement in mod.MovementTypes)
            {
                // iState_<MOVT> carries the Name, not the EditorID: the falmer's
                // Falmer_1HM_Walk record is named Falmer1HMWalk.
                string? name = movement.Name ?? movement.EditorID;
                if (string.IsNullOrEmpty(name)) continue;

                types[name] = new MovementType(
                    name,
                    movement.ForwardWalk, movement.ForwardRun,
                    movement.BackWalk, movement.BackRun,
                    movement.LeftWalk, movement.LeftRun,
                    movement.RightWalk, movement.RightRun);
            }
        }

        return types;
    }
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
