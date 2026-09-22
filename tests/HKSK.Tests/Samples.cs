using HKFBX.Codec;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Havok;

namespace HKSK.Tests;

/// <summary>An animation packfile as frames, however it is stored.</summary>
internal static class Samples
{
    /// <summary>
    /// An uncompressed animation read as it is, a compressed one through Havok's codec: an import
    /// writes the first by default and the game ships the second.
    /// </summary>
    public static SampledAnimation Of(string path, IAnimationCodec? codec = null)
    {
        if (UncompressedAnimation.Read(path) is { } raw) return raw;

        (SplineAnimationData spline, IReadOnlyList<short> trackToBone, _) = HkxAnimationFile.ReadAnimation(path);
        return (codec ?? new MopperAnimationCodec()).Decompress(spline) with { TrackToBone = trackToBone };
    }
}
