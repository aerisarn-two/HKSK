namespace HKSK.Model;

/// <summary>
/// Where a project named by the animation cache keeps its files.
/// </summary>
/// <param name="Name">
/// The project as the cache spells it, e.g. <c>DefaultMale</c>, <c>HMDaedra</c>.
/// </param>
/// <param name="ProjectFile">
/// The full path to <c>&lt;name&gt;.hkx</c>, or null when the meshes tree has no
/// such packfile.
/// </param>
/// <param name="Folder">
/// The directory holding it, which is what every relative path inside the project
/// resolves against. Null when the project was not found.
/// </param>
public readonly record struct ProjectLocation(string Name, string? ProjectFile, string? Folder)
{
    /// <summary>Whether the project was found on disk.</summary>
    public bool Found => ProjectFile is not null;

    public override string ToString() => $"{Name} -> {Folder ?? "(not found)"}";
}
