using System.Numerics;
using HKFBX.Fbx;
using LeanMeshIO;
using HKFBX.Model;
using HKX2;

namespace HKSK.Assembly;

/// <summary>A skeleton packfile built for a creature that came with none.</summary>
/// <param name="Path">Where it was written.</param>
/// <param name="Grounded">What standing the rig on the floor took, or null if it was not asked for.</param>
/// <param name="Renamed">What putting it under the game's names changed, and what it left alone.</param>
/// <param name="Ragdoll">What fitting the prototype's ragdoll to it produced.</param>
public sealed record AssembledSkeleton(
    string Path,
    GroundedSkeleton? Grounded,
    RenamedSkeleton Renamed,
    RetargetedRagdoll Ragdoll)
{
    public override string ToString() =>
        $"{Renamed.Rig.Count} bones, {Ragdoll}" +
        (Grounded is null ? "" : $", lifted {Grounded.Lift:0.#}");
}

/// <summary>
/// Builds a creature's <c>skeleton.hkx</c> from a rig that arrived in an FBX.
/// </summary>
/// <remarks>
/// <para>
/// An asset bought off a marketplace is a rig and some clips, and Skyrim wants rather more
/// than that. Three things are missing from every such asset and none of them can be got from
/// the FBX, because none of them was ever in it:
/// </para>
/// <list type="number">
/// <item>
/// it stands in the wrong place. A creature is authored on the origin with its lowest bone on
/// the floor, and a biped is built around its pelvis, so it hangs below the ground by the
/// length of its legs;
/// </item>
/// <item>
/// its bones are under an exporter's names, and everything in Skyrim that looks for a bone --
/// a ragdoll, a piece of armour, a magic effect, a behaviour graph -- looks for it by the
/// game's;
/// </item>
/// <item>
/// it has no ragdoll, and one cannot be derived: the joint limits are authored, and they are
/// the difference between a corpse that falls and one that turns itself inside out.
/// </item>
/// </list>
/// <para>
/// So the three are done in that order, each by the piece of the library that knows about it,
/// and this is only the order. Grounding first, because the lift has to be in place before
/// anything measures the creature; renaming second, because the ragdoll is matched by name;
/// the ragdoll last, onto a rig that is finally in the shape the game expects.
/// </para>
/// </remarks>
public static class CreatureSkeleton
{
    /// <summary>The name a Havok skeleton carries, which is its root bone's.</summary>
    public const string RigName = "NPC Root [Root]";

    /// <summary>Builds <paramref name="outputPath"/> from the rig in <paramref name="fbxPath"/>.</summary>
    /// <param name="fbxPath">An FBX holding the rig. Its animations, if any, are not read.</param>
    /// <param name="prototypePath">
    /// The shipped skeleton packfile to take a ragdoll from. Choose one of the same kind of
    /// creature: a bone skeleton's joints are hinged where a fleshed one's are not.
    /// </param>
    /// <param name="outputPath">Where to write the packfile.</param>
    /// <param name="names">
    /// What to call the rig's bones, or null to read the 3ds Max biped naming off the rig.
    /// </param>
    /// <param name="ground">
    /// Whether to stand the rig on the floor first. False for a rig already authored there.
    /// </param>
    public static AssembledSkeleton Build(
        string fbxPath,
        string prototypePath,
        string outputPath,
        IReadOnlyDictionary<string, string>? names = null,
        bool ground = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(fbxPath);
        ArgumentException.ThrowIfNullOrEmpty(prototypePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        Skeleton rig = FbxAnimationReader.ReadSkeleton(FbxDocument.Load(fbxPath));

        GroundedSkeleton? grounded = ground ? SkeletonGrounding.Ground(rig) : null;
        if (grounded is not null) rig = grounded.Rig;

        RenamedSkeleton renamed = SkeletonNaming.Rename(rig, names ?? SkeletonNaming.BipedMap(rig));
        RetargetedRagdoll fitted = RagdollRetarget.Fit(prototypePath, Havok(renamed.Rig), RigName);

        string? folder = Path.GetDirectoryName(outputPath);
        if (folder is { Length: > 0 }) Directory.CreateDirectory(folder);
        fitted.File.Save(outputPath);

        return new AssembledSkeleton(outputPath, grounded, renamed, fitted);
    }

    /// <summary>The same rig, as Havok keeps one.</summary>
    /// <remarks>
    /// Havok wants a parent to come before its children, which the FBX reader already
    /// guarantees by walking the hierarchy depth-first rather than taking file order.
    /// </remarks>
    public static hkaSkeleton Havok(Skeleton rig, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(rig);

        return new hkaSkeleton
        {
            m_name = name ?? (rig.Count > 0 ? rig.Bones[0].Name : rig.Name),
            m_bones = [.. rig.Bones.Select(b => new hkaBone { m_name = b.Name, m_lockTranslation = false })],
            m_parentIndices = [.. rig.Bones.Select(b => (short)b.ParentIndex)],
            m_referencePose = [.. rig.Bones.Select(b => Qs(b.ReferencePose))],
            m_referenceFloats = [],
            m_floatSlots = [],
            m_localFrames = [],
        };
    }

    /// <summary>
    /// A bone's rest pose, in the shape HKX2 keeps an hkQsTransform: the translation in the
    /// first row, the rotation quaternion in the second, the scale in the third. It is a
    /// container rather than a matrix, and is never multiplied as one.
    /// </summary>
    private static Matrix4x4 Qs(BoneTransform t) => new(
        t.Translation.X, t.Translation.Y, t.Translation.Z, 0f,
        t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W,
        t.Scale.X, t.Scale.Y, t.Scale.Z, 0f,
        0f, 0f, 0f, 0f);
}
