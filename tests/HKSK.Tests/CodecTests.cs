using System.Numerics;
using HKFBX.Codec;
using HKFBX.Model;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// Skips a test when Havok's spline codec is not available.
/// </summary>
/// <remarks>
/// Unlike <see cref="FbxFactAttribute"/> this asks only for mopper, not for game
/// data, so it runs on any machine that can execute mopper.exe -- which is what
/// makes the codec testable on a hosted runner.
/// </remarks>
public sealed class MopperFactAttribute : FactAttribute
{
    public MopperFactAttribute()
    {
        if (!Mopper.Available) Skip = "mopper.exe cannot be run here (not found, or no Wine off Windows)";
    }
}

/// <summary>
/// Havok's spline codec, on an animation built here rather than read from the game.
/// </summary>
/// <remarks>
/// Every other test that touches the codec goes through a real project, so it
/// needs the corpus and cannot run in CI. This one needs nothing but mopper, so
/// it is the test that proves the runner can actually execute Havok's encoder --
/// and that HKSK's dependency on it is wired up, since a missing or unrunnable
/// mopper.exe shows here as a skip rather than as a green suite that quietly
/// tested nothing.
///
/// Spline compression is lossy by design: it fits curves and quantizes them. The
/// tolerances are what a smooth motion of this size survives, not exact equality.
/// </remarks>
public class CodecTests
{
    [MopperFact]
    public void AnAnimationSurvivesCompressionAndDecompression()
    {
        SampledAnimation original = Wave(bones: 4, frames: 30);
        var codec = new MopperAnimationCodec();

        SplineAnimationData compressed = codec.Compress(original);

        Assert.True(compressed.Data.Length > 0, "the encoder produced no spline data");
        Assert.Equal(original.TrackCount, compressed.TransformTrackCount);

        SampledAnimation restored = codec.Decompress(compressed);

        Assert.Equal(original.FrameCount, restored.FrameCount);
        Assert.Equal(original.TrackCount, restored.TrackCount);
        Assert.Equal(original.Duration, restored.Duration, 3);

        double worstTranslation = 0, worstRotation = 0;

        for (int frame = 0; frame < original.FrameCount; frame++)
        for (int track = 0; track < original.TrackCount; track++)
        {
            BoneTransform a = original[frame, track];
            BoneTransform b = restored[frame, track];

            worstTranslation = Math.Max(worstTranslation, (a.Translation - b.Translation).Length());

            // A quaternion and its negation are the same rotation, so the closer
            // of the two differences is the real error.
            float same = (a.Rotation - b.Rotation).Length();
            float flipped = (a.Rotation + b.Rotation).Length();
            worstRotation = Math.Max(worstRotation, Math.Min(same, flipped));
        }

        Assert.True(worstTranslation < 0.1, $"translation drifted {worstTranslation}");
        Assert.True(worstRotation < 0.05, $"rotation drifted {worstRotation}");
    }

    /// <summary>
    /// A smooth motion: each bone offset along X, swinging about Z.
    /// </summary>
    /// <remarks>
    /// Smooth on purpose. Spline fitting is what is being exercised, and noise
    /// would test the quantizer's error bounds rather than that the codec runs.
    /// </remarks>
    private static SampledAnimation Wave(int bones, int frames)
    {
        const float frameDuration = 1f / 30f;
        var transforms = new BoneTransform[frames * bones];

        for (int frame = 0; frame < frames; frame++)
        {
            float time = frame * frameDuration;

            for (int bone = 0; bone < bones; bone++)
            {
                float phase = time * MathF.Tau + bone * 0.5f;

                transforms[frame * bones + bone] = new BoneTransform(
                    new Vector3(bone * 10f, MathF.Sin(phase) * 5f, 0f),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Sin(phase) * 0.5f),
                    Vector3.One);
            }
        }

        return new SampledAnimation
        {
            FrameCount = frames,
            TrackCount = bones,
            Duration = (frames - 1) * frameDuration,
            FrameDuration = frameDuration,
            Transforms = transforms,
        };
    }
}
