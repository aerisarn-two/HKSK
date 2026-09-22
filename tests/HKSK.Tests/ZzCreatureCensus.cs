using System.Text;
using System.Text.Json;
using HKSK.Behavior;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// Dumps, per creature project, everything a cross-project pattern census needs: the files
// the graph spans, each file's variables and events, the character's properties and
// animation list, the state-machine tree with transitions, and every clip with its state
// path and triggers. Analysis happens outside, over the dumps.
public sealed class ZzCreatureCensus
{
    private static readonly string Out =
        Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.Combine(Path.GetTempPath(), "census");

    [CorpusFact]
    public void Dump()
    {
        Directory.CreateDirectory(Out);
        var cache = SkyrimCache.Load(Corpus.Root!);
        var opts = new JsonSerializerOptions { WriteIndented = false };

        foreach (var actor in cache.Actors())
        {
            if (actor.ProjectFile is null || actor.Character is null) continue;
            string projectPath = actor.ProjectFile.File.Path;
            string folder = Path.GetDirectoryName(projectPath)!;
            var walk = ProjectWalk.Of(projectPath);
            if (walk.Steps.Count == 0) continue;

            // per-file string tables
            var events = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            var vars = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            var files = new List<object>();
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbBehaviorGraph g || g.m_data?.m_stringData is not { } sd) continue;
                if (events.ContainsKey(s.File)) continue;
                events[s.File] = sd.m_eventNames;
                vars[s.File] = sd.m_variableNames;
                var infos = g.m_data.m_variableInfos;
                var init = g.m_data.m_variableInitialValues;
                var variables = new List<object>();
                for (int i = 0; i < sd.m_variableNames.Count; i++)
                {
                    sbyte type = i < infos.Count ? infos[i].m_type : (sbyte)-1;
                    object? value = null;
                    if (init is not null && i < init.m_wordVariableValues.Count)
                    {
                        int raw = init.m_wordVariableValues[i].m_value;
                        value = type == 4 ? BitConverter.Int32BitsToSingle(raw) : raw;
                    }
                    variables.Add(new { name = sd.m_variableNames[i], type, value });
                }
                files.Add(new
                {
                    file = Rel(s.File),
                    inProjectFolder = s.File.StartsWith(folder, StringComparison.OrdinalIgnoreCase),
                    root = g.m_rootGenerator?.GetType().Name,
                    rootName = (g.m_rootGenerator as hkbNode)?.m_name,
                    variables,
                    events = sd.m_eventNames,
                    characterProperties = sd.m_characterPropertyNames,
                });
            }

            string EventName(string file, int id) =>
                id < 0 ? "-" : events.TryGetValue(file, out var e) && id < e.Count ? e[id] : $"#{id}";
            string VarName(string file, int id) =>
                id < 0 ? "-" : vars.TryGetValue(file, out var v) && id < v.Count ? v[id] : $"#{id}";

            // character
            var cd = actor.Character.Data;
            var props = new List<object>();
            var pn = cd.m_stringData?.m_characterPropertyNames ?? [];
            for (int i = 0; i < pn.Count; i++)
            {
                sbyte type = i < cd.m_characterPropertyInfos.Count ? cd.m_characterPropertyInfos[i].m_type : (sbyte)-1;
                int? v = cd.m_characterPropertyValues is { } pv && i < pv.m_wordVariableValues.Count ? pv.m_wordVariableValues[i].m_value : null;
                props.Add(new { name = pn[i], type, value = v });
            }

            // state path of a node: the (machine, state) names above it, outermost first
            string StatePath(IHavokObject node)
            {
                var parts = new List<string>();
                foreach (var a in walk.Ancestors(node))
                    if (a.Node is hkbStateMachineStateInfo si) parts.Add(si.m_name);
                parts.Reverse();
                return string.Join(" / ", parts);
            }

            // tree
            var tree = new StringBuilder();
            foreach (var s in walk.Steps)
            {
                string pad = new string(' ', s.Depth);
                switch (s.Node)
                {
                    case hkbStateMachine sm:
                        tree.AppendLine($"{pad}SM '{sm.m_name}' start={sm.m_startStateId} mode={sm.m_startStateMode} sync={VarName(s.File, sm.m_syncVariableIndex)} [{s.FileName}]");
                        if (sm.m_wildcardTransitions is { } wt)
                            foreach (var t in wt.m_transitions)
                                tree.AppendLine($"{pad} *on {EventName(s.File, t.m_eventId)} -> S{t.m_toStateId}{Cond(t)}");
                        break;
                    case hkbStateMachineStateInfo si:
                        tree.AppendLine($"{pad}S{si.m_stateId} '{si.m_name}'{(si.m_enable ? "" : " DISABLED")}");
                        if (si.m_transitions is { } tr)
                            foreach (var t in tr.m_transitions)
                                tree.AppendLine($"{pad} on {EventName(s.File, t.m_eventId)} -> S{t.m_toStateId}{Cond(t)}");
                        if (si.m_enterNotifyEvents is { } en && en.m_events.Count > 0)
                            tree.AppendLine($"{pad} enter: {string.Join(",", en.m_events.Select(e => EventName(s.File, e.m_id)))}");
                        if (si.m_exitNotifyEvents is { } ex && ex.m_events.Count > 0)
                            tree.AppendLine($"{pad} exit: {string.Join(",", ex.m_events.Select(e => EventName(s.File, e.m_id)))}");
                        break;
                    case hkbClipGenerator c:
                        tree.AppendLine($"{pad}CLIP '{c.m_name}' = {c.m_animationName} mode={c.m_mode}");
                        break;
                    case hkbBehaviorReferenceGenerator r:
                        tree.AppendLine($"{pad}REF '{r.m_name}' -> {r.m_behaviorName}");
                        break;
                    case hkbBlenderGenerator b:
                        tree.AppendLine($"{pad}BLEND '{b.m_name}' flags={b.m_flags} n={b.m_children.Count} [{Bind(s, b, VarName)}]");
                        break;
                    case hkbBlenderGeneratorChild bc:
                        tree.AppendLine($"{pad}child w={bc.m_weight}");
                        break;
                    case hkbManualSelectorGenerator ms:
                        tree.AppendLine($"{pad}SELECT '{ms.m_name}' n={ms.m_generators.Count} sel={ms.m_selectedGeneratorIndex} [{Bind(s, ms, VarName)}]");
                        break;
                    case BSiStateTaggingGenerator tg:
                        tree.AppendLine($"{pad}TAG '{tg.m_name}' iState={tg.m_iStateToSetAs}");
                        break;
                    case hkbModifierGenerator mg:
                        tree.AppendLine($"{pad}MODGEN '{mg.m_name}'");
                        break;
                    case hkbBehaviorGraph:
                        tree.AppendLine($"{pad}GRAPH [{s.FileName}]");
                        break;
                    case hkbGenerator g:
                        tree.AppendLine($"{pad}{g.GetType().Name} '{g.m_name}'");
                        break;
                    case hkbModifier m:
                        tree.AppendLine($"{pad}mod {m.GetType().Name} '{m.m_name}'{Expr(m)} [{Bind(s, m, VarName)}]");
                        break;
                }
            }

            string Cond(hkbStateMachineTransitionInfo t) =>
                t.m_condition switch
                {
                    hkbExpressionCondition ec => $" if({ec.m_expression})",
                    hkbStringCondition sc => $" if\"{sc.m_conditionString}\"",
                    null => "",
                    var c => $" if<{c.GetType().Name}>",
                };

            // clips
            var clips = new List<object>();
            foreach (var s in walk.Steps)
            {
                if (s.Node is not hkbClipGenerator c) continue;
                var triggers = c.m_triggers?.m_triggers.Select(t => new
                {
                    e = EventName(s.File, t.m_event.m_id),
                    t = t.m_localTime,
                    end = t.m_relativeToEndOfClip,
                }).ToList();
                var anc = walk.Ancestors(c.Node()).Select(a => a.Node).ToList();
                clips.Add(new
                {
                    name = c.m_name,
                    anim = c.m_animationName,
                    mode = c.m_mode,
                    speed = c.m_playbackSpeed,
                    file = s.FileName,
                    state = StatePath(c),
                    key = walk.KeyOf(c),
                    sync = anc.Any(a => a is BSSynchronizedClipGenerator),
                    inBlend = anc.Any(a => a is hkbBlenderGenerator),
                    triggers,
                });
            }

            // class census
            var classes = walk.Steps.GroupBy(s => s.Node.GetType().Name)
                .Where(g => g.Key.StartsWith("hkb") || g.Key.StartsWith("BS"))
                .ToDictionary(g => g.Key, g => g.Count());

            var expressions = walk.Steps.Select(s => s.Node).OfType<hkbExpressionData>()
                .Select(e => e.m_expression).Where(e => e.Length > 0).ToList();

            var doc = new
            {
                project = actor.Name,
                projectFile = Rel(projectPath),
                character = new
                {
                    name = actor.Character.Name,
                    rig = actor.Character.RigName,
                    ragdoll = actor.Character.RagdollName,
                    behavior = actor.Character.BehaviorFilename,
                    scale = cd.m_scale,
                    animations = actor.Character.AnimationNames,
                    properties = props,
                },
                files,
                clips,
                classes,
                expressions,
                sets = actor.Sets is null ? null : "present",
            };
            File.WriteAllText(Path.Combine(Out, actor.Name + ".json"), JsonSerializer.Serialize(doc, opts));
            File.WriteAllText(Path.Combine(Out, actor.Name + ".tree.txt"), tree.ToString());
        }
    }

    private static string Rel(string path)
    {
        string root = Corpus.Root!;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path[root.Length..].TrimStart('/', '\\') : path;
    }

    private static string Expr(hkbModifier m) => m switch
    {
        hkbEvaluateExpressionModifier ee => " {" + string.Join("; ", ee.m_expressions?.m_expressionsData.Select(e => e.m_expression) ?? []) + "}",
        _ => "",
    };

    private static string Bind(ProjectStep s, hkbBindable b, Func<string, int, string> varName) =>
        b.m_variableBindingSet is { } set
            ? string.Join(", ", set.m_bindings.Select(x => $"{x.m_memberPath}<-{(x.m_bindingType == 1 ? "prop:" : "")}{varName(s.File, x.m_variableIndex)}"))
            : "";
}

internal static class ZzExt
{
    public static IHavokObject Node(this hkbClipGenerator c) => c;
}
