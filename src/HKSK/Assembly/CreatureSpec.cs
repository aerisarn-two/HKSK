namespace HKSK.Assembly;

/// <summary>What a creature is to be assembled from.</summary>
/// <param name="Name">
/// The project's stem: <c>MyBeast</c> gives <c>MyBeastProject.hkx</c>,
/// <c>characters\MyBeast.hkx</c> and <c>behaviors\MyBeastBehavior.hkx</c>.
/// </param>
/// <param name="SkeletonPath">
/// The rig packfile every animation was authored against, copied into the project.
/// </param>
/// <param name="Animations">
/// The animations with their roles, <b>in the order they are to be numbered</b>. That
/// order is every clip's cache index, so it is appended to and never inserted into.
/// </param>
/// <param name="RagdollPath">
/// The ragdoll packfile, where there is one. Null names the skeleton for both, which
/// is what the game does for the creatures whose rig carries its own ragdoll.
/// </param>
/// <param name="MovementTypeName">
/// What the creature's movement type is called, which the graph declares as
/// <c>iState_&lt;name&gt;</c> and the speed table is keyed on.
/// </param>
/// <param name="ClipDurations">
/// How long each animation runs for, by its stem, where the assembler cannot read it.
/// A duration is what turns a speed into a travel, so a clip with a speed and no
/// duration cannot be given root motion.
/// </param>
/// <param name="TurnRate">
/// Degrees a second for a turn in place that has to be made rather than given. Null
/// reads one off the creature's height.
/// </param>
public sealed record CreatureSpec(
    string Name,
    string SkeletonPath,
    IReadOnlyList<RoledAnimation> Animations,
    string? RagdollPath = null,
    string MovementTypeName = "Default",
    IReadOnlyDictionary<string, float>? ClipDurations = null,
    float? TurnRate = null);

/// <summary>What was assembled.</summary>
/// <param name="ProjectPath">The project packfile, which is what a race names.</param>
/// <param name="Plan">What the animations were read as.</param>
/// <param name="Files">Everything written, relative to the output folder.</param>
/// <param name="Cache">
/// The creature's row for the animation cache: its file list, its clips and their
/// numbering, and the root motion of every slot that has any. A caller merges this
/// into the game's <c>animationdatasinglefile.txt</c>.
/// </param>
/// <param name="Movement">
/// The movement type the creature's clips describe, for the <c>MOVT</c> record a caller
/// then writes. Its eight speeds are the clips', because the record is authored from
/// them and not the other way round.
/// </param>
/// <param name="Notes">What was decided along the way.</param>
public sealed record AssemblyResult(
    string ProjectPath,
    CreaturePlan Plan,
    IReadOnlyList<string> Files,
    HKSK.Cache.AnimationDataProject Cache,
    HKSK.Speed.MovementType Movement,
    IReadOnlyList<string> Notes);
