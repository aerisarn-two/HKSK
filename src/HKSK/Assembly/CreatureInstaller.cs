using HKSK.Cache;
using HKSK.Model;
using HKSK.Speed;

namespace HKSK.Assembly;

/// <summary>What installing a creature into a Meshes folder changed.</summary>
/// <param name="MeshesFolder">Where the three cache files were written.</param>
/// <param name="Added">Whether the creature was new to the cache rather than rebuilt in it.</param>
/// <param name="SpeedTable">
/// Whether a speed block was written for it, which needs a sampler in its graph and a
/// movement type with somewhere to go.
/// </param>
/// <param name="Notes">What was decided.</param>
public sealed record InstallReport(string MeshesFolder, bool Added, bool SpeedTable, IReadOnlyList<string> Notes);

/// <summary>
/// Puts an assembled creature into the caches the game reads creatures out of.
/// </summary>
/// <remarks>
/// <para>
/// A creature's packfiles are not enough on their own. The game finds a project by name
/// in <c>animationdatasinglefile.txt</c> and takes the first behaviour in its file list,
/// so a creature whose loose files are perfect and whose cache row is missing reports
/// that its root behaviour cannot be found.
/// </para>
/// <para>
/// The speed table is the part that has to come last, because it is measured rather
/// than written. It is built by sampling the creature's own ladder at every speed the
/// engine may ask for, so it needs the graph, the movement type and the root motion --
/// all three of which exist only once the creature has been assembled and its row is in
/// the cache.
/// </para>
/// </remarks>
public static class CreatureInstaller
{
    /// <summary>
    /// Adds the creature to the caches under <paramref name="meshesFolder"/>, building
    /// its speed table, and writes all three files back.
    /// </summary>
    /// <param name="made">What <see cref="CreatureAssembler.Assemble"/> returned.</param>
    /// <param name="meshesFolder">
    /// The <c>Meshes</c> folder the creature's project sits under. The project's own
    /// files must already be there, which they are when it was assembled into it.
    /// </param>
    public static InstallReport Install(AssemblyResult made, string meshesFolder)
    {
        ArgumentNullException.ThrowIfNull(made);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshesFolder);

        var notes = new List<string>();
        SkyrimCache cache = Existing(meshesFolder)
            ?? SkyrimCache.FromParts(new AnimationDataFile(), new AnimationSetDataFile());

        string stem = Path.GetFileNameWithoutExtension(made.Cache.Name);
        int at = cache.AnimationData.Projects.FindIndex(p =>
            string.Equals(p.Stem, stem, StringComparison.OrdinalIgnoreCase));

        bool added = at < 0;
        if (added) cache.AnimationData.Projects.Add(made.Cache);
        else cache.AnimationData.Projects[at] = made.Cache;

        notes.Add(added
            ? $"'{stem}' was added to the cache, which now lists {cache.AnimationData.Projects.Count} projects"
            : $"'{stem}' was already in the cache and its row was rebuilt");

        // Written and read back, so that the generator resolves the project's Havok
        // files the way the game does: by looking them up under the meshes folder.
        cache.Save(meshesFolder);
        cache = SkyrimCache.Load(meshesFolder);

        bool table = false;
        try
        {
            HKSK.Model.Amendment amendment = SpeedDataGenerator.Amend(
                cache, stem, new Dictionary<string, MovementType>(StringComparer.OrdinalIgnoreCase)
                {
                    [made.Movement.Name] = made.Movement,
                });

            table = amendment != HKSK.Model.Amendment.Unchanged;
            notes.Add($"the speed table: {amendment}");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            notes.Add($"no speed table: {e.Message}");
        }

        cache.Save(meshesFolder);
        return new InstallReport(meshesFolder, added, table, notes);
    }

    private static SkyrimCache? Existing(string meshesFolder) =>
        File.Exists(Path.Combine(meshesFolder, SkyrimCache.AnimationDataFileName))
            ? SkyrimCache.Load(meshesFolder)
            : null;
}
