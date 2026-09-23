using System.Numerics;
using System.Text;
using HKSK.Assembly;
using HKSK.Havok;
using HKX2;
using Xunit;

namespace HKSK.Tests;

// The whole skeleton route against the bought asset: read its rig, stand it on the floor, put
// it under the game's names, and fit the bone skeleton's ragdoll to it.
public sealed class ZzBuildSkeleton
{
    [Fact]
    public void Dump()
    {
        string corpus = Environment.GetEnvironmentVariable("HKSK_CORPUS") ?? "";
        string asset = Environment.GetEnvironmentVariable("HKSK_ASSET") ?? "";
        if (corpus.Length == 0 || !File.Exists(asset)) return;

        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        string built = Path.Combine(outDir, "skeleton.hkx");
        var sb = new StringBuilder();

        AssembledSkeleton made = CreatureSkeleton.Build(
            asset,
            Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx"),
            built,
            feet: [("Bip01_L_Foot", "Bip01_L_Toe0"), ("Bip01_R_Foot", "Bip01_R_Toe0")]);

        sb.AppendLine(made.ToString());
        sb.AppendLine();
        sb.AppendLine($"grounded: lift={made.Grounded?.Lift:0.##} lowest={made.Grounded?.Lowest:0.##} rootAdded={made.Grounded?.RootAdded}");
        sb.AppendLine($"renamed: {made.Renamed}");
        sb.AppendLine($"  unmapped: {string.Join(", ", made.Renamed.Unmapped)}");
        sb.AppendLine($"ragdoll: {made.Ragdoll}");
        sb.AppendLine();

        // Read it back the way anything else would.
        var file = HavokFile.Load(built);
        var container = file.All<hkaAnimationContainer>().First();
        hkaSkeleton rig = container.m_skeletons[0], ragdoll = container.m_skeletons[1];
        var instance = file.All<hkaRagdollInstance>().First();

        sb.AppendLine($"read back: rig '{rig.m_name}' {rig.m_bones.Count} bones, ragdoll {ragdoll.m_bones.Count} bones, " +
                      $"{instance.m_rigidBodies.Count} bodies, {instance.m_constraints.Count} constraints");
        sb.AppendLine();

        var world = new Matrix4x4[rig.m_bones.Count];
        for (int i = 0; i < rig.m_bones.Count; i++)
        {
            Matrix4x4 local = RagdollRetarget.Real(rig.m_referencePose[i]);
            short parent = rig.m_parentIndices[i];
            world[i] = parent < 0 ? local : local * world[parent];
        }

        sb.AppendLine($"rig stands from {world.Min(m => m.Translation.Z):0.##} to {world.Max(m => m.Translation.Z):0.##}");
        sb.AppendLine();

        foreach (var bone in rig.m_bones.Select((b, i) => (b, i)))
            sb.AppendLine($"  [{bone.i,2}] parent={rig.m_parentIndices[bone.i],2} z={world[bone.i].Translation.Z,7:0.##} {bone.b.m_name}");

        sb.AppendLine();
        foreach (var body in instance.m_rigidBodies)
        {
            string shape = body.m_collidable?.m_shape switch
            {
                hkpCapsuleShape c => $"capsule r={c.m_radius:0.##}",
                hkpConvexVerticesShape h => $"hull ({h.m_numVertices} points)",
                var s => s?.GetType().Name ?? "none",
            };
            sb.AppendLine($"  body '{body.m_name}' at z={body.m_motion.m_motionState.m_transform.Translation.Z:0.##} {shape}");
        }

        // The clips, over the rig the skeleton file now holds.
        string clips = Environment.GetEnvironmentVariable("HKSK_CLIPS") ?? "";
        string template = Path.Combine(corpus, "actors", "draugr", "animations", "mt_idle.hkx");
        if (!File.Exists(template))
            template = Directory.GetFiles(Path.Combine(corpus, "actors", "draugr", "animations"), "*.hkx").First();

        if (File.Exists(clips))
        {
            sb.AppendLine();
            sb.AppendLine($"--- clips from {Path.GetFileName(clips)}, template {Path.GetFileName(template)}");

            WrittenClips done = CreatureClips.Write(
                clips, template, Path.Combine(outDir, "animations"), made.Renamed.Rig, made.Names,
                turnDegrees: made.Grounded?.Turn ?? 0f);

            sb.AppendLine(done.ToString());
            foreach (WrittenClip clip in done.Clips) sb.AppendLine($"  {clip}");

            sb.AppendLine();
            sb.AppendLine("--- what each clip is, and whether its feet agree");
            RoledClips read = ClipRoles.Of(done);
            sb.AppendLine(read.ToString());
            foreach (RoledClip clip in read.Clips)
                sb.AppendLine($"  {clip.Clip.Name,-16} {clip.Animation.Roles.FirstOrDefault()?.ToString() ?? "(nothing)"}" +
                              $"  speed={(clip.Animation.Speed is { } v ? $"{v:0.#}" : "-")}" +
                              (clip.Disagreement is null ? "" : $"  !! {clip.Disagreement}"));

            foreach (string note in read.Unmeasured) sb.AppendLine($"  ?? {note}");

            // And the whole creature, from the clips that read as something.
            sb.AppendLine();
            sb.AppendLine("--- assembled");

            var usable = read.Clips
                .Where(c => c.Animation.Roles.Count > 0)
                .Select(c => c.Animation)
                .ToList();

            sb.AppendLine($"{usable.Count} of {read.Clips.Count} clips have a role to fill");

            var spec = new CreatureSpec(
                "BoneSkeleton",
                built,
                usable,
                MovementTypeName: "BoneSkeletonDefault",
                ClipDurations: read.Durations);

            try
            {
                AssemblyResult made2 = CreatureAssembler.Assemble(spec, Path.Combine(outDir, "creature"));
                sb.AppendLine($"project: {made2.ProjectPath}");
                sb.AppendLine($"plan: {made2.Plan.Locomotion}, modules {string.Join(", ", made2.Plan.Modules)}");
                sb.AppendLine($"movement: {made2.Movement}");
                sb.AppendLine($"files ({made2.Files.Count}):");
                foreach (string f in made2.Files) sb.AppendLine($"  {f}");
                sb.AppendLine("notes:");
                foreach (string n in made2.Notes) sb.AppendLine($"  {n}");
                sb.AppendLine($"cache: {made2.Cache.Name}, {made2.Cache.Movements?.Movements.Count ?? 0} movement rows");
            }
            catch (Exception e)
            {
                sb.AppendLine($"!! {e.GetType().Name}: {e.Message}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "built-skeleton.txt"), sb.ToString());
    }
}
