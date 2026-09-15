using HKSK.Behavior;
using HKSK.Model;
using HKX2;

namespace HKSK.Tests;

/// <summary>
/// The arms a locomotion state blends between.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The state's own blends are the arms.</strong> Where there are several,
/// each is a child of one enclosing blender and that child's weight is its heading,
/// so the enclosing blender is the compass. Where there is one, there is no compass
/// and no heading to read: the creature moves forward and turns, and the single
/// blend is the whole of its locomotion.
/// </para>
/// <para>
/// Looking for an enclosing blender first and giving up when there isn't one costs
/// 19 of the 86 shipped blocks -- every forward-only creature, from the chicken to
/// the mammoth. It also misleads: the horse's single blend sits under
/// <c>SaddleOffsetBlend</c>, which is a blender but not a compass, and reading its
/// child weights as headings gets the horse wrong rather than skipping it.
/// </para>
/// </remarks>
public static class Compass
{
    /// <summary>
    /// The arms of a locomotion state, each a heading and the ladder at it.
    /// </summary>
    public static List<(float Direction, SpeedLadder Ladder)> ArmsOf(
        ProjectWalk walk, LocomotionState state, ActorProject project)
    {
        ArgumentNullException.ThrowIfNull(walk);
        ArgumentNullException.ThrowIfNull(project);

        var arms = new List<(float, SpeedLadder)>();
        if (state.Blends.Count == 0) return arms;

        // one blend: forward only, and nothing above it says otherwise
        if (state.Blends.Count == 1)
        {
            if (state.Blends[0].Node is hkbBlenderGenerator only)
                arms.Add((0f, SpeedLadder.FromBlender(only, project)));

            return arms;
        }

        // several: each is a weighted child of the compass, and the weight is the heading
        foreach (SpeedConsumer blend in state.Blends)
        {
            if (blend.Node is not hkbBlenderGenerator ladder) continue;
            if (walk.StepOf(ladder)?.Parent is not hkbBlenderGeneratorChild child) continue;

            arms.Add((child.m_weight, SpeedLadder.FromBlender(ladder, project)));
        }

        return arms;
    }
}
