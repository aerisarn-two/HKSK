using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;
using Xunit;
namespace HKSK.Tests;

// One row per attack: the flag, the nearest blender above its clips, the clips' travel and
// whether anything raises bAnimationDriven over them. For scoring rules outside.
public sealed class ZzLocoReach
{
    [CorpusFact]
    public void Look()
    {
        var cache = SkyrimCache.Load(Corpus.Root!);
        var shipped = AnimationSetDataFile.Load(Path.Combine(Corpus.Root!, "animationsetdatasinglefile.txt"));
        var L = new List<string> { "flag\tblendDepth\tlocoBlend\tspeedBlend\ttravel\tspeed\tlocoMax\tdriven\tproject\tevent" };

        foreach (var p in shipped.Projects)
        {
            var actor = cache.OpenActor(p.Stem);
            string? path = cache.FindProjectFile(p.Stem);
            if (actor is null || path is null) continue;
            var walk = ProjectWalk.Of(path);
            string folder = Path.GetDirectoryName(path)!;

            var vars2 = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string FileOf2(IHavokObject n) => walk.Steps.FirstOrDefault(x => ReferenceEquals(x.Node, n)).File ?? "";
            var roots = new Dictionary<string, hkbGenerator>(StringComparer.OrdinalIgnoreCase);
            var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbBehaviorGraph g)
                {
                    if (g.m_rootGenerator is not null) roots.TryAdd(s.File, g.m_rootGenerator);
                    vars2[s.File] = [.. g.m_data?.m_stringData?.m_variableNames ?? []];
                    var names = g.m_data?.m_stringData?.m_variableNames ?? [];
                    for (int i = 0; i < names.Count; i++)
                        if (string.Equals(names[i], "bAnimationDriven", StringComparison.OrdinalIgnoreCase)) indexOf[s.File] = i;
                }

            IEnumerable<IHavokObject> Kids(IHavokObject n)
            {
                if (n is hkbBehaviorReferenceGenerator r)
                {
                    string? q = HavokPath.Resolve(folder, r.m_behaviorName);
                    if (q is not null && roots.TryGetValue(Path.GetFullPath(q), out var root)) yield return root;
                    yield break;
                }
                foreach (var (_, _, c) in HavokEdges.Of(n)) yield return c;
            }

            var parents = new Dictionary<IHavokObject, List<IHavokObject>>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
                foreach (var kid in Kids(s.Node))
                {
                    if (!parents.TryGetValue(kid, out var list)) parents[kid] = list = [];
                    if (!list.Any(x => ReferenceEquals(x, s.Node))) list.Add(s.Node);
                }

            // scopes that raise bAnimationDriven
            var driven = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
            foreach (var s in walk.Steps)
            {
                if (!indexOf.TryGetValue(s.File, out int at) || s.Node is not hkbNode node) continue;
                if (!(node.m_variableBindingSet?.m_bindings ?? []).Any(b => b.m_variableIndex == at
                        && (b.m_memberPath.StartsWith("bIsActive", StringComparison.Ordinal) || b.m_memberPath == "isActive"))) continue;

                IHavokObject? scope = node as hkbStateMachine;
                if (scope is null)
                {
                    IHavokObject at2 = node;
                    for (int i = 0; i < 8 && scope is null; i++)
                    {
                        var up = parents.GetValueOrDefault(at2)?.FirstOrDefault();
                        if (up is null) break;
                        if (up is hkbModifierGenerator mg) scope = mg.m_generator;
                        at2 = up;
                    }
                }
                if (scope is null) continue;

                var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance);
                var st = new Stack<IHavokObject>(); st.Push(scope);
                while (st.Count > 0)
                {
                    var n = st.Pop();
                    if (!seen.Add(n)) continue;
                    if (n is hkbClipGenerator) driven.Add(n);
                    foreach (var c in Kids(n)) st.Push(c);
                }
            }

            var byName = new Dictionary<string, List<hkbClipGenerator>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in walk.Steps)
                if (s.Node is hkbClipGenerator c && c.m_name is not null)
                {
                    if (!byName.TryGetValue(c.m_name, out var l)) byName[c.m_name] = l = [];
                    l.Add(c);
                }

            var travelOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(c => c.Slot?.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            // a clip's average speed: how far the root went, over how long the curve runs
            static float SpeedOf(HKSK.Model.Clip c) =>
                c.Slot?.Motion is { Duration: > 0.01f } m ? m.Travel / m.Duration : 0f;

            var speedOf = actor.Clips.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(SpeedOf), StringComparer.OrdinalIgnoreCase);

            // the fastest thing the creature does that is not an attack: its top locomotion speed
            var attackClips = p.Sets.Sets.SelectMany(x => x.Attacks.Attacks).SelectMany(x => x.Clips)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            float locoMax = actor.Clips.Where(c => !attackClips.Contains(c.Name)).Select(SpeedOf).DefaultIfEmpty(0f).Max();

            var seenEv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in p.Sets.Sets.SelectMany(s => s.Attacks.Attacks))
            {
                if (!seenEv.Add(a.EventName)) continue;
                var clips = a.Clips.SelectMany(n => byName.GetValueOrDefault(n) ?? []).ToList();
                if (clips.Count == 0) continue;

                int best = 99;
                foreach (var clip in clips)
                {
                    var q = new Queue<(IHavokObject Node, int Depth)>();
                    var seen = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                    q.Enqueue((clip, 0));
                    while (q.Count > 0)
                    {
                        (IHavokObject at, int d) = q.Dequeue();
                        if (d >= best || d > 16) continue;
                        foreach (var up in parents.GetValueOrDefault(at) ?? [])
                        {
                            if (!seen.Add(up)) continue;
                            if (up is hkbBlenderGenerator) best = Math.Min(best, d + 1);
                            else q.Enqueue((up, d + 1));
                        }
                    }
                }

                // a blender ancestor, at any distance, whose other arm holds a movement tree
                int locoBlend = 99;
                foreach (var clip in clips)
                {
                    var q2 = new Queue<(IHavokObject Node, int Depth)>();
                    var seen2 = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                    q2.Enqueue((clip, 0));
                    while (q2.Count > 0)
                    {
                        (IHavokObject at, int d) = q2.Dequeue();
                        if (d > 24) continue;
                        foreach (var up in parents.GetValueOrDefault(at) ?? [])
                        {
                            if (up is hkbBlenderGenerator bl && d + 1 < locoBlend)
                            {
                                foreach (var arm in bl.m_children)
                                {
                                    if (arm is null || ReferenceEquals(arm, at)) continue;
                                    var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    var s3 = new Stack<IHavokObject>(); s3.Push(arm);
                                    var seen3 = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { at };
                                    while (s3.Count > 0 && moving.Count < 4)
                                    {
                                        var n3 = s3.Pop();
                                        if (!seen3.Add(n3)) continue;
                                        if (n3 is hkbClipGenerator c3 && travelOf.GetValueOrDefault(c3.m_name) > 50f) moving.Add(c3.m_name);
                                        foreach (var k3 in Kids(n3)) s3.Push(k3);
                                    }
                                    if (moving.Count >= 4) { locoBlend = d + 1; break; }
                                }
                            }
                            if (!seen2.Add(up)) continue;
                            q2.Enqueue((up, d + 1));
                        }
                    }
                }

                // an ancestor blender whose blend parameter is bound to a speed variable:
                // the clip that plays, and so the attack's travel, is chosen by the actor's speed
                int speedBlend = 99;
                foreach (var clip in clips)
                {
                    var q3 = new Queue<(IHavokObject Node, int Depth)>();
                    var seen4 = new HashSet<IHavokObject>(ReferenceEqualityComparer.Instance) { clip };
                    q3.Enqueue((clip, 0));
                    while (q3.Count > 0)
                    {
                        (IHavokObject at, int d) = q3.Dequeue();
                        if (d > 24 || d >= speedBlend) continue;
                        foreach (var up in parents.GetValueOrDefault(at) ?? [])
                        {
                            if (up is hkbBlenderGenerator bl2)
                            {
                                var names2 = vars2.GetValueOrDefault(FileOf2(up)) ?? [];
                                foreach (var b in bl2.m_variableBindingSet?.m_bindings ?? [])
                                    if (b.m_memberPath == "blendParameter" && b.m_variableIndex >= 0 && b.m_variableIndex < names2.Length
                                        && names2[b.m_variableIndex].Contains("Speed", StringComparison.OrdinalIgnoreCase))
                                        speedBlend = Math.Min(speedBlend, d + 1);
                            }
                            if (seen4.Add(up)) q3.Enqueue((up, d + 1));
                        }
                    }
                }

                var named = a.Clips.Where(travelOf.ContainsKey).ToList();
                float travel = named.Count == 0 ? -1f : named.Max(c => travelOf[c]);
                bool isDriven = clips.Any(driven.Contains);

                float speed = named.Count == 0 ? -1f : named.Max(c => speedOf.GetValueOrDefault(c));
                L.Add($"{(a.MovingAttack != 0 ? 1 : 0)}\t{best}\t{locoBlend}\t{speedBlend}\t{travel:0.0}\t{speed:0.0}\t{locoMax:0.0}\t{(isDriven ? 1 : 0)}\t{p.Stem}\t{a.EventName}");
            }
        }

        File.WriteAllText("/tmp/claude-1000/-home-ecanepa-Dev-SKAssets/db8ab7eb-aee0-4d86-8209-2ec7ecaa14cd/scratchpad/asd/locoreach.tsv", string.Join("\n", L));
    }
}
