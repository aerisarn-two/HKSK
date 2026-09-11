using HKSK.Havok;
using HkFbx = HKFBX.Model;

namespace HKSK.Fbx;

/// <summary>
/// The skeleton a paired animation is rigged to, rebuilt.
/// </summary>
/// <remarks>
/// No packfile in the game declares <c>PairedRoot</c> -- the string appears in
/// the paired animations and in none of the other 7,699 Havok files -- so the
/// combined skeleton has to be built rather than loaded.
///
/// What builds it is the animation itself. Its annotation tracks name every bone
/// in the order the binding expects, so that order is the bone order and
/// track-to-bone is the identity. The two participants' rigs then supply what the
/// animation does not carry: the hierarchy, and the reference pose.
///
/// A rig is allowed to be missing, and to be incomplete. Exporting a killmove
/// from the bear's project when nothing has said who the other actor is still has
/// to produce every track, so a bone no rig accounts for is created under its own
/// half's root with an identity pose. That happens in the shipped game as well as
/// in a mod: 18 of the 296 paired animations name more bones on one side than
/// that actor's rig declares -- the draugr's half is 129 bones against a rig of
/// 84 -- because a rig is not the whole skeleton.
/// </remarks>
public static class PairedRig
{
    /// <summary>
    /// Builds the combined skeleton for a paired animation.
    /// </summary>
    /// <param name="paired">The animation, which decides the bone order.</param>
    /// <param name="driverRig">
    /// The rig of the actor whose bones are unprefixed, or null to synthesise them.
    /// </param>
    /// <param name="partnerRig">
    /// The rig of the actor whose bones carry <c>2_</c>, or null to synthesise them.
    /// </param>
    public static HkFbx.Skeleton Build(
        PairedAnimation paired,
        HkFbx.Skeleton? driverRig,
        HkFbx.Skeleton? partnerRig)
    {
        ArgumentNullException.ThrowIfNull(paired);

        if (!paired.IsCombined)
            throw new ArgumentException(
                $"'{paired.Path}' has no partner half, so it has no combined skeleton",
                nameof(paired));

        var names = paired.Tracks;
        var indexByName = new Dictionary<string, int>(names.Count, StringComparer.OrdinalIgnoreCase);

        for (int bone = 0; bone < names.Count; bone++)
            indexByName.TryAdd(names[bone], bone);

        // The file's own root is its first track, and which bone that is depends on
        // the layout. The ordinary one starts at PairedRoot with both actors under
        // it; the first-person killmoves start at NPC -- the viewer -- and carry
        // PairedRoot inside, with the partner below that. Following track 0 rather
        // than looking for a name reproduces both, and a hierarchy that is wrong
        // moves every bone, because the transforms are local to it.
        int treeRoot = names.Count > 0 ? 0 : -1;

        int pairRoot = indexByName.GetValueOrDefault(PairedAnimation.RootBone, treeRoot);
        int driverRoot = indexByName.GetValueOrDefault(PairedAnimation.DriverRootBone, pairRoot);

        // Three of the game's first-person pairings place the partner's root bone
        // and no subtree root above it, so this can be absent.
        int partnerRoot = indexByName.GetValueOrDefault(PairedAnimation.PartnerPrefix, pairRoot);

        var driver = Lookup(driverRig);
        var partner = Lookup(partnerRig);

        var bones = new HkFbx.Bone[names.Count];

        for (int bone = 0; bone < names.Count; bone++)
        {
            string name = names[bone];
            bool isPartner = PairedAnimation.IsPartnerTrack(name) || name == PairedAnimation.PartnerPrefix;

            int fallback = isPartner ? partnerRoot : driverRoot;

            // A subtree root hangs from the pair's root, and the pair's root from
            // whatever the file put above it. Only track 0 has nothing above it.
            if (bone == driverRoot || bone == partnerRoot) fallback = pairRoot;
            if (bone == pairRoot) fallback = treeRoot;
            if (bone == treeRoot) fallback = -1;

            var rig = isPartner ? partner : driver;
            string inRig = isPartner ? PairedAnimation.WithoutPrefix(name) : name;

            HkFbx.BoneTransform pose = HkFbx.BoneTransform.Identity;
            int parent = fallback;

            if (rig is { } known && known.Index.TryGetValue(inRig, out int source))
            {
                pose = known.Skeleton.Bones[source].ReferencePose;

                int sourceParent = known.Skeleton.Bones[source].ParentIndex;

                if (sourceParent >= 0)
                {
                    string parentName = known.Skeleton.Bones[sourceParent].Name;
                    string asTrack = isPartner ? PairedAnimation.PartnerPrefix + parentName : parentName;

                    // A rig parent the animation does not drive cannot be a parent
                    // here: the skeleton holds exactly the tracks, no more.
                    if (indexByName.TryGetValue(asTrack, out int mapped) && mapped != bone)
                        parent = mapped;
                }
            }

            if (parent == bone) parent = fallback;

            bones[bone] = new HkFbx.Bone(name, parent, pose);
        }

        return new HkFbx.Skeleton
        {
            Name = PairedAnimation.RootBone,
            Bones = bones,
        };
    }

    /// <summary>
    /// Which half of the animation a project's rig accounts for.
    /// </summary>
    /// <remarks>
    /// The folder a killmove sits in does not say: <c>SharedKillMoves\Human&amp;Falmer</c>
    /// holds one whose unprefixed half matches the draugr's rig, because the
    /// humanoids share their bone naming and the folder is a filing convention.
    /// Overlap decides it instead, and it is decisive in practice -- a rig either
    /// covers nearly all of one half or almost none of it.
    /// </remarks>
    public static bool DriverIs(PairedAnimation paired, HkFbx.Skeleton? rig)
    {
        ArgumentNullException.ThrowIfNull(paired);

        if (rig is null) return true;

        var bones = rig.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int driver = paired.DriverBones.Count(bones.Contains);
        int partner = paired.PartnerBones.Count(bones.Contains);

        return driver >= partner;
    }

    private static (HkFbx.Skeleton Skeleton, Dictionary<string, int> Index)? Lookup(HkFbx.Skeleton? rig)
    {
        if (rig is null) return null;

        var index = new Dictionary<string, int>(rig.Bones.Count, StringComparer.OrdinalIgnoreCase);

        for (int bone = 0; bone < rig.Bones.Count; bone++)
            index.TryAdd(rig.Bones[bone].Name, bone);

        return (rig, index);
    }
}
