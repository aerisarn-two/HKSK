using System.Numerics;
using HKSK.Havok;
using HKX2;

namespace HKSK.Assembly;

/// <summary>What moving a ragdoll onto another rig did, and what it could not take with it.</summary>
/// <param name="File">The skeleton packfile: the new rig, the ragdoll fitted to it, and the mappings.</param>
/// <param name="Bodies">How many ragdoll bones came across.</param>
/// <param name="Dropped">
/// The prototype's ragdoll bones the new rig has not got, which are left behind along with
/// the constraints that held them. A skeleton with no jaw loses the jaw, and nothing else.
/// </param>
/// <param name="Scale">
/// What the new creature measures against the prototype, as a ratio. The capsules are
/// sized by it, so a value far from 1 is worth reading twice.
/// </param>
public sealed record RetargetedRagdoll(
    HavokFile File,
    int Bodies,
    IReadOnlyList<string> Dropped,
    float Scale)
{
    public override string ToString() =>
        $"{Bodies} bodies at {Scale:0.###} of the prototype's size" +
        (Dropped.Count == 0 ? "" : $", without {string.Join(", ", Dropped)}");
}

/// <summary>
/// Fits a shipped creature's ragdoll to a rig of one's own.
/// </summary>
/// <remarks>
/// <para>
/// A ragdoll is not derivable from a rig. The bones say where the joints are, but nothing in
/// them says how far a knee may bend, which way an elbow is hinged, or how much of a shoulder's
/// twist to allow, and those limits are the whole difference between a body that falls and one
/// that turns itself inside out. They have to come from a creature that already has them.
/// </para>
/// <para>
/// What a rig <em>does</em> settle is where everything goes, and the game's own files say so
/// plainly: in every skeleton packfile measured, a ragdoll bone sits exactly where the animation
/// bone of the same name sits, the transform between them is the identity, and a rigid body's
/// own transform is that same pose again. So the fitting is a matter of moving each body to
/// where the new rig puts its bone and leaving every authored number alone.
/// </para>
/// <para>
/// Two things are authored and have to be carried rather than recomputed. The capsules are in
/// body-local space and are scaled, not replaced, since a creature half the size wants a
/// ragdoll half the size and not a differently shaped one. The constraints' pivot frames hold
/// the hinge and cone axes in each body's own space: the child's side is kept exactly as the
/// prototype has it, because an axis means the same thing relative to the same bone, and only
/// the parent's side is recomputed so that the two agree about where the joint is.
/// </para>
/// <para>
/// Which prototype to take matters more than it looks. Skyrim's walking dead come in two
/// kinds, and the difference is not cosmetic: a fleshed draugr's hips, shoulders and head are
/// three-degree ball joints, where the bone skeleton's are hinges, and the skeleton carries a
/// hull for a skull and a jaw that swings. A skeleton given a draugr's ragdoll flops when it
/// ought to clatter.
/// </para>
/// </remarks>
public static class RagdollRetarget
{
    /// <summary>
    /// The prefix a ragdoll bone's name carries over the animation bone it stands for.
    /// </summary>
    public const string Prefix = "Ragdoll_";

    /// <summary>Fits the ragdoll in <paramref name="prototypePath"/> to <paramref name="rig"/>.</summary>
    /// <param name="prototypePath">
    /// A shipped skeleton packfile to take the ragdoll from: its bodies, its constraints and
    /// all of their limits. Choose one of the same kind of creature.
    /// </param>
    /// <param name="rig">
    /// The animation skeleton to fit it to, under the names the game uses. A rig whose bones
    /// are still called what an exporter called them matches nothing and keeps no bodies.
    /// </param>
    /// <param name="name">The name the fitted rig is saved under, or the rig's own if omitted.</param>
    public static RetargetedRagdoll Fit(string prototypePath, hkaSkeleton rig, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(prototypePath);
        ArgumentNullException.ThrowIfNull(rig);

        HavokFile file = HavokFile.Load(prototypePath);
        hkaAnimationContainer container = file.All<hkaAnimationContainer>().FirstOrDefault()
            ?? throw new InvalidDataException($"'{prototypePath}' holds no animation container");

        if (container.m_skeletons.Count < 2)
            throw new InvalidDataException(
                $"'{prototypePath}' holds {container.m_skeletons.Count} skeleton(s): a prototype needs the rig and the ragdoll");

        hkaSkeleton protoRig = container.m_skeletons[0], ragdollRig = container.m_skeletons[1];
        hkaRagdollInstance instance = file.All<hkaRagdollInstance>().FirstOrDefault()
            ?? throw new InvalidDataException($"'{prototypePath}' holds no ragdoll instance");

        if (name is { Length: > 0 }) rig.m_name = name;

        Matrix4x4[] protoWorld = World(protoRig), ourWorld = World(rig);
        float scale = Scale(protoRig, protoWorld, rig, ourWorld);

        // Which animation bone a ragdoll bone stands for is settled by the prototype's own
        // mapper, not by its name. The two do not always agree: the draugr's rig calls its
        // left foot 'NPC L Foot [LLft ]', with the L doubled, and its ragdoll spells the same
        // bone 'Ragdoll_NPC L Foot [Lft ]' without the typo. The engine pairs them by index
        // and never notices; anything matching on the name gets it wrong.
        IReadOnlyDictionary<int, int> stands = StandsFor(file, protoRig, ragdollRig);

        // A bone our rig has not got takes its body and its constraints with it.
        var keep = new List<int>();
        var dropped = new List<string>();
        var ourBone = new Dictionary<int, int>();

        for (int i = 0; i < ragdollRig.m_bones.Count; i++)
        {
            string wanted = stands.TryGetValue(i, out int theirs)
                ? protoRig.m_bones[theirs].m_name
                : Animation(ragdollRig.m_bones[i].m_name);

            int mine = IndexOf(rig, wanted);
            if (mine < 0) { dropped.Add(ragdollRig.m_bones[i].m_name); continue; }
            keep.Add(i);
            ourBone[i] = mine;
        }

        if (keep.Count == 0)
            throw new InvalidDataException(
                $"none of the prototype's {ragdollRig.m_bones.Count} ragdoll bones is in the rig: " +
                "the rig is probably still under an exporter's names rather than the game's");

        // A bone whose ragdoll parent was dropped would hang off nothing. Reattach it to the
        // nearest kept ancestor, so the chain stays whole rather than breaking into pieces.
        var order = keep.ToList();
        var place = order.Select((proto, at) => (proto, at)).ToDictionary(p => p.proto, p => p.at);

        var fitted = new hkaSkeleton
        {
            m_name = ragdollRig.m_name,
            m_bones = [.. order.Select(i => ragdollRig.m_bones[i])],
            m_parentIndices = [.. order.Select(i => (short)KeptParent(ragdollRig, i, place))],
            m_referenceFloats = ragdollRig.m_referenceFloats,
            m_floatSlots = ragdollRig.m_floatSlots,
            m_localFrames = ragdollRig.m_localFrames,
        };

        // Every body goes where our rig puts the bone it is named after; the reference pose is
        // that same placing, expressed against whichever parent the bone ended up with.
        var world = new Matrix4x4[order.Count];
        var pose = new Matrix4x4[order.Count];

        for (int at = 0; at < order.Count; at++)
        {
            world[at] = ourWorld[ourBone[order[at]]];
            int parent = fitted.m_parentIndices[at];
            Matrix4x4 local = parent < 0 ? world[at] : Local(world[parent], world[at]);
            pose[at] = Qs(local);
        }

        fitted.m_referencePose = pose;

        var bodies = new List<hkpRigidBody>(order.Count);
        foreach (int proto in order)
        {
            hkpRigidBody body = instance.m_rigidBodies[proto];
            Place(body, world[place[proto]], scale);
            bodies.Add(body);
        }

        var constraints = new List<hkpConstraintInstance>();
        foreach (hkpConstraintInstance constraint in instance.m_constraints)
        {
            if (Transforms(constraint) is not { } atom) continue;
            if (constraint.m_entities is not { Length: > 1 } ends) continue;
            if (ends[0] is not hkpRigidBody child || ends[1] is not hkpRigidBody parent) continue;

            int a = bodies.IndexOf(child), b = bodies.IndexOf(parent);
            if (a < 0 || b < 0) continue;

            // The child's side holds the joint's axes in the child bone's own space and means
            // the same thing on any creature, so it is kept and only scaled. The parent's side
            // is then whatever puts the two at the same joint under the new proportions.
            Matrix4x4 local = Scaled(Real(atom.m_transformA), scale);
            atom.m_transformA = Qs(local);
            atom.m_transformB = Qs(Local(world[b], world[a] * local));

            constraints.Add(constraint);
        }

        instance.m_skeleton = fitted;
        instance.m_rigidBodies = bodies;
        instance.m_constraints = constraints;
        instance.m_boneToRigidBodyMap = [.. Enumerable.Range(0, bodies.Count)];

        container.m_skeletons = [rig, fitted];

        foreach (hkpPhysicsSystem system in file.All<hkpPhysicsSystem>())
        {
            // Whatever else the system carries -- a bumper capsule, a collision proxy -- is
            // not ours to keep: it was built around the prototype's body and has no bone.
            system.m_rigidBodies = bodies;
            system.m_constraints = constraints;
        }

        foreach (hkaSkeletonMapper mapper in file.All<hkaSkeletonMapper>())
            Remap(mapper, rig, fitted, ourBone, order);

        return new RetargetedRagdoll(file, bodies.Count, dropped, scale);
    }

    /// <summary>
    /// The mappings, rebuilt: the game's own carry the identity between a ragdoll bone and the
    /// animation bone it stands for, and list every animation bone with no body as unmapped.
    /// </summary>
    private static void Remap(
        hkaSkeletonMapper mapper,
        hkaSkeleton rig,
        hkaSkeleton fitted,
        Dictionary<int, int> ourBone,
        List<int> order)
    {
        if (mapper.m_mapping is not { } data) return;

        bool forward = data.m_skeletonB == data.m_skeletonA || ReferenceEquals(data.m_skeletonA, data.m_skeletonB)
            ? true
            : data.m_simpleMappings.Count == 0 || IsRig(data.m_skeletonA, fitted);

        hkaSkeleton a = forward ? rig : fitted, b = forward ? fitted : rig;
        data.m_skeletonA = a;
        data.m_skeletonB = b;

        var simple = new List<hkaSkeletonMapperDataSimpleMapping>(order.Count);
        for (int at = 0; at < order.Count; at++)
        {
            short mine = (short)ourBone[order[at]];
            simple.Add(new hkaSkeletonMapperDataSimpleMapping
            {
                m_boneA = forward ? mine : (short)at,
                m_boneB = forward ? (short)at : mine,
                m_aFromBTransform = Qs(Matrix4x4.Identity),
            });
        }

        data.m_simpleMappings = simple;
        data.m_chainMappings = [];

        // Going the other way, an animation bone with no body of its own is driven by nothing
        // and has to be named as such, or it is left wherever the last pose put it.
        var mapped = ourBone.Values.ToHashSet();
        data.m_unmappedBones = forward
            ? []
            : [.. Enumerable.Range(0, rig.m_bones.Count).Where(i => !mapped.Contains(i)).Select(i => (short)i)];
    }

    private static bool IsRig(hkaSkeleton? candidate, hkaSkeleton fitted) => !ReferenceEquals(candidate, fitted);

    /// <summary>The animation bone a ragdoll bone stands for: its own name, less the trimmings.</summary>
    /// <remarks>
    /// The game's ragdoll bones are the rig's names under a <c>Ragdoll_</c> prefix, and some
    /// carry a trailing <c>01</c> that the exporter added to keep two objects apart in the
    /// scene. Neither is part of the bone.
    /// </remarks>
    public static string Animation(string ragdollBone)
    {
        string name = ragdollBone.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? ragdollBone[Prefix.Length..]
            : ragdollBone;

        return name.EndsWith("01", StringComparison.Ordinal) ? name[..^2] : name;
    }

    /// <summary>
    /// What the new creature measures against the prototype: how far its topmost bone stands
    /// above its lowest, which is the one length both rigs are certain to have.
    /// </summary>
    private static float Scale(hkaSkeleton proto, Matrix4x4[] protoWorld, hkaSkeleton ours, Matrix4x4[] ourWorld)
    {
        float theirs = Height(protoWorld), mine = Height(ourWorld);
        return theirs > 1e-4f && mine > 1e-4f ? mine / theirs : 1f;

        static float Height(Matrix4x4[] world) =>
            world.Length == 0 ? 0f : world.Max(m => m.Translation.Z) - world.Min(m => m.Translation.Z);
    }

    private static void Place(hkpRigidBody body, Matrix4x4 world, float scale)
    {
        hkMotionState state = body.m_motion.m_motionState;
        state.m_transform = world;

        Quaternion rotation = Quaternion.CreateFromRotationMatrix(world);
        Vector3 centre = Vector3.Transform(ToVector3(state.m_sweptTransform.m_centerOfMassLocal) * scale, world);
        var at = new Vector4(centre, 0f);

        state.m_sweptTransform.m_centerOfMass0 = at;
        state.m_sweptTransform.m_centerOfMass1 = at;
        state.m_sweptTransform.m_rotation0 = rotation;
        state.m_sweptTransform.m_rotation1 = rotation;
        state.m_sweptTransform.m_centerOfMassLocal =
            state.m_sweptTransform.m_centerOfMassLocal * scale;
        state.m_objectRadius *= scale;

        if (body.m_collidable?.m_shape is hkpCapsuleShape capsule)
        {
            capsule.m_vertexA *= scale;
            capsule.m_vertexB *= scale;
            capsule.m_radius *= scale;
        }
        else if (body.m_collidable?.m_shape is hkpConvexVerticesShape hull)
        {
            // A skull is a hull rather than a capsule. Its points are kept four at a time and
            // transposed: one vector holds four x's, the next four y's, the last four z's.
            foreach (hkpConvexVerticesShapeFourVectors four in hull.m_rotatedVertices)
            {
                four.m_x *= scale;
                four.m_y *= scale;
                four.m_z *= scale;
            }

            // A face is a normal and a distance. Moving the points further out moves the faces
            // with them, but leaves every normal pointing where it pointed.
            hull.m_planeEquations =
            [
                .. hull.m_planeEquations.Select(p => new Vector4(p.X, p.Y, p.Z, p.W * scale)),
            ];

            hull.m_aabbHalfExtents *= scale;
            hull.m_aabbCenter *= scale;
            hull.m_radius *= scale;
        }
    }

    private static hkpSetLocalTransformsConstraintAtom? Transforms(hkpConstraintInstance constraint) =>
        constraint.m_data switch
        {
            hkpRagdollConstraintData ragdoll => ragdoll.m_atoms.m_transforms,
            hkpLimitedHingeConstraintData hinge => hinge.m_atoms.m_transforms,
            _ => null,
        };

    private static int KeptParent(hkaSkeleton ragdoll, int bone, Dictionary<int, int> place)
    {
        int at = ragdoll.m_parentIndices[bone];
        while (at >= 0 && !place.ContainsKey(at)) at = ragdoll.m_parentIndices[at];
        return at < 0 ? -1 : place[at];
    }

    /// <summary>
    /// Which animation bone each of the prototype's ragdoll bones stands for, read off the
    /// prototype's own mapper.
    /// </summary>
    private static IReadOnlyDictionary<int, int> StandsFor(HavokFile file, hkaSkeleton rig, hkaSkeleton ragdoll)
    {
        var stands = new Dictionary<int, int>();

        foreach (hkaSkeletonMapper mapper in file.All<hkaSkeletonMapper>())
        {
            if (mapper.m_mapping is not { } data) continue;

            // A file carries the mapping both ways round; either will do, read the right way.
            bool forward = ReferenceEquals(data.m_skeletonA, rig) && ReferenceEquals(data.m_skeletonB, ragdoll);
            bool backward = ReferenceEquals(data.m_skeletonA, ragdoll) && ReferenceEquals(data.m_skeletonB, rig);
            if (!forward && !backward) continue;

            foreach (hkaSkeletonMapperDataSimpleMapping pair in data.m_simpleMappings)
            {
                int bone = forward ? pair.m_boneB : pair.m_boneA;
                int animation = forward ? pair.m_boneA : pair.m_boneB;
                if (bone >= 0 && bone < ragdoll.m_bones.Count && animation >= 0 && animation < rig.m_bones.Count)
                    stands.TryAdd(bone, animation);
            }
        }

        return stands;
    }

    /// <summary>
    /// The bone a name refers to, allowing for the short form in brackets being misspelled.
    /// </summary>
    /// <remarks>
    /// A bone's name is prose followed by a four-character tag, and the tag is where the
    /// mistakes are: the draugr's left foot is tagged <c>[LLft ]</c> and everybody else's
    /// <c>[Lft ]</c>. The prose is what the bone is; the tag is an abbreviation of it.
    /// </remarks>
    private static int IndexOf(hkaSkeleton rig, string name)
    {
        for (int i = 0; i < rig.m_bones.Count; i++)
            if (string.Equals(rig.m_bones[i].m_name, name, StringComparison.OrdinalIgnoreCase)) return i;

        string prose = Prose(name);
        if (prose.Length == 0) return -1;

        int found = -1;
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            if (!string.Equals(Prose(rig.m_bones[i].m_name), prose, StringComparison.OrdinalIgnoreCase)) continue;
            if (found >= 0) return -1; // Two bones under one prose name: no answer is better than the wrong one.
            found = i;
        }

        return found;
    }

    private static string Prose(string name)
    {
        int bracket = name.IndexOf('[');
        return (bracket < 0 ? name : name[..bracket]).Trim();
    }

    private static Matrix4x4 Local(Matrix4x4 parent, Matrix4x4 child) =>
        Matrix4x4.Invert(parent, out Matrix4x4 back) ? child * back : child;

    private static Matrix4x4 Scaled(Matrix4x4 m, float scale)
    {
        m.Translation *= scale;
        return m;
    }

    private static Matrix4x4[] World(hkaSkeleton rig)
    {
        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            Matrix4x4 local = Real(rig.m_referencePose[i]);
            short parent = i < rig.m_parentIndices.Count ? rig.m_parentIndices[i] : (short)-1;
            world[i] = parent < 0 ? local : local * world[parent];
        }

        return world;
    }

    /// <summary>
    /// HKX2 keeps an hkQsTransform in a Matrix4x4 rather than converting it: the translation
    /// in the first row, the rotation quaternion in the second, the scale in the third. It is
    /// a container and not a matrix, and multiplying two of them means nothing.
    /// </summary>
    internal static Matrix4x4 Real(Matrix4x4 qs) =>
        Matrix4x4.CreateScale(new Vector3(qs.M31, qs.M32, qs.M33))
        * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(qs.M21, qs.M22, qs.M23, qs.M24)))
        * Matrix4x4.CreateTranslation(new Vector3(qs.M11, qs.M12, qs.M13));

    internal static Matrix4x4 Qs(Matrix4x4 real)
    {
        if (!Matrix4x4.Decompose(real, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            translation = real.Translation;
        }

        return new Matrix4x4(
            translation.X, translation.Y, translation.Z, 0f,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            scale.X, scale.Y, scale.Z, 0f,
            0f, 0f, 0f, 0f);
    }

    private static Vector3 ToVector3(Vector4 v) => new(v.X, v.Y, v.Z);
}
