using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Engine;
using HKSK.Model;
using HKX2;
using Xunit;

namespace HKSK.Tests;

public sealed class FlatFlyerDump
{
    private static readonly Events Moving = Events.Of("moveStart", "moveForward");

    [MastersFact]
    public void Look()
    {
        SkyrimCache cache = SkyrimCache.Load(Corpus.Root!);
        List<string> lines = [];

        foreach (string name in new[]
        {
            "DragonProject", "IceWraithProject", "AtronachStormProject",
            "WispProject", "WitchlightProject", "ChaurusFlyer",
            "AtronachFlame", "Dragon_Priest",
        })
        {
            if (cache.OpenActor(name) is not ActorProject actor) { lines.Add($"{name}: no actor"); continue; }
            if (cache.FindProjectFile(name) is not { } path) { lines.Add($"{name}: no project"); continue; }

            SpeedEntry e = cache.SpeedData!.Block(name)!.Entries.First(x => x.Records.Count > 0);
            var byHeading = e.Records.Select(r => r.Points.Count > 0 ? r.Points[^1].Y : 0f).ToList();
            lines.Add($"=== {name}: shipped per heading {string.Join(" ", byHeading.Select(v => $"{v:0.##}"))}");
            float biggest = e.Records.SelectMany(r => r.Points).Select(q => q.Y).DefaultIfEmpty(0f).Max();
            int nonzero = e.Records.SelectMany(r => r.Points).Count(q => q.Y > 0f);
            lines.Add($"    largest shipped y {biggest:E6} over {nonzero} strictly-positive of " +
                      $"{e.Records.Sum(r => r.Points.Count)} points");

            ProjectWalk walk = ProjectWalk.Of(path);
            hkbBehaviorGraph graph = walk.Steps.Select(s => s.Node).OfType<hkbBehaviorGraph>().First();
            Properties properties = Properties.OfProject(path);

            Evaluation run = ActiveGenerators.Evaluate(graph, walk, tables =>
            {
                foreach (Variables v in tables.Values)
                {
                    v.Set("iSyncIdleLocomotion", 1);
                    v.Set("iSyncForwardState", 0);
                    v.Set("iSyncTurnState", 1);
                    v.Set("Direction", 0f);
                    v.Set("Speed", 200f);
                    v.Set("SpeedDamped", 200f);
                }
            }, properties, Moving);

            foreach (ActiveNode n in run.Active)
            {
                if (n.Generator is not hkbClipGenerator clip) continue;
                if (clip.m_animationName is not { Length: > 0 } a) continue;
                string stem = Path.GetFileNameWithoutExtension(a.Replace('\\', '/'));
                ClipMovement? m = actor.Animation(stem)?.Motion;
                float d = m is null ? 0f : m.Duration / Math.Max(Math.Abs(clip.m_playbackSpeed), 1e-4f);
                float v = m is { Translations.Count: > 0 } && d > 0f ? m.Translations[^1].Value.Length() / d : 0f;
                lines.Add($"    live clip '{clip.m_name}' -> {stem}  motion={n.Motion:0.###}  {v:0.###} u/s");
            }

            // and every animation in the project that travels near the shipped value
            int travellers = actor.Animations.Count(sl =>
                sl.Motion is { Duration: > 0f, Translations.Count: > 0 } mm &&
                mm.Translations[^1].Value.Length() > 0f);
            lines.Add($"    {travellers} of {actor.Animations.Count} animations in the project travel");
            foreach (AnimationSlot sl in actor.Animations)
                if (sl.Motion is { Duration: > 0f, Translations.Count: > 0 } mm &&
                    mm.Translations[^1].Value.Length() > 0f)
                    lines.Add($"        travels: {sl.FileStem} {mm.Translations[^1].Value.Length() / mm.Duration:0.###} u/s");

            float want = byHeading.Max();
            foreach (AnimationSlot slot in actor.Animations)
            {
                if (slot.Motion is not { Duration: > 0f } m || m.Translations.Count == 0) continue;
                float v = m.Translations[^1].Value.Length() / m.Duration;
                if (want > 0 && Math.Abs(v - want) <= 0.02f * want)
                    lines.Add($"    a clip that travels at {want:0.##}: {slot.FileStem} ({v:0.###} u/s)");
            }
        }

        File.WriteAllText("/tmp/flatflyers.txt", string.Join("\n", lines));
    }
}
