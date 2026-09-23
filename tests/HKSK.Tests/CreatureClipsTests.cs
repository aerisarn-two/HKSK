using System.Numerics;
using HKFBX.Fbx;
using LeanMeshIO;
using HKFBX.Model;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Turning an FBX's takes into a creature's animations.
/// </summary>
public sealed class CreatureClipsTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"hksk-clips-{Guid.NewGuid():N}");

    public CreatureClipsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A rig under an exporter's names: hips, a spine, two legs with toes.</summary>
    private static Skeleton Exported() => new()
    {
        Name = "Bip01",
        Bones =
        [
            new Bone("Bip01", -1, Rest(0, 0, 40)),
            new Bone("Bip01_Pelvis", 0, Rest(0, 0, 0)),
            new Bone("Bip01_Spine", 0, Rest(0, 0, 10)),
            new Bone("Bip01_Spine1", 2, Rest(0, 0, 10)),
            new Bone("Bip01_Spine2", 3, Rest(0, 0, 10)),
            new Bone("Bip01_Neck", 4, Rest(0, 0, 5)),
            new Bone("Bip01_Head", 5, Rest(0, 0, 5)),
            new Bone("Bip01_L_Clavicle", 4, Rest(5, 0, 0)),
            new Bone("Bip01_R_Clavicle", 4, Rest(-5, 0, 0)),
            new Bone("Bip01_L_Thigh", 1, Rest(5, 0, 0)),
            new Bone("Bip01_L_Calf", 9, Rest(0, 0, -20)),
            new Bone("Bip01_L_Foot", 10, Rest(0, 0, -20)),
            new Bone("Bip01_L_Toe0", 11, Rest(0, 5, 0)),
            new Bone("Bip01_R_Thigh", 1, Rest(-5, 0, 0)),
            new Bone("Bip01_R_Calf", 13, Rest(0, 0, -20)),
            new Bone("Bip01_R_Foot", 14, Rest(0, 0, -20)),
            new Bone("Bip01_R_Toe0", 15, Rest(0, 5, 0)),
        ],
    };

    private static BoneTransform Rest(float x, float y, float z) =>
        new(new Vector3(x, y, z), Quaternion.Identity, Vector3.One);

    /// <summary>
    /// A walk on the spot: the feet take turns sliding backwards along the ground, which is
    /// what a creature travelling forwards looks like when the root does not move.
    /// </summary>
    private static SampledAnimation Walk(Skeleton rig, int frames = 20, float slide = 2f)
    {
        var transforms = new BoneTransform[frames * rig.Count];

        for (int frame = 0; frame < frames; frame++)
        {
            for (int bone = 0; bone < rig.Count; bone++)
                transforms[frame * rig.Count + bone] = rig.Bones[bone].ReferencePose;

            // The left foot is down for the first half and the right for the second; whichever
            // is down slides back, and whichever is up is lifted clear.
            bool leftDown = frame < frames / 2;
            int step = leftDown ? frame : frame - frames / 2;

            Set(11, new Vector3(0, -slide * step, leftDown ? 0 : 10));
            Set(15, new Vector3(0, -slide * step, leftDown ? 10 : 0));

            void Set(int bone, Vector3 by)
            {
                BoneTransform rest = rig.Bones[bone].ReferencePose;
                transforms[frame * rig.Count + bone] =
                    rest with { Translation = rest.Translation + by };
            }
        }

        return new SampledAnimation
        {
            FrameCount = frames,
            TrackCount = rig.Count,
            Duration = frames / 30f,
            FrameDuration = 1 / 30f,
            Transforms = transforms,
        };
    }

    private string Fbx(Skeleton rig, SampledAnimation clip, string take)
    {
        string path = Path.Combine(_folder, take + ".fbx");
        FbxAnimationWriter.Build(rig, clip, take, BoneNaming.Havok).Save(path);
        return path;
    }

    /// <summary>
    /// The tracks are bound by name, not by position. The creature's rig has grown a root and
    /// a lift above everything the clip knows about, so a clip written without this drives
    /// bones two along from the ones it means -- and nothing catches it, because every index
    /// is valid and the creature simply moves the wrong parts.
    /// </summary>
    [CorpusFact]
    public void ClipTracksAreBoundToTheBonesTheyNameAndNotToTheirOldPlaces()
    {
        Skeleton exported = Exported();
        string fbx = Fbx(exported, Walk(exported), "Walk");

        Skeleton grounded = SkeletonGrounding.Ground(exported).Rig;
        Skeleton creature = SkeletonNaming
            .Rename(grounded, SkeletonNaming.BipedMap(grounded, hasMagicNode: false)).Rig;

        // The tracks are in the order the FBX gives its bones back, which is a depth-first
        // walk and not the order they went in.
        Skeleton asRead = FbxAnimationReader.ReadSkeleton(FbxDocument.Load(fbx));

        string template = Directory
            .GetFiles(Corpus.Path_("actors", "draugr", "animations"), "*.hkx").Order().First();

        WrittenClips done = CreatureClips.Write(
            fbx, template, Path.Combine(_folder, "out"), creature,
            SkeletonNaming.BipedMap(grounded, hasMagicNode: false), takes: ["Walk"]);

        WrittenClip clip = Assert.Single(done.Clips);
        Assert.True(File.Exists(clip.Path));

        var binding = HavokFile.Load(clip.Path).All<hkaAnimationContainer>().First().m_bindings[0];
        var bound = binding.m_transformTrackToBoneIndices;

        Assert.Equal(asRead.Count, bound.Count);

        // Track by track, the bone it drives is the bone of that name in the creature's rig.
        var map = SkeletonNaming.BipedMap(asRead, hasMagicNode: false);
        for (int track = 0; track < asRead.Count; track++)
        {
            string wanted = map.GetValueOrDefault(asRead.Bones[track].Name, asRead.Bones[track].Name);
            Assert.Equal(wanted, creature.Bones[bound[track]].Name);
        }

        // And the order really is not the one the rig went in, which is the whole point.
        Assert.NotEqual(
            exported.Bones.Select(b => b.Name),
            asRead.Bones.Select(b => b.Name));

        // Grounding put two bones on top, so nothing drives the root or the lift.
        Assert.DoesNotContain(bound, i => i == 0);
        Assert.DoesNotContain(bound, i => i == 1);
    }

    /// <summary>
    /// The travel is read off the feet and handed back, and is not written into the clip: in
    /// Skyrim an animation carries no travel at all, the cache does.
    /// </summary>
    [CorpusFact]
    public void TheTravelIsMeasuredAndLeftOutOfTheAnimation()
    {
        Skeleton exported = Exported();

        // Twenty frames at thirty a second, the planted foot sliding two units a frame: the
        // creature means to travel sixty units a second forwards.
        string fbx = Fbx(exported, Walk(exported), "Walk");

        Skeleton grounded = SkeletonGrounding.Ground(exported).Rig;
        Skeleton creature = SkeletonNaming
            .Rename(grounded, SkeletonNaming.BipedMap(grounded, hasMagicNode: false)).Rig;

        string template = Directory
            .GetFiles(Corpus.Path_("actors", "draugr", "animations"), "*.hkx").Order().First();

        WrittenClip clip = Assert.Single(
            CreatureClips.Write(
                fbx, template, Path.Combine(_folder, "out"), creature,
                SkeletonNaming.BipedMap(grounded, hasMagicNode: false), takes: ["Walk"]).Clips);

        Assert.NotNull(clip.Travel);
        Assert.Equal(60f, clip.Travel!.Speed, 0);

        // Forwards, which for a rig whose toes point along +Y is zero.
        Assert.InRange(Math.Abs(clip.Travel.Heading), 0f, 5f);

        // And the file itself says nothing about travelling, as the game's own do not.
        var animation = HavokFile.Load(clip.Path).All<hkaAnimationContainer>().First().m_animations[0];
        Assert.Null(animation.m_extractedMotion);
    }
}
