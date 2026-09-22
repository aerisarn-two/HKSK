using System.Text;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Two open points of docs/creature-patterns-research.md §8: what a victim-side paired
// kill state is made of, and how the four-arm creatures' speed-multiplier locomotion
// meets the speed table.
public sealed class ZzOpenPoints
{
    private static readonly string Out =
        Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.Combine(Path.GetTempPath(), "census");

    [CorpusFact]
    public void Look()
    {
        Directory.CreateDirectory(Out);
        var cache = SkyrimCache.Load(Corpus.Root!);
        var speed = SpeedDataFile.Load(Path.Combine(Corpus.Root!, SpeedDataFile.FileName));
        var sb = new StringBuilder();

        sb.AppendLine("=== BSSynchronizedClipGenerator per project: name | prefix | lead | reorient | motionFromRoot | bindIdx | clip anim | entering events | clip triggers | cache events");
        foreach (var actor in cache.Actors())
        {
            if (actor.ProjectFile is null) continue;
            var walk = ProjectWalk.Of(actor.ProjectFile.File.Path);
            if (walk.Steps.Count == 0) continue;
            var events = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph { m_data.m_stringData: { } sd }) events.TryAdd(s.File, sd.m_eventNames);
            string Ev(string f, int id) => id >= 0 && events.TryGetValue(f, out var e) && id < e.Count ? e[id] : $"#{id}";

            foreach (var s in walk.Steps)
            {
                if (s.Node is not BSSynchronizedClipGenerator sync) continue;
                var clip = sync.m_pClipGenerator as hkbClipGenerator;
                // entering events: transitions whose target is the state holding this generator (or an ancestor state)
                var entering = new List<string>();
                foreach (var a in walk.Ancestors(sync))
                {
                    if (a.Node is not hkbStateMachineStateInfo si) continue;
                    var sm = walk.Nearest<hkbStateMachine>(si);
                    if (sm is null) continue;
                    var smStep = walk.StepOf(sm)!.Value;
                    foreach (var st in sm.m_states)
                        foreach (var t in st.m_transitions?.m_transitions ?? [])
                            if (t.m_toStateId == si.m_stateId) entering.Add(Ev(smStep.File, t.m_eventId));
                    foreach (var t in sm.m_wildcardTransitions?.m_transitions ?? [])
                        if (t.m_toStateId == si.m_stateId) entering.Add("*" + Ev(smStep.File, t.m_eventId));
                    if (entering.Count > 0) break;
                }
                var triggers = clip?.m_triggers?.m_triggers.Select(t => Ev(s.File, t.m_event.m_id)) ?? [];
                var cached = clip is null ? null : actor.Clips.FirstOrDefault(c => c.Name == clip.m_name);
                sb.AppendLine($"{actor.Name} | {sync.m_name} | '{sync.m_SyncAnimPrefix}' | lead={sync.m_bLeadCharacter} | reorient={sync.m_bReorientSupportChar} | rootMotion={sync.m_bApplyMotionFromRoot} | bind={sync.m_sAnimationBindingIndex} | {Path.GetFileName(clip?.m_animationName ?? "?")} | enter=[{string.Join(",", entering.Distinct())}] | trig=[{string.Join(",", triggers)}] | cache=[{string.Join(",", cached?.Events.Select(e => e.Name) ?? [])}]");
            }
        }

        sb.AppendLine("\n=== speed table blocks vs sampler, for every creature");
        foreach (var actor in cache.Actors())
        {
            if (actor.ProjectFile is null) continue;
            var walk = ProjectWalk.Of(actor.ProjectFile.File.Path);
            bool sampler = walk.Steps.Any(s => s.Node is BSSpeedSamplerModifier);
            var block = speed.Block(actor.Name);
            string keys = block is null ? "-" : string.Join(",", block.Entries.Select(e => $"{e.Key}:{e.Records.Count}rec"));
            // clips whose playbackSpeed is bound to a variable: which variables
            var bound = new HashSet<string>();
            var vars = new ProjectVariables(walk.Steps);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_variableBindingSet is { } set)
                    foreach (var b in set.m_bindings)
                        if (b.m_memberPath.Contains("playbackSpeed", StringComparison.OrdinalIgnoreCase))
                            bound.Add(vars.NameOf(s, b) ?? $"#{b.m_variableIndex}");
            sb.AppendLine($"{actor.Name,-30} sampler={sampler,-5} block={(block is null ? "none" : keys),-40} playbackSpeed<-[{string.Join(",", bound.Order())}]");
        }

        File.WriteAllText(Path.Combine(Out, "openpoints.txt"), sb.ToString());
    }
}
