using System.Text;
using HKSK.Behavior;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Does the animation cache hold root motion for the dragon's flight clips, and does the
// graph ever raise bAnimationDriven under them? Grouped by the flight state each clip sits in.
public sealed class ZzFlightMotion
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var actor = cache.OpenActor("DragonProject")!;
        var walk = ProjectWalk.Of(actor.ProjectFile!.File.Path);
        var sb = new StringBuilder();
        var rows = new List<(string state, string clip, string anim, float travel, float turn, bool has, float dur)>();
        foreach (var s in walk.Steps)
        {
            if (s.Node is not hkbClipGenerator c) continue;
            var states = walk.Ancestors(c).Select(a => a.Node).OfType<hkbStateMachineStateInfo>().Select(x => x.m_name).Reverse().ToList();
            string top = states.Count > 1 && states[0] == "ST_Default" ? states[1] : states.FirstOrDefault() ?? "-";
            var m = actor.MotionOf(c);
            rows.Add((top, c.m_name, Path.GetFileName(c.m_animationName), m?.Travel ?? 0, m?.Turn ?? 0, m?.HasMovement ?? false, m?.Duration ?? 0));
        }
        foreach (var g in rows.GroupBy(r => r.state).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"## {g.Key}: {g.Count()} clips, {g.Count(r => r.has)} with root motion, {g.Count(r => r.travel > 1)} travelling >1");
            foreach (var r in g.Where(r => r.has).OrderByDescending(r => r.travel).Take(12))
                sb.AppendLine($"   {r.anim,-44} travel={r.travel,8:F1} turn={r.turn,6:F1} dur={r.dur:F2}");
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath(), "flightmotion.txt"), sb.ToString());
    }
}
