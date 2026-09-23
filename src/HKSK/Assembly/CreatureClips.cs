using HKFBX.Fbx;
using HKFBX.Model;
using HKSK.Havok;
using LeanMeshIO;

namespace HKSK.Assembly;

/// <summary>One animation, written and measured.</summary>
/// <param name="Name">The take it came from, which is what the clip is called.</param>
/// <param name="Path">The animation file written for it.</param>
/// <param name="Frames">How many frames it holds.</param>
/// <param name="Duration">Seconds it runs for.</param>
/// <param name="Travel">
/// How fast the clip means to go, read off its feet, or null where it could not be read --
/// a clip with nothing on the ground says so rather than answering wrongly.
/// </param>
public sealed record WrittenClip(
    string Name,
    string Path,
    int Frames,
    float Duration,
    InferredMotion? Travel)
{
    public override string ToString() =>
        $"{Name}: {Frames} frames, {Duration:0.##}s" +
        (Travel is null ? ", standing still" : $", {Travel}");
}

/// <summary>What converting an FBX's takes produced.</summary>
/// <param name="Clips">One per take, in the order they were asked for.</param>
/// <param name="Bones">How many bones the clips drive.</param>
/// <param name="Unmatched">
/// The clips' bones that are not in the creature's rig, whose tracks drive nothing. A handful
/// is ordinary -- an exporter's IK effectors have no counterpart -- and a great many means
/// the clips are over a different creature.
/// </param>
public sealed record WrittenClips(IReadOnlyList<WrittenClip> Clips, int Bones, IReadOnlyList<string> Unmatched)
{
    public override string ToString() =>
        $"{Clips.Count} clips over {Bones} bones" +
        (Unmatched.Count == 0 ? "" : $", {Unmatched.Count} driving nothing");
}

/// <summary>
/// Turns an FBX's takes into the animation files a creature's project refers to.
/// </summary>
/// <remarks>
/// <para>
/// The travel is measured here and written nowhere near the clip, which is the part worth
/// stating plainly. Skyrim does not carry an animation's travel in the animation: of twelve
/// hundred sampled from the game, all but four have no extracted motion at all, and the root
/// track of a run that covers two hundred and fifty units sits at the origin for every frame.
/// The travel is in the cache's movement block and the engine applies it, which is why root
/// motion belongs to a <em>slot</em> and can be authored for a clip that never had any.
/// </para>
/// <para>
/// So each clip is written with its root standing still, exactly as the game's own are, and
/// what the feet say about how fast it means to go is handed back for the cache row to carry.
/// A bought clip that walks on the spot is not a broken animation; it is an animation whose
/// travel has not been written down yet.
/// </para>
/// <para>
/// The one thing that has to be got right in the file itself is which bone each track drives.
/// A clip arrives over the rig as its author built it, and is written over a rig that has
/// since been given a root at the origin and a bone to carry the lift, so every track drives
/// a bone two along from where it thinks. Nothing catches that if it is wrong: every index is
/// valid, and the creature simply moves the wrong parts.
/// </para>
/// </remarks>
public static class CreatureClips
{
    /// <summary>The bones a biped stands on, which are what its travel is read from.</summary>
    /// <remarks>The toes read better than the ankles on a rig that has both.</remarks>
    public static readonly string[] Feet = ["NPC L Toe0 [LToe]", "NPC R Toe0 [RToe]"];

    /// <summary>Writes every take in <paramref name="fbxPath"/> as an animation.</summary>
    /// <param name="fbxPath">An FBX whose takes are the clips, one per animation.</param>
    /// <param name="templatePath">A shipped animation to write into, for the shape of the file.</param>
    /// <param name="outputFolder">Where the animations go.</param>
    /// <param name="rig">
    /// The rig the animations will be played over: the grounded, renamed one the creature's
    /// skeleton file holds, not the one the FBX carries.
    /// </param>
    /// <param name="names">
    /// What to call the clips' bones so they line up with the creature's rig, or null to read
    /// the biped naming off them as the skeleton route does. The clips arrive under the names
    /// their exporter used, and the creature's rig has since been put under the game's.
    /// </param>
    /// <param name="takes">Which takes to write, or null for all of them.</param>
    /// <param name="feet">The bones to read the travel from, or the biped's two.</param>
    /// <param name="turnDegrees">
    /// How far the creature's rig was turned to face the way the game faces, so the travel is
    /// reported in the game's frame rather than the exporter's. A clip read over a rig built
    /// facing backwards comes back travelling at 180 degrees, which is forwards for that rig
    /// and backwards for the game; the same turn that fixed the rig fixes the reading.
    /// </param>
    public static WrittenClips Write(
        string fbxPath,
        string templatePath,
        string outputFolder,
        Skeleton rig,
        IReadOnlyDictionary<string, string>? names = null,
        IReadOnlyList<string>? takes = null,
        IReadOnlyList<string>? feet = null,
        float turnDegrees = 0f)
    {
        ArgumentException.ThrowIfNullOrEmpty(fbxPath);
        ArgumentException.ThrowIfNullOrEmpty(templatePath);
        ArgumentException.ThrowIfNullOrEmpty(outputFolder);
        ArgumentNullException.ThrowIfNull(rig);

        FbxDocument document = FbxDocument.Load(fbxPath);
        Skeleton exported = FbxAnimationReader.ReadSkeleton(document);

        // The clips arrive under their exporter's names and the creature's rig is under the
        // game's, so they are put under the same names before anything is matched. Matching
        // by name rather than by position is what makes this survive a rig whose bones were
        // reordered, or one that grew a root and a lift after the clips were made.
        Skeleton theirs = SkeletonNaming.Rename(exported, names ?? SkeletonNaming.BipedMap(exported)).Rig;

        var ourIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rig.Count; i++) ourIndex.TryAdd(rig.Bones[i].Name, i);

        var unmatched = new List<string>();
        var bone = new short[theirs.Count];
        for (int i = 0; i < theirs.Count; i++)
        {
            if (ourIndex.TryGetValue(theirs.Bones[i].Name, out int mine)) bone[i] = (short)mine;
            else { bone[i] = -1; unmatched.Add(theirs.Bones[i].Name); }
        }

        if (unmatched.Count == theirs.Count)
            throw new InvalidDataException(
                $"not one of the clips' {theirs.Count} bones is in the creature's rig: " +
                "the clips are over a different creature, or under names it does not use");

        Directory.CreateDirectory(outputFolder);

        var wanted = takes ?? FbxAnimationReader.ReadTakeNames(document);
        var written = new List<WrittenClip>(wanted.Count);

        foreach (string take in wanted)
        {
            // Read under the names the FBX uses, since that is how its curves are found: a
            // clip read over the renamed rig matches no curve at all and comes back as the
            // rest pose on every frame, which looks like an animation that stands still.
            SampledAnimation clip = FbxAnimationReader.ReadAnimation(document, exported, takeName: take);

            // A track whose bone the creature has not got is left pointing at the root, which
            // every rig has and no clip drives: dropping the track instead would renumber the
            // rest, and every index is valid so nothing would report it.
            SampledAnimation bound = clip with
            {
                TrackToBone =
                [
                    .. Enumerable.Range(0, clip.TrackCount).Select(t =>
                    {
                        int at = clip.BoneForTrack(t);
                        return at >= 0 && at < bone.Length && bone[at] >= 0 ? bone[at] : (short)0;
                    }),
                ],
            };

            string path = Path.Combine(outputFolder, take + ".hkx");
            UncompressedAnimation.Write(templatePath, bound, path);

            // Measured over the renamed rig, which is the same rig in the same order and is
            // the one whose feet are called what the caller asked for.
            InferredMotion? measured = FootMotion.Infer(clip, theirs, Named(theirs, feet ?? Feet));
            InferredMotion? travel = measured is null || turnDegrees == 0f
                ? measured
                : measured with { Heading = Wrap(measured.Heading + turnDegrees) };

            written.Add(new WrittenClip(take, path, clip.FrameCount, clip.Duration, travel));
        }

        return new WrittenClips(written, theirs.Count, unmatched);
    }

    /// <summary>An angle brought back into the half turn either side of straight ahead.</summary>
    private static float Wrap(float degrees)
    {
        float at = degrees % 360f;
        if (at > 180f) at -= 360f;
        if (at < -180f) at += 360f;
        return at;
    }

    /// <summary>The feet a rig actually has, since a name it has not got reads as nothing.</summary>
    private static IReadOnlyList<string> Named(Skeleton rig, IReadOnlyList<string> wanted)
    {
        var have = rig.Bones.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. wanted.Where(have.Contains)];
    }
}
