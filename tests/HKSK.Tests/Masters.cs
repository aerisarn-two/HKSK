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
    internal static readonly string[] Order =
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

    /// <summary>
    /// The roles each race gives a movement type -- <c>walk</c>, <c>run</c>,
    /// <c>swim</c>, <c>fly</c>, <c>sneak</c>, <c>sprint</c> -- by the type's name.
    /// </summary>
    /// <remarks>
    /// A race's base movement defaults are the only place the masters point at a
    /// movement type, apart from the default object manager naming the player's.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> RaceRoles()
    {
        var names = new Dictionary<Mutagen.Bethesda.Plugins.FormKey, string>();
        var roles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<ISkyrimModDisposableGetter>();

        try
        {
            foreach (string master in Order)
            {
                string path = Path.Combine(DataFolder!, master);
                if (!File.Exists(path)) continue;

                var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
                mods.Add(mod);

                foreach (IMovementTypeGetter movement in mod.MovementTypes)
                    if ((movement.Name ?? movement.EditorID) is { Length: > 0 } name)
                        names[movement.FormKey] = name;
            }

            foreach (var mod in mods)
                foreach (IRaceGetter race in mod.Races)
                    foreach ((string role, var link) in new (string, Mutagen.Bethesda.Plugins.IFormLinkNullableGetter<IMovementTypeGetter>)[]
                             {
                                 ("walk", race.BaseMovementDefaultWalk), ("run", race.BaseMovementDefaultRun),
                                 ("swim", race.BaseMovementDefaultSwim), ("fly", race.BaseMovementDefaultFly),
                                 ("sneak", race.BaseMovementDefaultSneak), ("sprint", race.BaseMovementDefaultSprint),
                             })
                    {
                        if (link.IsNull || !names.TryGetValue(link.FormKey, out string? name)) continue;
                        if (!roles.TryGetValue(name, out SortedSet<string>? set)) roles[name] = set = [];
                        set.Add(role);
                    }
        }
        finally
        {
            foreach (var mod in mods) mod.Dispose();
        }

        return roles.ToDictionary(r => r.Key, r => (IReadOnlySet<string>)r.Value, StringComparer.OrdinalIgnoreCase);
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
