using HKSK.Havok;

namespace HKSK.Behavior;

/// <summary>
/// The chain from a project packfile to the one behaviour graph it entered at.
/// </summary>
/// <param name="ProjectFile">The <c>&lt;name&gt;.hkx</c> this started from.</param>
/// <param name="CharacterFile">The character packfile, resolved on disk.</param>
/// <param name="BehaviorFile">The entry behaviour packfile, resolved on disk.</param>
/// <param name="StoredCharacterName">
/// The character as the project file spells it, e.g. <c>Characters\DefaultMale.hkx</c>.
/// </param>
/// <param name="StoredBehaviorName">
/// The behaviour as the character file spells it, e.g. <c>Behaviors\0_Master.hkx</c>.
/// </param>
public readonly record struct BehaviorRoot(
    string ProjectFile,
    string CharacterFile,
    string BehaviorFile,
    string StoredCharacterName,
    string StoredBehaviorName)
{
    /// <summary>
    /// Follows a project to its root behaviour graph, or null if the chain breaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The root behaviour is the only file addressed from the character
    /// file, which is the only file addressed from the project file.</strong> Both
    /// links are exactly one wide in all 49 shipped projects, and there is no
    /// second route: <c>hkbProjectStringData.m_behaviorFilenames</c> exists and is
    /// <em>empty</em> in every one of them, so a project never names a behaviour
    /// itself.
    /// </para>
    /// <para>
    /// That matters because the animation cache also lists behaviour files -- 13
    /// projects list more than one -- and those are the referenced graphs, not the
    /// entry point. Picking from that list is guessing; this is reading. The
    /// shortcuts both happen to work on the vanilla files and neither is sound:
    /// the root is the cache's first behaviour in all 49, and it is named after
    /// the project in only 3 (the player's two and the first-person rig's are all
    /// <c>0_Master</c>).
    /// </para>
    /// <para>
    /// Everything is resolved against the project packfile's own folder, which is
    /// what the stored relative paths are written against. Their spelling is not
    /// uniform -- the wolf's is <c>Behaviors Wolf\</c>, the chicken's character is
    /// <c>chickencharater.hkx</c>, misspelt in the shipped game -- so the paths
    /// are followed as stored rather than reconstructed from a convention.
    /// </para>
    /// </remarks>
    /// <param name="projectHkx">Path to a project packfile.</param>
    public static BehaviorRoot? Of(string projectHkx)
    {
        if (!File.Exists(projectHkx)) return null;

        Havok.ProjectFile project = Havok.ProjectFile.Load(projectHkx);
        string folder = project.Folder;

        // one character file, named by the project and nothing else
        if (project.CharacterFiles.Count != 1) return null;

        string storedCharacter = project.CharacterFiles[0];
        if (HavokPath.Resolve(folder, storedCharacter) is not { } characterPath) return null;

        // one behaviour file, named by the character and nothing else
        string storedBehavior = Havok.CharacterFile.Load(characterPath).BehaviorFilename;
        if (storedBehavior.Length == 0) return null;
        if (HavokPath.Resolve(folder, storedBehavior) is not { } behaviorPath) return null;

        return new BehaviorRoot(
            projectHkx, characterPath, behaviorPath, storedCharacter, storedBehavior);
    }

    /// <summary>The entry graph's file name without folder or extension, e.g. <c>0_master</c>.</summary>
    public string Name => Path.GetFileNameWithoutExtension(BehaviorFile);

    public override string ToString() =>
        $"{Path.GetFileNameWithoutExtension(ProjectFile)} -> {Path.GetFileName(CharacterFile)} -> {Path.GetFileName(BehaviorFile)}";
}
