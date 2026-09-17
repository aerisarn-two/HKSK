using Mutagen.Bethesda.Skyrim;
using MovementType = HKSK.Speed.MovementType;

namespace HKSK.SpeedGen;

/// <summary>
/// What the speed table needs from the game's masters: the movement types, and what
/// the races do with them.
/// </summary>
/// <remarks>
/// HKSK reads Havok files and the animation cache and does not open plugins, so this
/// is the caller's half. The five shipped masters are opened directly and in order --
/// <c>GameEnvironment.Typical</c> wants the <c>plugins.txt</c> a launcher writes, which
/// a machine that never ran the game does not have -- later ones overriding earlier,
/// which is all a load order does for records nothing else touches.
/// </remarks>
public static class MasterData
{
    /// <summary>The masters, in load order.</summary>
    public static readonly string[] Order =
        ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    /// <summary>Every movement type the masters define, by name.</summary>
    public static IReadOnlyDictionary<string, MovementType> MovementTypes(string dataFolder)
    {
        var types = new Dictionary<string, MovementType>(StringComparer.OrdinalIgnoreCase);

        foreach (string master in Order)
        {
            string path = Path.Combine(dataFolder, master);
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
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> RaceRoles(string dataFolder)
    {
        var names = new Dictionary<Mutagen.Bethesda.Plugins.FormKey, string>();
        var roles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<ISkyrimModDisposableGetter>();

        try
        {
            foreach (string master in Order)
            {
                string path = Path.Combine(dataFolder, master);
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
