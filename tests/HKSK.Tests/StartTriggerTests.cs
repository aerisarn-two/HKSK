using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

/// <summary>
/// A clip's triggers at local time 0 have fired by the time the graph is at rest.
/// </summary>
/// <remarks>
/// The end-of-clip case is the obvious one and was implemented first: a clip that
/// has been playing has reached its end. The start case is the same argument from
/// the other side -- a clip that is active was entered, so a trigger sitting at its
/// first frame has fired too -- and without it the ice wraith never leaves its
/// intro. <c>IceWraithRootBehavior</c> starts in <c>Initialize</c> and its only exit
/// is event 32, <c>InitializeStop</c>, which the <c>Initialize</c> clip raises with
/// <c>m_relativeToEndOfClip</c> false at <c>m_localTime</c> 0.
/// </remarks>
public sealed class StartTriggerTests
{
    private static readonly Events Moving = Events.Of("moveStart", "moveForward");

    [MastersFact]
    public void TheIceWraithLeavesItsIntroAndReachesItsCompass()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        string path = cache.FindProjectFile("IceWraithProject")!;
        ProjectWalk walk = ProjectWalk.Of(path);
        hkbBehaviorGraph graph = walk.Steps.Select(s => s.Node).OfType<hkbBehaviorGraph>().First();

        // The trigger this rests on, read off the file rather than assumed.
        hkbClipGenerator intro = walk.Steps.Select(s => s.Node).OfType<hkbClipGenerator>()
            .First(c => c.m_name == "Initialize");
        hkbClipTrigger trigger = Assert.Single(intro.m_triggers!.m_triggers!);
        Assert.False(trigger.m_relativeToEndOfClip);
        Assert.Equal(0f, trigger.m_localTime);

        Evaluation run = ActiveGenerators.Evaluate(graph, walk, tables =>
        {
            foreach (Variables variables in tables.Values)
            {
                variables.Set("iSyncIdleLocomotion", 1);
                variables.Set("iSyncForwardState", 0);
                variables.Set("iSyncTurnState", 1);
                variables.Set("Direction", 0f);
                variables.Set("Speed", 100f);
            }
        }, Properties.OfProject(path), Moving);

        Assert.DoesNotContain(run.Active, n => n.Generator == intro);

        // What it reaches instead: the four-arm compass its shipped table describes,
        // every arm a clip the cache records at 319.667 units a second.
        hkbBlenderGenerator compass = Assert.Single(
            run.Active.Select(n => n.Generator).OfType<hkbBlenderGenerator>()
                .Where(b => (b.m_flags & 16) != 0 && b.m_children?.Count == 4));

        Assert.Equal(["RunF", "RunR", "RunB", "RunL"],
            compass.m_children!.Select(c => c.m_generator?.m_name));
    }
}
