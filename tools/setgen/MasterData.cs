using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace HKSK.SetGen;

/// <summary>
/// What the set data needs from the game's masters: which events can be sent, which of
/// them equip a weapon, and which attack events each project's races use.
/// </summary>
/// <remarks>
/// HKSK reads Havok files and the animation cache and does not open plugins, so this is
/// the caller's half. The masters are opened directly and in order, later records
/// overriding earlier, as <c>speedgen</c> does.
/// </remarks>
public static class MasterData
{
    public static readonly string[] Order =
        ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

    public static HKSK.SetData.GameEvents Events(string dataFolder)
    {
        var idles = new Dictionary<FormKey, IIdleAnimationGetter>();
        var races = new Dictionary<FormKey, IRaceGetter>();
        var mods = new List<ISkyrimModDisposableGetter>();

        try
        {
            foreach (string master in Order)
            {
                string path = Path.Combine(dataFolder, master);
                if (!File.Exists(path)) continue;

                var mod = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
                mods.Add(mod);

                foreach (IIdleAnimationGetter record in mod.IdleAnimations) idles[record.FormKey] = record;
                foreach (IRaceGetter race in mod.Races) races[race.FormKey] = race;
            }

            var idle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var equip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IIdleAnimationGetter record in idles.Values)
            {
                if (string.IsNullOrEmpty(record.AnimationEvent)) continue;
                idle.Add(record.AnimationEvent);

                // The equip path resolves Action Draw and Action Force Equip through the
                // idle tree; the idles it can land on sit under the roots named for
                // drawing and equipping (DrawSheathRoot, ForceEquipRoot, WerewolfDrawRoot).
                if (RootOf(record, idles).EditorID is { } root &&
                    (root.Contains("Draw", StringComparison.OrdinalIgnoreCase) || root.Contains("Equip", StringComparison.OrdinalIgnoreCase)))
                    equip.Add(record.AnimationEvent);
            }

            var attacks = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (IRaceGetter race in races.Values)
                foreach (string? graph in new[] { race.BehaviorGraph.Male?.File.DataRelativePath.Path, race.BehaviorGraph.Female?.File.DataRelativePath.Path })
                {
                    if (string.IsNullOrEmpty(graph)) continue;

                    string stem = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));
                    if (!attacks.TryGetValue(stem, out var events)) attacks[stem] = events = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var attack in race.Attacks)
                        if (!string.IsNullOrEmpty(attack.AttackEvent)) events.Add(attack.AttackEvent);
                }

            return new HKSK.SetData.GameEvents(
                idle, equip,
                attacks.ToDictionary(a => a.Key, a => (IReadOnlySet<string>)a.Value, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var mod in mods) mod.Dispose();
        }
    }

    private static IIdleAnimationGetter RootOf(IIdleAnimationGetter idle, Dictionary<FormKey, IIdleAnimationGetter> idles)
    {
        var seen = new HashSet<FormKey>();
        IIdleAnimationGetter at = idle;

        while (seen.Add(at.FormKey) && at.RelatedIdles.FirstOrDefault() is { IsNull: false } parent
               && idles.TryGetValue(parent.FormKey, out var next))
            at = next;

        return at;
    }
}
