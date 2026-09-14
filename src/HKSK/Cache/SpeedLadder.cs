using System.Numerics;
using HKSK.Model;
using HKX2;

namespace HKSK.Cache;

/// <summary>
/// One step of a locomotion blend: where it sits, and what it delivers.
/// </summary>
/// <remarks>
/// <see cref="Weight"/> is the child's position on the blend-parameter axis --
/// authored, and for a cardinal blender it is the movement type's own value for
/// that direction. <see cref="Travel"/> and <see cref="Duration"/> describe the
/// clip beneath it, so <c>|Travel| / Duration</c> is what it actually delivers.
///
/// The two are meant to be the same number. Where they are not, the difference
/// is what <c>speeddatasinglefile.txt</c> records.
/// </remarks>
/// <param name="Weight">Position on the blend-parameter axis, in game units/s.</param>
/// <param name="Travel">Root-motion displacement of the clip, as a vector.</param>
/// <param name="Duration">Clip duration divided by its PlaybackSpeed.</param>
/// <param name="Animation">The clip this rung plays, when it came from a graph.</param>
public readonly record struct SpeedRung(float Weight, Vector3 Travel, float Duration, string? Animation = null)
{
    /// <summary>What this rung delivers on its own, in game units/s.</summary>
    public float Delivered => Duration > 0f ? Travel.Length() / Duration : 0f;

    public override string ToString() => $"w={Weight:G6} -> {Delivered:G6}";
}

/// <summary>
/// The rungs of one locomotion blender, and the response curve they produce.
/// </summary>
/// <remarks>
/// This is the model <c>docs/speed-data.md</c> §6 describes, made executable. It
/// is what a speed table records: set the blend parameter to x, and the creature
/// travels at <see cref="Evaluate"/>(x).
///
/// <strong>Accuracy.</strong> <see cref="Evaluate"/> is the runtime's own
/// behaviour, not an approximation to it: measured against Havok Behavior with a
/// blender built to a shipped ladder's shape, it agrees to float32 epsilon
/// (docs/speed-data.md §6.1).
///
/// The shipped table is a different question, and <see cref="Tabulate"/> answers
/// it: the sampler that wrote <c>speeddatasinglefile.txt</c> recorded the response
/// at <c>x - </c><see cref="SamplerOffset"/> while storing <c>x</c>.
/// </remarks>
public sealed class SpeedLadder
{
    /// <summary>The rungs, ordered by <see cref="SpeedRung.Weight"/>.</summary>
    public IReadOnlyList<SpeedRung> Rungs { get; }

    /// <summary>The blender these rungs came from, when they came from one.</summary>
    public string? Name { get; init; }

    public SpeedLadder(IEnumerable<SpeedRung> rungs)
    {
        ArgumentNullException.ThrowIfNull(rungs);
        Rungs = [.. rungs.OrderBy(r => r.Weight)];
    }

    /// <summary>The speed delivered when the blend parameter is <paramref name="x"/>.</summary>
    /// <remarks>
    /// The blend is time-synchronised, so the children's translation and duration
    /// interpolate as two independent ramps and the speed is the magnitude of the
    /// first over the second. **Travel is a vector and must stay one**: collapsing
    /// it to a length before interpolating overstates any blend that mixes
    /// headings, because |lerp(a,b)| &lt; lerp(|a|,|b|) unless a and b are parallel.
    /// </remarks>
    public float Evaluate(float x)
    {
        (Vector3 travel, float duration) = Resolve(x);
        return duration > 0f ? travel.Length() / duration : 0f;
    }

    /// <summary>
    /// The travel and duration the blend produces at <paramref name="x"/>, before
    /// they are divided.
    /// </summary>
    /// <remarks>
    /// A heading that falls between two compass arms is a blend of both, and
    /// blending two speeds is not the same as blending what produces them: the
    /// travel has to stay a vector and the duration has to stay separate all the
    /// way to the last division (§5.2, §6). So the pair is what a caller composing
    /// two ladders needs, and <see cref="Evaluate"/> is the single-ladder case of
    /// it.
    /// </remarks>
    public (Vector3 Travel, float Duration) Resolve(float x)
    {
        if (Rungs.Count == 0) return (Vector3.Zero, 0f);
        if (Rungs.Count == 1 || x <= Rungs[0].Weight) return (Rungs[0].Travel, Rungs[0].Duration);
        if (x >= Rungs[^1].Weight) return (Rungs[^1].Travel, Rungs[^1].Duration);

        for (int i = 1; i < Rungs.Count; i++)
        {
            SpeedRung b = Rungs[i];
            if (x > b.Weight) continue;

            SpeedRung a = Rungs[i - 1];
            float span = b.Weight - a.Weight;
            if (span <= 0f) return (b.Travel, b.Duration);

            float u = (x - a.Weight) / span;
            return (Vector3.Lerp(a.Travel, b.Travel, u), a.Duration + u * (b.Duration - a.Duration));
        }

        return (Rungs[^1].Travel, Rungs[^1].Duration);
    }

    /// <summary>The parameter above which the response stops changing.</summary>
    public float Saturation => Rungs.Count == 0 ? 0f : Rungs[^1].Weight;

    /// <summary>
    /// The shift between the x a shipped sampler stored and the response it
    /// recorded there.
    /// </summary>
    /// <remarks>
    /// Measured, not derived. <see cref="Evaluate"/> is the blend law exactly, so
    /// this is the whole of the difference between the law and the shipped file:
    /// fitting <c>x' = a*x + b</c> over the corpus gives <c>a = 1.000000</c> and
    /// <c>b = -0.040445</c>, a pure offset with no scale, constant across creatures
    /// whose segment spans run from 30 to 3160 and whose duration ratios run from
    /// 1.2 to 71.
    ///
    /// Applying it takes the median error against the shipped file from 0.0715% to
    /// 0.0021%. Where it comes from is not known: it is not in the blender, which
    /// was measured; not in the flags; and not in the sampling grid, which is 0.5
    /// throughout. Treat it as a property of Bethesda's sampler.
    /// </remarks>
    public const float SamplerOffset = 0.0404f;

    /// <summary>What a shipped speed table records at <paramref name="x"/>.</summary>
    /// <remarks>
    /// <see cref="Evaluate"/> is what the creature does when the blend parameter is
    /// x. This is what the file says at x, which is the same curve read
    /// <see cref="SamplerOffset"/> earlier. Use this to check a table or to
    /// regenerate one; use <see cref="Evaluate"/> to predict a creature.
    /// </remarks>
    public float Tabulate(float x) => Evaluate(x - SamplerOffset);

    /// <summary>
    /// Builds a ladder from a blender, resolving each child's clip through the cache.
    /// </summary>
    /// <remarks>
    /// A child may be a clip generator or a subtree containing several -- a
    /// quadruped's rung is a turn blender holding the straight clip and its left
    /// and right variants. The shortest animation name under a child is taken,
    /// which picks the straight one, since the variants are the same name with a
    /// suffix.
    ///
    /// Rungs whose clip has no root motion in the cache are skipped rather than
    /// zeroed: a rung with no motion is a rung this library does not know about,
    /// and inventing a zero for it would silently flatten the curve.
    /// </remarks>
    public static SpeedLadder FromBlender(hkbBlenderGenerator blender, ActorProject project)
    {
        ArgumentNullException.ThrowIfNull(blender);
        ArgumentNullException.ThrowIfNull(project);

        var rungs = new List<SpeedRung>();

        foreach (hkbBlenderGeneratorChild? child in blender.m_children ?? [])
        {
            if (child?.m_generator is null) continue;

            hkbClipGenerator? clip = ClipsUnder(child.m_generator)
                .OrderBy(c => (c.m_animationName ?? "").Length)
                .FirstOrDefault();

            if (clip?.m_animationName is not { } name || clip.m_playbackSpeed <= 0f) continue;

            AnimationSlot? slot = project.Animation(Path.GetFileNameWithoutExtension(name.Replace('\\', '/')));
            ClipMovement? motion = slot?.Motion;
            if (motion is null || motion.Duration <= 0f || motion.Translations.Count == 0) continue;

            rungs.Add(new SpeedRung(
                child.m_weight,
                motion.Translations[^1].Value,
                motion.Duration / clip.m_playbackSpeed,
                Path.GetFileNameWithoutExtension(name.Replace('\\', '/'))));
        }

        return new SpeedLadder(rungs) { Name = blender.m_name };
    }

    private static IEnumerable<hkbClipGenerator> ClipsUnder(object? node)
    {
        switch (node)
        {
            case null: yield break;
            case hkbClipGenerator clip: yield return clip; yield break;
            case hkbBlenderGenerator blend:
                foreach (hkbBlenderGeneratorChild? c in blend.m_children ?? [])
                    foreach (hkbClipGenerator found in ClipsUnder(c?.m_generator)) yield return found;
                yield break;
            case hkbModifierGenerator mod:
                foreach (hkbClipGenerator found in ClipsUnder(mod.m_generator)) yield return found;
                yield break;
        }
    }
}
