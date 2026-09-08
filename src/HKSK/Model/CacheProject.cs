using HKSK.Cache;
using HKSK.Validation;

namespace HKSK.Model;

/// <summary>
/// A project the animation cache lists, of either kind.
/// </summary>
/// <remarks>
/// Skyrim's cache holds two quite different things under one grammar, and the
/// shipped data separates them absolutely: a project carries a clip cache if and
/// only if it has animation set data. All 49 that do are actors; the other 380
/// are doors, windmills and pillars.
///
/// The difference is not a degree. An <see cref="ActorProject"/> has animations
/// numbered by a character file, clips over them, root motion and attack sets. A
/// <see cref="PropProject"/> has a list of files and nothing else -- no clips, no
/// motion, no sets, in any of the 380 -- so almost nothing an actor can be asked
/// is meaningful to ask of one.
///
/// Hence two types rather than one with everything nullable: what you can do
/// with a project is decided when it is opened, not discovered when a call
/// throws.
/// </remarks>
public abstract class CacheProject
{
    private protected CacheProject(AnimationDataProject data) => Data = data;

    /// <summary>The project's entry in the animation data.</summary>
    public AnimationDataProject Data { get; }

    /// <summary>The project stem, e.g. <c>ChickenProject</c>.</summary>
    public string Name => Data.Stem;

    /// <summary>
    /// The Havok files the project is made of, as stored: Windows-separated and
    /// relative to the folder holding the project .hkx.
    /// </summary>
    public IReadOnlyList<string> Files => Data.Block.Files;

    /// <summary>What is wrong with this project, if anything.</summary>
    public IReadOnlyList<Finding> Validate() => ConsistencyReport.Check(this);

    public override string ToString() => $"{GetType().Name} {Name}";
}

/// <summary>
/// A project with no animation cache: a door, a windmill, a puzzle pillar.
/// </summary>
/// <remarks>
/// 380 of the game's 429 projects. It carries a file list -- every one of them
/// does -- naming a behaviour, a character and a skeleton, and that is the whole
/// of it: none has a single clip or a single movement block.
///
/// The files are usually generic, <c>Behaviors\Behavior00.hkx</c> and
/// <c>Characters\Character00.hkx</c>, and they do not live under
/// <c>actors/</c> the way an actor's do -- they are scattered through
/// <c>animbehaviors/</c>, <c>architecture/</c> and <c>clutter/</c> beside the
/// thing they animate.
/// </remarks>
public sealed class PropProject : CacheProject
{
    internal PropProject(AnimationDataProject data) : base(data) { }
}
